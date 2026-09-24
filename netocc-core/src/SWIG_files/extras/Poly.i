// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/* Poly: companion of the generated Poly.i, included before its classes. Bulk copies avoid a call per node. */

%csmethodmodifiers Poly_Triangulation::CopyNodesTo "internal";
%csmethodmodifiers Poly_Triangulation::CopyTrianglesTo "internal";

%extend Poly_Triangulation {
  void CopyNodesTo(gp_Pnt* theArray) const {
    const int n = $self->NbNodes();
    for (int i = 1; i <= n; ++i) theArray[i - 1] = $self->Node(i);
  }
  void CopyTrianglesTo(Poly_Triangle* theArray) const {
    const int n = $self->NbTriangles();
    for (int i = 1; i <= n; ++i) theArray[i - 1] = $self->Triangle(i);
  }
  %proxycode %{
  /// <summary>All nodes (index i = OCCT node i+1), copied in one native call.</summary>
  public gp_Pnt[] NodesToArray() {
    var nodes = new gp_Pnt[NbNodes()];
    CopyNodesTo(nodes);
    return nodes;
  }

  /// <summary>All triangles (index i = OCCT triangle i+1), copied in one native call.</summary>
  public Poly_Triangle[] TrianglesToArray() {
    var triangles = new Poly_Triangle[NbTriangles()];
    CopyTrianglesTo(triangles);
    return triangles;
  }
  %}
}
