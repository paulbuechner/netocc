// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * Aspect companion: a window for a native handle. The platforms' window classes (WNT_Window, Xw_Window, Cocoa_Window) each
 * exist on one platform, and the generated modules serve all of them, so Aspect_Window gets one factory: an HWND on
 * Windows, an NSView on macOS, an X11 window on Linux (with the display connection, which may be null elsewhere). The
 * dispatch is verbatim C++: SWIG's preprocessor, which doesn't define _WIN32, would pick a branch in an %extend body.
 */
%{
#if defined(_WIN32)
  #include <WNT_Window.hxx>
#elif defined(__APPLE__)
  #include <Cocoa_Window.hxx>
#else
  #include <Xw_Window.hxx>
#endif

static opencascade::handle<Aspect_Window> NetOcc_WindowFromNativeHandle(void* theHandle,
                                                                       const opencascade::handle<Aspect_DisplayConnection>& theDisplay)
{
#if defined(_WIN32)
  (void)theDisplay;
  return new WNT_Window((Aspect_Handle)theHandle);
#elif defined(__APPLE__)
  (void)theDisplay;
  return new Cocoa_Window((NSView*)theHandle);
#else
  return new Xw_Window(theDisplay, (Aspect_Drawable)(uintptr_t)theHandle);
#endif
}
%}

%extend Aspect_Window {
  static opencascade::handle<Aspect_Window> FromNativeHandle(void* theHandle, const opencascade::handle<Aspect_DisplayConnection>& theDisplay) {
    return NetOcc_WindowFromNativeHandle(theHandle, theDisplay);
  }
}
