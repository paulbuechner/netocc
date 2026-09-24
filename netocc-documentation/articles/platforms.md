# Platforms

## Frameworks

One AnyCPU assembly: .NET Framework 3.5, 4.5 to 4.8, .NET 6, 8 and 10, and netstandard2.0.

## Natives

`NetOcc` depends on one package of natives per platform, `NetOcc.runtime.<rid>`: NetOcc's native libraries and OCCT's.

| RID | Needs |
|---|---|
| win-x64, win-x86 | the Visual C++ 2015-2022 runtime |
| linux-x64 | glibc 2.35 or newer (Ubuntu 22.04) |
| osx-arm64 | macOS 14 or newer |

.NET apps load the natives from `runtimes/<rid>/native`. .NET Framework apps get them in `x64\` and `x86\` next to the app, so AnyCPU works; NetOcc picks the folder for the process.

## 32-bit

On win-x86, `size_t` is four bytes: a `ulong` above 32 bits throws `OcctException` (`Standard_OutOfRange`) instead of wrapping around, as C `long`, which is 32 bits on Windows, does everywhere.

## Views

The OpenGL driver draws into a window of the platform: an HWND, an X11 window with its display connection (`Aspect_DisplayConnection`), an NSView. The demos host an HWND, so they run on Windows.
