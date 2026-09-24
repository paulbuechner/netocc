// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OCC.Core;

/// <summary>
/// Loads the NetOcc native libraries (NetOcc&lt;Module&gt; with the wrappers of an OCCT module,
/// NetOccRuntime) and, through the OS loader, the OCCT libraries next to them, before the first
/// P/Invoke. Probed locations, relative to the app base directory, then to NetOcc.dll's own (a
/// plugin's; on .NET Framework also a shadow copy's original): <c>&lt;arch&gt;/</c> (AnyCPU .NET
/// Framework layout), <c>runtimes/&lt;rid&gt;/native/</c>, and the directory itself. The
/// netstandard2.0 build has no resolver: it preloads on Windows only and leaves other systems to
/// .NET's default probing.
/// </summary>
internal static class NativeLibraryLoader
{
  /// <summary>The hand-written native helpers (src/Native).</summary>
  internal const string RuntimeLibrary = "NetOccRuntime";

  private const string Prefix = "NetOcc";

  private const uint LoadWithAlteredSearchPath = 0x00000008;

  private const int ErrorModNotFound = 126;

  private static readonly object Sync = new();
  private static readonly Dictionary<string, IntPtr> Handles = [];

  // why a library found in a probed directory didn't load, by name
  private static readonly Dictionary<string, string> Failures = [];

  private static List<string> _directories;

  // CA2255: a library module initializer is the point here; it must run before any P/Invoke.
#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Initialize()
  {
#if NET5_0_OR_GREATER
    // a library that is there but doesn't load fails with the OS's reason (a dependency missing);
    // one that isn't there is left to the default probing
    NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, static (name, _, _) =>
    {
      if (!name.StartsWith(Prefix, StringComparison.Ordinal))
      {
        return IntPtr.Zero;
      }

      var handle = Load(name, out var failure);
      return handle != IntPtr.Zero || failure is null
        ? handle
        : throw new DllNotFoundException(failure);
    });
#else
    // .NET Framework and the netstandard2.0 build have no resolver, but DllImport resolves by
    // module name, so a preloaded module wins: every NetOcc library of the first directory that has
    // them
    if (IsWindows)
    {
      foreach (var directory in Directories())
      {
        if (!Directory.Exists(directory))
        {
          continue;
        }

        var libraries = Array.FindAll(Directory.GetFiles(directory, $"{Prefix}*.dll"), IsNative);
        if (libraries.Length == 0)
        {
          continue;
        }

        foreach (var library in libraries)
        {
          // DllImport then reports the library itself missing: tell why it didn't load
          if (Load(Path.GetFileNameWithoutExtension(library), out var failure) == IntPtr.Zero
              && failure is not null)
          {
            Trace.TraceError(failure);
          }
        }

        break;
      }
    }
#endif
  }

#if NETFRAMEWORK
  // .NET Framework runs on Windows only. RuntimeInformation arrived in 4.7.1, Is64BitProcess and
  // AppContext in 4.0 and 4.6; the net35 build needs the older equivalents.
  private static bool IsWindows => true;

  private static string ArchName() => IntPtr.Size == 8 ? "x64" : "x86";

  private static string OsName() => "win";

  private static string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;
#else
  private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

  private static string ArchName() => RuntimeInformation.ProcessArchitecture switch
  {
    Architecture.X86 => "x86",
    Architecture.X64 => "x64",
    Architecture.Arm64 => "arm64",
    var other => other.ToString().ToLowerInvariant(),
  };

  private static string OsName() => IsWindows ? "win" :
    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";

  private static string BaseDirectory => AppContext.BaseDirectory;
