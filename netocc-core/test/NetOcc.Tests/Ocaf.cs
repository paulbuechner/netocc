// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

//
using OCC.Core.Standard;
using OCC.Core.TDataStd;
using OCC.Core.TDF;

namespace NetOcc.Tests;

/// <summary>
/// OCAF lookups the tests share: attributes by GUID plus DownCast, entries, names, label sequences.
/// </summary>
internal static class Ocaf
{
  public static T? Find<T>(TDF_Label label, Guid id, Func<Standard_Transient, T> downCast)
    where T : TDF_Attribute
  {
    TDF_Attribute? attribute = null;
    return label.FindAttribute(id, ref attribute) ? downCast(attribute!) : null;
  }

  public static string? Name(TDF_Label label) =>
    Find(label, TDataStd_Name.GetID(), TDataStd_Name.DownCast)?.Get();

  public static string Entry(TDF_Label label)
  {
    var entry = "";
    TDF_Tool.Entry(label, ref entry);
    return entry;
  }

  public static List<TDF_Label> Labels(Action<TDF_LabelSequence> fill)
  {
    using var sequence = new TDF_LabelSequence();
    fill(sequence);
    return [.. sequence];
  }
}
