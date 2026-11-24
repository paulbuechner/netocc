# SPDX-FileCopyrightText: 2026 Paul Büchner
# SPDX-License-Identifier: MIT

"""What the native libraries of a stage load: each file's dependency records (a PE file's imports, delay-load ones
included; an ELF file's DT_NEEDED entries; a Mach-O file's dylib load commands), read in plain Python so a pack reads
every RID's stage on any host, and the MSBuild items the runtime packages' targets select natives by: each native with
all it loads, the module libraries marked."""
from __future__ import annotations

import re
import struct
from pathlib import Path
from xml.sax.saxutils import quoteattr


# --------------------------------------------------------------------------- dependency records
def needed(path: Path) -> list[str]:
    """The file names a shared library asks the loader for, in its order (a Mach-O install name's last part)."""
    data = path.read_bytes()
    if data[:2] == b"MZ":
        return pe_imports(data)
    if data[:4] == b"\x7fELF":
        return elf_needed(data)
    if data[:4] in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xfe\xed\xfa\xce",
                    b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf"):
        return macho_dylibs(data)
    raise ValueError(f"{path.name} is no PE, ELF or Mach-O file")


def pe_imports(data: bytes) -> list[str]:
    """The DLLs of the import table and the delay-load import table."""
    header = struct.unpack_from("<I", data, 0x3C)[0]
    if data[header:header + 4] != b"PE\0\0":
        raise ValueError("no PE header")
    sections, optional_size = struct.unpack_from("<H", data, header + 6)[0], struct.unpack_from("<H", data, header + 20)[0]
    optional = header + 24
    pe32_plus = struct.unpack_from("<H", data, optional)[0] == 0x20B
    image_base = struct.unpack_from("<Q", data, optional + 24)[0] if pe32_plus else struct.unpack_from("<I", data, optional + 28)[0]
    directories = optional + (112 if pe32_plus else 96)
    directory_count = struct.unpack_from("<I", data, directories - 4)[0]
    table = [struct.unpack_from("<IIII", data, optional + optional_size + 40 * i + 8) for i in range(sections)]

    def offset(rva: int) -> int:
        for size, address, raw_size, raw in table:
            if address <= rva < address + max(size, raw_size):
                return rva - address + raw
        raise ValueError(f"RVA {rva:#x} in no section")

    def name(rva: int) -> str:
        start = offset(rva)
        return data[start:data.index(b"\0", start)].decode("ascii")

    names: list[str] = []
    # IMAGE_DIRECTORY_ENTRY_IMPORT: 20-byte descriptors, the name's RVA at 12, ended by a zero one
    imports = struct.unpack_from("<I", data, directories + 8)[0] if directory_count > 1 else 0
    position = offset(imports) if imports else None
    while position is not None and (rva := struct.unpack_from("<I", data, position + 12)[0]):
        names.append(name(rva))
        position += 20
    # IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT: 32-byte descriptors, attributes at 0 (bit 0 clear: addresses, not RVAs), the
    # name at 4
    delayed = struct.unpack_from("<I", data, directories + 13 * 8)[0] if directory_count > 13 else 0
    position = offset(delayed) if delayed else None
    while position is not None and (address := struct.unpack_from("<I", data, position + 4)[0]):
        attributes = struct.unpack_from("<I", data, position)[0]
        names.append(name(address if attributes & 1 else address - image_base))
        position += 32
    return names


def elf_needed(data: bytes) -> list[str]:
    """The DT_NEEDED entries of the dynamic segment, their strings from DT_STRTAB."""
    wide, order = data[4] == 2, "<" if data[5] == 1 else ">"
    if wide:
        table, entry_size, count = struct.unpack_from(order + "Q", data, 0x20)[0], *struct.unpack_from(order + "HH", data, 0x36)
        header, dynamic_entry = order + "IIQQQQQQ", order + "qQ"
    else:
        table, entry_size, count = struct.unpack_from(order + "I", data, 0x1C)[0], *struct.unpack_from(order + "HH", data, 0x2A)
        header, dynamic_entry = order + "IIIIIIII", order + "iI"
    segments = []
    for i in range(count):
        fields = struct.unpack_from(header, data, table + i * entry_size)
        # (type, file offset, address, size in the file); 64-bit: type, flags, offset, address, ...; 32-bit: type, offset,
        # address, physical address, size, ...
        segments.append((fields[0], fields[2], fields[3], fields[5]) if wide else (fields[0], fields[1], fields[2], fields[4]))
    dynamic = next((s for s in segments if s[0] == 2), None)  # PT_DYNAMIC
    if dynamic is None:
        return []
    entries, strings = [], None
    for position in range(dynamic[1], dynamic[1] + dynamic[3], struct.calcsize(dynamic_entry)):
        tag, value = struct.unpack_from(dynamic_entry, data, position)
        if tag == 0:  # DT_NULL
            break
        if tag == 1:  # DT_NEEDED
            entries.append(value)
        elif tag == 5:  # DT_STRTAB, an address: the PT_LOAD segment holding it gives its file offset
            strings = next(offset + value - address for kind, offset, address, size in segments
                           if kind == 1 and address <= value < address + size)
    if strings is None:
        return []
    return [data[strings + e:data.index(b"\0", strings + e)].decode("utf-8") for e in entries]