#endif

  // NetOcc<Module>, not a managed NetOcc.* assembly next to it (NetOcc.dll, NetOcc.Tests.dll):
  // native names have no dots
  private static bool IsNative(string file)
  {
    var name = Path.GetFileNameWithoutExtension(file);
    return name.Length > Prefix.Length && name.IndexOf('.') < 0;
  }

  // the library's handle, or IntPtr.Zero with the reason it didn't load (null when no probed
  // directory has it)
  private static IntPtr Load(string name, out string failure)
  {
    lock (Sync)
    {
      if (Handles.TryGetValue(name, out var handle))
      {
        Failures.TryGetValue(name, out failure);
        return handle;
      }

      failure = null;
      var file = OsName() switch
      {
        "win" => $"{name}.dll",
        "osx" => $"lib{name}.dylib",
        _ => $"lib{name}.so",
      };
      foreach (var directory in Directories())
      {
        var path = Path.Combine(directory, file);
        if (!File.Exists(path))
        {
          continue;
        }

        handle = LoadFile(path, out var error);
        if (handle != IntPtr.Zero)
        {
          failure = null;
          break;
        }

        failure ??= error;
      }

      Handles[name] = handle;
      if (failure is not null)
      {
        Failures[name] = failure;
      }

      return handle;
    }
  }

  private static IntPtr LoadFile(string path, out string error)
  {
    error = null;
    if (IsWindows)
    {
      // search the library's own directory for its dependencies (TK*.dll)
      var handle = LoadLibraryExW(path, IntPtr.Zero, LoadWithAlteredSearchPath);
      if (handle == IntPtr.Zero)
      {
        var code = Marshal.GetLastWin32Error();
        error = $"{path} doesn't load: {new Win32Exception(code).Message}"
                + (code == ErrorModNotFound
                  ? " A library it needs is missing, such as the Visual C++ 2015-2022 runtime."
                  : "");
      }

      return handle;
    }

#if NET5_0_OR_GREATER
    try
    {
      return NativeLibrary.Load(path);
    }
    catch (Exception e) when (e is DllNotFoundException or BadImageFormatException)
    {
      // dlopen's reason, which names a missing dependency
      error = e.Message;
      return IntPtr.Zero;
    }
#else
    return IntPtr.Zero;
#endif
  }

  // where the natives may be, in probing order: around the app, then around NetOcc.dll where that's
  // elsewhere
  private static List<string> Directories()
  {
    if (_directories is not null)
    {
      return _directories;
    }

    var arch = ArchName();
    var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    var directories = new List<string>();
    foreach (var root in Roots())
    {
      foreach (var directory in new[]
               {
                 Combine(root, arch), Combine(root, "runtimes", $"{OsName()}-{arch}", "native"),
                 root
               })
      {
        if (!directories.Exists(known => string.Equals(known, directory, comparison)))
        {
          directories.Add(directory);
        }
      }
    }

    return _directories = directories;
  }

  private static IEnumerable<string> Roots()
  {
    yield return Normalized(BaseDirectory);

    // empty when loaded from bytes or bundled into a single-file app
    var assembly = typeof(NativeLibraryLoader).Assembly;
    if (!string.IsNullOrEmpty(assembly.Location))
    {
      yield return Normalized(Path.GetDirectoryName(assembly.Location));
    }

#if NETFRAMEWORK
    // a shadow copy's original (NUnit, ASP.NET): the natives stay there
    if (Uri.TryCreate(assembly.CodeBase, UriKind.Absolute, out var codeBase) && codeBase.IsFile)
    {
      yield return Normalized(Path.GetDirectoryName(codeBase.LocalPath));
    }
#endif
  }

  // full, without a trailing separator (but a root's): BaseDirectory has one, Path.GetDirectoryName
  // doesn't
  private static string Normalized(string directory)
  {
    var full = Path.GetFullPath(directory);
    return full.Length > Path.GetPathRoot(full).Length
      ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
      : full;
  }

  // Path.Combine takes more than two parts only from .NET Framework 4.0 on.
  private static string Combine(string first, params string[] rest)
  {
    var path = first;
    foreach (var part in rest)
    {
      path = Path.Combine(path, part);
    }

    return path;
  }

  [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
  private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);
}
