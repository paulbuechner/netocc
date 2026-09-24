// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System.IO;

// NUnit
using NUnit.Framework;

//
using OCC.Core.BRep;
using OCC.Core.BRepGProp;
using OCC.Core.BRepMesh;
using OCC.Core.BRepPrimAPI;
using OCC.Core.BRepTools;
using OCC.Core.GProp;
using OCC.Core.gp;
using OCC.Core.IFSelect;
using OCC.Core.IGESControl;
using OCC.Core.Message;
using OCC.Core.RWGltf;
using OCC.Core.RWMesh;
using OCC.Core.RWStl;
using OCC.Core.STEPControl;
using OCC.Core.StlAPI;
using OCC.Core.TColStd;
using OCC.Core.TDataStd;
using OCC.Core.TDF;
using OCC.Core.TDocStd;
using OCC.Core.TopAbs;
using OCC.Core.TopExp;
using OCC.Core.TopLoc;
using OCC.Core.TopoDS;
using OCC.Core.XCAFApp;
using OCC.Core.XCAFDoc;

namespace NetOcc.Samples;

/// <summary>Data exchange: STEP, IGES, BRep and STL files, and streams.</summary>
[TestFixture]
public class DataExchange : Files
{
  private static double Volume(TopoDS_Shape shape)
  {
    var properties = new GProp_GProps();
    BRepGProp.VolumeProperties(shape, properties);
    return properties.Mass();
  }

  private static readonly TopoDS_Shape Part =
    new BRepPrimAPI_MakeCylinder(new gp_Ax2(new gp_Pnt(0, 0, 0), new gp_Dir(0, 0, 1)), 10, 30)
      .Shape();

  [Test]
  public void Step()
  {
    // Arrange
    var shape = Part;

    // Act
    #region step
    var writer = new STEPControl_Writer();
    if (writer.Transfer(shape, STEPControl_StepModelType.STEPControl_AsIs)
        != IFSelect_ReturnStatus.IFSelect_RetDone
        || writer.Write("Ünïcödé.step") != IFSelect_ReturnStatus.IFSelect_RetDone)
    {
      throw new IOException("writing the STEP file failed");
    }

    var reader = new STEPControl_Reader();
    if (reader.ReadFile("Ünïcödé.step") != IFSelect_ReturnStatus.IFSelect_RetDone)
    {
      throw new IOException("reading the STEP file failed");
    }

    reader.TransferRoots();       // translates every root entity
    var read = reader.OneShape(); // all of them: one shape, or a compound
    #endregion

    // Assert
    Assert.That(Volume(read), Is.EqualTo(Volume(shape)).Within(1e-6));
  }

  [Test]
  public void Iges()
  {
    // Arrange
    var shape = Part;

    // Act
    #region iges
    var writer =
      new IGESControl_Writer("MM", 1); // 1: faces as B-rep solids, 0: as trimmed surfaces
    writer.AddShape(shape);
    writer.ComputeModel();
    writer.Write("part.igs");

    var reader = new IGESControl_Reader();
    reader.ReadFile("part.igs");
    reader.TransferRoots();
    var read = reader.OneShape();
    #endregion

    // Assert
    Assert.That(Volume(read), Is.EqualTo(Volume(shape)).Within(1e-3));
  }

  [Test]
  public void BRepFiles()
  {
    // Arrange
    var shape = Part;

    // Act
    #region brep
    // OCCT's own format keeps the shape exactly as it is
    BRepTools.Write(shape, "part.brep");

    var read = new TopoDS_Shape(); // Read fills the shape it's given
    BRepTools.Read(read, "part.brep", new BRep_Builder());
    #endregion

    // Assert
    Assert.That(Volume(read), Is.EqualTo(Volume(shape)).Within(1e-9));
  }

  [Test]
  public void Streams()
  {
    // Arrange
    var shape = Part;

    // Act
    #region streams
    // any System.IO.Stream stands in for std::ostream and std::istream
    using var buffer = new MemoryStream();
    BRepTools.Write(shape, buffer);

    buffer.Position = 0;
    var read = new TopoDS_Shape();
    BRepTools.Read(read, buffer, new BRep_Builder());
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(buffer.Length, Is.GreaterThan(0));
      Assert.That(Volume(read), Is.EqualTo(Volume(shape)).Within(1e-9));
    }
  }

  [Test]
  public void Stl()
  {
    // Arrange
    var shape = Part;

    // Act
    #region stl
    // STL holds triangles: mesh first
    new BRepMesh_IncrementalMesh(shape, 0.05);

    var writer = new StlAPI_Writer();
    writer.ASCIIMode() = false; // a ref return: binary STL
    writer.Write(shape, "part.stl");

    // read back as one triangulation, not as a face per triangle
    var mesh = RWStl.ReadFile("part.stl");
    var triangles = mesh.NbTriangles();
    #endregion

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(new FileInfo("part.stl").Length, Is.EqualTo(84 + 50L * triangles));
      Assert.That(triangles, Is.GreaterThan(100));
    }
  }

  [Test]
  public void Gltf()
  {
    // Arrange
    var shape = Part;

    // Act
    #region gltf
    // the glTF and OBJ writers take an XCAF document, for names, colors and assemblies
    var application = XCAFApp_Application.GetApplication();
    TDocStd_Document? document = null;
    application.NewDocument("MDTV-XCAF", ref document);
    var label = XCAFDoc_DocumentTool.ShapeTool(document!.Main()).AddShape(shape, false);
    TDataStd_Name.Set(label, "cylinder");

    // they write the triangulation: mesh first
    new BRepMesh_IncrementalMesh(shape, 0.05);

    var writer = new RWGltf_CafWriter("part.glb", true); // true: binary glTF
    var axes = writer.ChangeCoordinateSystemConverter();
    axes.SetInputLengthUnit(0.001); // millimetres in, glTF's metres out
    axes.SetInputCoordinateSystem(RWMesh_CoordinateSystem
                                    .RWMesh_CoordinateSystem_Zup); // Z up in, glTF's Y up out
    var written = writer.Perform(document, new TColStd_IndexedDataMapOfStringString(),
                                 new Message_ProgressRange());

    application.Close(document);
    #endregion

    // Assert: read back, a triangulated face per face of the cylinder
    TDocStd_Document? read = null;
    application.NewDocument("MDTV-XCAF", ref read);
    var reader = new RWGltf_CafReader();
    reader.SetDocument(read!);
    var readBack = reader.Perform("part.glb", new Message_ProgressRange());
    var roots = new TDF_LabelSequence();
    XCAFDoc_DocumentTool.ShapeTool(read!.Main()).GetFreeShapes(roots);
    var triangles = 0;
    foreach (var face in new TopExp_Explorer(XCAFDoc_ShapeTool.GetShape(roots[1]),
                                             TopAbs_ShapeEnum.TopAbs_FACE))
    {
      triangles += BRep_Tool.Triangulation(TopoDS.Face(face), new TopLoc_Location())?.NbTriangles()
                   ?? 0;
    }

    application.Close(read);
    using (Assert.EnterMultipleScope())
    {
      Assert.That(written && readBack, Is.True);
      Assert.That(File.ReadAllBytes("part.glb")[..4], Is.EqualTo("glTF"u8.ToArray()));
      Assert.That(triangles, Is.GreaterThan(100));
    }
  }
}