# LC_LOAD_DYLIB, LC_LOAD_WEAK_DYLIB, LC_REEXPORT_DYLIB, LC_LAZY_LOAD_DYLIB, LC_LOAD_UPWARD_DYLIB
MACHO_DYLIB_COMMANDS = {0xC, 0x80000018, 0x8000001F, 0x20, 0x80000023}


def macho_dylibs(data: bytes, start: int = 0) -> list[str]:
    """The dylibs the load commands name, of every architecture of a universal file."""
    magic = data[start:start + 4]
    if magic in (b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf"):  # universal: big-endian table of the architectures' files
        wide = magic == b"\xca\xfe\xba\xbf"
        count = struct.unpack_from(">I", data, start + 4)[0]
        names: list[str] = []
        for i in range(count):
            offset = struct.unpack_from(">Q", data, start + 8 + 32 * i + 8)[0] if wide else \
                struct.unpack_from(">I", data, start + 8 + 20 * i + 8)[0]
            names += [n for n in macho_dylibs(data, offset) if n not in names]
        return names
    order = "<" if magic in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe") else ">"
    wide = magic in (b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf")
    count = struct.unpack_from(order + "I", data, start + 16)[0]
    position = start + (32 if wide else 28)
    names = []
    for _ in range(count):
        command, size = struct.unpack_from(order + "II", data, position)
        if command in MACHO_DYLIB_COMMANDS:
            name = position + struct.unpack_from(order + "I", data, position + 8)[0]
            names.append(data[name:data.index(b"\0", name)].decode("utf-8").rsplit("/", 1)[-1])
        position += size
    return names


# --------------------------------------------------------------------------- what each native loads
def closures(stage: Path) -> dict[str, list[str]]:
    """Each file of the stage with the files of the stage it loads, directly or through others, itself included (sorted,
    case-insensitively). Names match case-insensitively, as Windows' loader does; the stage's own spelling is kept."""
    files = {p.name.lower(): p.name for p in stage.iterdir() if p.is_file()}
    direct = {key: [d.lower() for d in needed(stage / name) if d.lower() in files] for key, name in files.items()}
    result = {}
    for key, name in files.items():
        seen, todo = set(), [key]
        while todo:
            if (current := todo.pop()) not in seen:
                seen.add(current)
                todo += direct[current]
        result[name] = sorted((files[k] for k in seen), key=str.lower)
    return dict(sorted(result.items(), key=lambda item: item[0].lower()))


def foreign(stage: Path) -> list[str]:
    """What the stage's files load that the stage doesn't hold: another package's libraries, and the system's."""
    files = {p.name.lower() for p in stage.iterdir() if p.is_file()}
    names = {d.lower(): d for p in sorted(stage.iterdir()) if p.is_file() for d in needed(p) if d.lower() not in files}
    return sorted(names.values(), key=str.lower)


def module_of(name: str, prefix: str) -> str | None:
    """The module whose library a file is (NetOccDataExchange.dll, libNetOccDataExchange.so or .dylib: DataExchange), or
    None. Module names have no dots, which a managed assembly next to them has (NetOcc.dll)."""
    match = re.fullmatch(rf"(?:lib)?{re.escape(prefix)}([A-Za-z0-9]+)\.(?:dll|so|dylib)", name)
    return match.group(1) if match else None


# --------------------------------------------------------------------------- the runtime package's data
def write(stage: Path, target: Path, package: str, prefix: str) -> None:
    """The items a runtime package's targets select natives by, as an MSBuild file: _<prefix>Native per file of the stage
    with Package, Loads (all it loads of the stage, itself included) and, for a module's library, Module. Exits on a stage
    without its runtime library (<prefix>Runtime)."""
    loads = closures(stage)
    modules = {name: module_of(name, prefix) for name in loads}
    if "Runtime" not in modules.values():
        raise SystemExit(f"no {prefix}Runtime library in {stage}")
    item = f"_{prefix}Native"
    lines = [f"    <{item} Include={quoteattr(name)} Package={quoteattr(package)}"
             + (f" Module={quoteattr(modules[name])}" if modules[name] else "")
             + f" Loads={quoteattr(';'.join(files))} />" for name, files in loads.items()]
    target.write_text(f"""<Project>
  <!--
    {package}'s natives, each with all it loads of them: itself, what it imports (delay-loaded too)
    and what those import. The {prefix} modules' libraries are marked.
    Written by build.py pack from the files (loads.py).
  -->
  <ItemGroup>
{chr(10).join(lines)}
  </ItemGroup>
</Project>
""", encoding="utf-8")


def write_foreign(stage: Path, target: Path, package: str, rid: str, items: list[str]) -> None:
    """For a package whose natives load another package's: what they load that the stage doesn't hold, as items of each
    name (the other packages' targets keep those natives and what they load), with Rid."""
    names = ";".join(foreign(stage))
    lines = [f"    <{item} Include={quoteattr(names)} Rid={quoteattr(rid)} />" for item in items]
    target.write_text(f"""<Project>
  <!--
    What {package}'s natives load that it doesn't hold, the system's libraries among them.
    Written by build.py pack from the files (loads.py).
  -->
  <ItemGroup>
{chr(10).join(lines)}
  </ItemGroup>
</Project>
""", encoding="utf-8")
