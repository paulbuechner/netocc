// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;

namespace NetOcc.Generator.Emit;

/// <summary>
/// The order of a module's %import lines: what it uses, transitively, each package after the
/// packages its classes derive from.
/// </summary>
internal static class ImportOrder
{
  // transitive imports: every module this one or its imports use, in a stable order; never the
  // module itself, which a cycle between packages reaches too. A package comes after the packages
  // its classes derive from: SWIG drops a base it meets after the class (Warning 401), and uses may
  // form cycles, which the depth-first order breaks anywhere.
  public static List<string> Closure(string own, IEnumerable<string> direct,
                                     Func<string, IReadOnlyList<string>> importsOf,
                                     Func<string, IReadOnlyCollection<string>>? basesOf = null)
  {
    List<string> ordered = [];
    HashSet<string> seen = [own];

    void Visit(string package)
    {
      if (!seen.Add(package))
      {
        return;
      }

      foreach (var dependency in importsOf(package))
      {
        Visit(dependency);
      }

      ordered.Add(package);
    }

    foreach (var package in direct.Order(StringComparer.Ordinal))
    {
      Visit(package);
    }

    return basesOf is null ? ordered : BasesFirst(ordered, basesOf);
  }

  // a stable reorder: each package after the listed packages its classes derive from. Inheritance
  // between packages has no cycles; should one appear, the rest keeps its order (and build.py
  // generate reports the base SWIG drops)
  private static List<string> BasesFirst(List<string> packages,
                                         Func<string, IReadOnlyCollection<string>> basesOf)
  {
    List<string> sorted = [];
    HashSet<string> pending = [.. packages];
    var remaining = new List<string>(packages);
    while (remaining.Count > 0)
    {
      var next = remaining.FindIndex(p => basesOf(p).All(b => b == p || !pending.Contains(b)));
      var package = remaining[next < 0 ? 0 : next];
      remaining.Remove(package);
      pending.Remove(package);
      sorted.Add(package);
    }

    return sorted;
  }
}
