// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/* TopoDS: companion of the generated TopoDS.i, included before its classes. */

%csmethodmodifiers TopoDS_Shape::NetOcc_HashCode "internal";

%extend TopoDS_Shape {
  int NetOcc_HashCode() const { return (int)std::hash<TopoDS_Shape>{}(*$self); }
  %proxycode %{
  // C++ operator== is IsEqual (same TShape, location and orientation).
  public override bool Equals(object obj) {
    var other = obj as TopoDS_Shape;
    return !ReferenceEquals(other, null) && IsEqual(other);
  }

  public override int GetHashCode() {
    return NetOcc_HashCode();
  }
  %}
}
