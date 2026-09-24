// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/* Standard: companion of the generated Standard.i, included before its classes. */

%extend Standard_Transient {
  %proxycode %{
  // Two proxies can wrap one native object: compare the native pointers.
  public override bool Equals(object obj) {
    var other = obj as Standard_Transient;
    return !ReferenceEquals(other, null) && getCPtr(other).Handle == getCPtr(this).Handle;
  }

  public override int GetHashCode() {
    return getCPtr(this).Handle.GetHashCode();
  }
  %}
}
