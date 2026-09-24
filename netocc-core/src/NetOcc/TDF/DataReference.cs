// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace OCC.Core.TDF;

/// <summary>
/// Native references on a TDF_Data that label, attribute and TNaming_Builder proxies hold, so the
/// OCAF tree outlives them in any finalization order (src/SWIG_files/extras/TDF.i, TNaming.i). Each
/// acquire returns the data with one reference added, or zero; release tolerates zero.
/// </summary>
internal static class DataReference
{
  // the native library of the ApplicationFramework module (src/SWIG_files/modules.json), with the
  // TDF and TNaming wrappers, whose companions define these as SWIGSTDCALL, like SWIG's own exports
  // (stdcall matters on x86 only)
  private const string Library = "NetOccApplicationFramework";

  [DllImport(Library, EntryPoint = "NetOcc_TDF_AcquireLabelData",
             CallingConvention = CallingConvention.StdCall)]
  public static extern IntPtr AcquireFromLabel(IntPtr label);

  [DllImport(Library, EntryPoint = "NetOcc_TDF_AcquireAttributeData",
             CallingConvention = CallingConvention.StdCall)]
  public static extern IntPtr AcquireFromAttribute(IntPtr attribute);

  [DllImport(Library, EntryPoint = "NetOcc_TDF_AcquireBuilderData",
             CallingConvention = CallingConvention.StdCall)]
  public static extern IntPtr AcquireFromBuilder(IntPtr builder);

  [DllImport(Library, EntryPoint = "NetOcc_TDF_ReleaseData",
             CallingConvention = CallingConvention.StdCall)]
  public static extern void Release(IntPtr data);
}
