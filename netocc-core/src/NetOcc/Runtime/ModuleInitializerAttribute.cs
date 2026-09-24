// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill: module initializers are a CLR feature; the C# compiler only needs the attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class ModuleInitializerAttribute : Attribute;
#endif
