// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;

namespace OCC.Core;

/// <summary>
/// What a native object refers to without owning it (References.i), which the proxy holds in
/// <c>netoccOwner</c>:
/// <list type="bullet">
/// <item>arguments its constructor keeps, and the object a by-value return points into (the object
/// a member was called on): held here too, until the GC has collected the proxy. Finalizers run in
/// any order, and the destructor may still use them (a <c>BRepGraph_MutGuard</c>'s clears its item
/// in the graph).</item>
/// <item>arguments its members keep, one per member and position: for the proxy's life only, since
/// such an argument may be the object's own (held here, it would keep the object forever).</item>
/// </list>
/// </summary>
internal static class NativeKeep
{
  // SWIG converts a constructor's arguments in a static helper, before the proxy exists: the kept
  // ones wait here
  [ThreadStatic] private static List<object> _pending;

  private static readonly List<Held> Table = [];
  private static int _sweepAt = 64;

  // a finalizer after each full collection sweeps the table, also when no new object keeps anything
  static NativeKeep() => new Sweeper();

  /// <summary>
  /// Before a constructor's native call: drops what an earlier one left pending, whose proxy never
  /// took it (a managed exception in its call, which skips the constructor body).
  /// </summary>
  public static void Begin() => _pending = null;

  /// <summary>
  /// An argument the constructor being called keeps (after the native call, which may have failed).
  /// </summary>
  public static void Push(object argument)
  {
    if (argument is not null)
    {
      (_pending ??= []).Add(argument);
    }
  }

  /// <summary>
  /// What <paramref name="keeper"/>, a new proxy, keeps: the arguments pushed for its constructor,
  /// or null.
  /// </summary>
  public static object Take(object keeper)
  {
    var pending = _pending;
    _pending = null;
    return pending is null
      ? null
      : Hold(keeper, null, pending.Count == 1 ? pending[0] : pending.ToArray());
  }

  /// <summary>
  /// Holds <paramref name="kept"/> for <paramref name="keeper"/> until the GC has collected it,
  /// with its netoccOwner <paramref name="owner"/>: the new netoccOwner.
  /// </summary>
  public static object Hold(object keeper, object owner, object kept)
  {
    var held = new Held(keeper, owner, kept);
    lock (Table)
    {
      if (Table.Count >= _sweepAt)
      {
        Sweep();
      }

      Table.Add(held);
    }

    return held;
  }

  /// <summary>
  /// Keeps a member's <paramref name="argument"/> in place of what <paramref name="slot"/> (member
  /// and position) kept before, with the proxy's netoccOwner <paramref name="owner"/>: the new
  /// netoccOwner.
  /// </summary>
  public static object Keep(object owner, string slot, object argument)
  {
    var slots = owner as Slots ?? new Slots(owner);
    slots.Set(slot, argument);
    return slots;
  }

  // drops what collected proxies held; the caller has the table's lock
  private static void Sweep()
  {
    Table.RemoveAll(held => !held.Keeper.IsAlive);
    _sweepAt = Math.Max(64, 2 * Table.Count);
  }

  private sealed class Held(object keeper, object owner, object kept)
  {
    // tracks resurrection: alive until the GC has collected the proxy, after its finalizer
    public readonly WeakReference Keeper = new(keeper, true);
    public readonly object Owner = owner;
    public readonly object Kept = kept;
  }

  private sealed class Slots(object owner)
  {
    public readonly object Owner = owner;
    private readonly Dictionary<string, object> _arguments = [];

    public void Set(string slot, object argument)
    {
      lock (_arguments)
      {
        _arguments[slot] = argument;
      }
    }
  }

  // never referenced: collected with each full collection, its finalizer sweeps and registers it
  // again
  private sealed class Sweeper
  {
    ~Sweeper()
    {
      if (Environment.HasShutdownStarted || AppDomain.CurrentDomain.IsFinalizingForUnload())
      {
        return;
      }

      if (Monitor.TryEnter(Table))
      {
        try
        {
          Sweep();
        }
        finally
        {
          Monitor.Exit(Table);
        }
      }

      global::System.GC.ReRegisterForFinalize(this);
    }
  }
}
