// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Runtime.InteropServices;

namespace OCC.Core;

/// <summary>
/// A complex number with C++'s <c>std::complex&lt;double&gt;</c> layout (the real part, then the
/// imaginary part), which OCCT's polynomial solvers take and return (Std.i). .NET Framework 3.5 has
/// no System.Numerics.Complex.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Complex(double real, double imaginary)
{
  private readonly double _real = real;
  private readonly double _imaginary = imaginary;

  /// <summary>The real part.</summary>
  public double Real => _real;

  /// <summary>The imaginary part.</summary>
  public double Imaginary => _imaginary;

  public override string ToString() =>
    string.Format(CultureInfo.InvariantCulture, "({0}, {1})", _real, _imaginary);
}
