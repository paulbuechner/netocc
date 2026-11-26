// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
#if NETFRAMEWORK
using System.Runtime.Serialization;
#endif

//
using OCC.Core.Standard;

namespace OCC.Core;

/// <summary>
/// A C++ exception raised inside OCCT (<c>Standard_Failure</c> and subclasses, or
/// <c>std::exception</c>), caught at the native boundary and rethrown in .NET.
/// </summary>
#if NETFRAMEWORK
// crosses app domains (plugin hosts, test runners) as a copy
[Serializable]
#endif
public class OcctException : Exception
{
  /// <param name="occtType">OCCT exception class, e.g. <c>Standard_ConstructionError</c>.</param>
  /// <param name="occtMessage">Message as reported by OCCT.</param>
  public OcctException(string occtType, string occtMessage) : base(
    string.IsNullOrEmpty(occtMessage) ? occtType : $"{occtType}: {occtMessage}")
  {
    OcctType = occtType;
    OcctMessage = occtMessage;
  }

  /// <param name="occtType">OCCT exception class, e.g. <c>Standard_ConstructionError</c>.</param>
  /// <param name="occtMessage">Message as reported by OCCT.</param>
  /// <param name="innerException">
  /// What a C# override threw, which OCCT unwound as a <c>NetOcc_ManagedException</c>.
  /// </param>
  public OcctException(string occtType, string occtMessage, Exception innerException) : base(
    string.IsNullOrEmpty(occtMessage) ? occtType : $"{occtType}: {occtMessage}", innerException)
  {
    OcctType = occtType;
    OcctMessage = occtMessage;
  }

  // what a wrapper caught (Exceptions.i): an exception of a C# override comes back as the inner one
  internal static OcctException FromNative(string occtType, string occtMessage) =>
    occtType == Directors.ManagedException && Directors.Take(occtMessage) is { } thrown
      ? new OcctException(occtType, thrown.Message, thrown)
      : new OcctException(occtType, occtMessage);

#if NETFRAMEWORK
  protected OcctException(SerializationInfo info, StreamingContext context) : base(info, context)
  {
    OcctType = info.GetString(nameof(OcctType));
    OcctMessage = info.GetString(nameof(OcctMessage));
  }
#endif

  /// <summary>OCCT exception class, e.g. <c>Standard_ConstructionError</c>.</summary>
  public string OcctType { get; }

  /// <summary>Message as reported by OCCT.</summary>
  public string OcctMessage { get; }

  /// <summary>
  /// Whether OCCT raised <typeparamref name="T"/> or a subclass of it:
  /// <c>Is&lt;Standard_RangeError&gt;()</c> holds for a <c>Standard_OutOfRange</c>. The proxies of
  /// OCCT's exception classes carry its class hierarchy.
  /// </summary>
  public bool Is<T>() where T : Standard_Failure =>
    Raised is { } raised && typeof(T).IsAssignableFrom(raised);

#if NETFRAMEWORK
  public override void GetObjectData(SerializationInfo info, StreamingContext context)
  {
    base.GetObjectData(info, context);
    info.AddValue(nameof(OcctType), OcctType);
    info.AddValue(nameof(OcctMessage), OcctMessage);
  }
#endif

  // the proxy of the raised class: OCC.Core.<Package>.<Class>, the package its name's prefix; none
  // for std::exception or a class outside the wrapped packages
  private Type Raised =>
    OcctType?.IndexOf('_') is int end and > 0
      ? typeof(OcctException).Assembly.GetType($"OCC.Core.{OcctType.Substring(0, end)}.{OcctType}")
      : null;
}
