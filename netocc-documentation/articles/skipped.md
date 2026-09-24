# Skipped members

What NetOcc leaves out of OCCT, 1066 entries, each for a reason: members OCCT declares but never defines or exports, APIs C# can't express, and the parts of Visualization NetOcc doesn't wrap (the renderer's internals, one platform's input). netocc-gen logs every skip with its reason; members C# reaches another way (overload twins, hand-written members, .NET types) aren't skips. Written by `skips.py` from the generator's skip logs.

Many of the reasons are traps described in [Pitfalls](pitfalls.md).

## Accepted

### Raw pointers

| Member | Reason |
|---|---|
| `gp_XYZ::GetData`, `gp_XYZ::ChangeData` | A pointer into the struct. A C# struct is pinned for the call only, and the GC may move it after; its fields hold the data. |
| `BinObjMgt_Persistent::GetOStream`, `GetIStream` | The persistent's stream exists only while a storage driver pastes. `SetOStream` keeps a stream, which C# lends for a call only, and without one OCCT dereferences a null stream. |
| `Image_AlienPixMap::GetPalette` (excluded in the config) | Declared only on Windows without FreeImage: a WIC palette (`IWICPalette*`). Generated on Windows, it breaks the other platforms' builds. |
| `Standard_CLocaleSentry::GetCLocale` | The C runtime's locale handle: `_locale_t` on Windows, `locale_t` elsewhere. Generated on Windows, the type doesn't exist on Linux. |
| `Standard_Type::Register` | Takes a `std::type_info&`: C++ RTTI, which C# can't produce. |

### Class template instances

| Member | Reason |
|---|---|
| `RWGltf_GltfOStreamWriter::RWGltf_GltfOStreamWriter` | Takes rapidjson's `BasicOStreamWrapper<std::ostream>`, a C++ template of a third-party library that C# can't create. |
| `IntPolyh_ArrayOfPointNormal::Dump`, `IntPolyh_ArrayOfEdges::Dump`, `IntPolyh_ArrayOfTriangles::Dump` | `IntPolyh_Array<T>::Dump` calls `T::Dump()`, which these `T` lack or take arguments for: the member doesn't compile in C++ either. |
| `BRepGraph_LayerTopoSupplement::AttachedTo`; `Image_PixMap`'s 3D sizes (`SizeXYZ`, `InitWrapper3D`, `InitTrash3D`, `InitZero3D`, `Image_PixMapData::Init`); `Aspect_WindowInputListener::TouchPoints` | A class template instance of `size_t` (`NCollection_LinearVector<size_t>`, `NCollection_Vec3<size_t>`, a map keyed by `size_t`). NetOcc names instances by their canonical types, which spell `size_t` per platform (`unsigned long long` on Windows, `unsigned long` elsewhere); keeping the sugar would take a parser change. |
| `NCollection_UtfString<wchar_t>` and the `ToUtfWide` members returning it | `wchar_t` is 16 bits on Windows and 32 elsewhere: no C# type has both widths. `NCollection_UtfString<char16_t>` and `<char32_t>` hold UTF-16 and UTF-32. |
| `Graphic3d_Layer::Structures`, `NonCullableStructures`, `ArrayOfStructures`; `Graphic3d_BvhCStructureSet::Structures`, `Graphic3d_BvhCStructureSetTrsfPers::Structures`; `Graphic3d_Structure::Network`; `Graphic3d_StructureManager::DefinedViews`, `RecomputeStructures` | Collections of raw pointers to the structure manager's structures and views: no C# collection holds raw pointers. C# reaches structures through their presentations (`PrsMgr`, `AIS`). |
| `BRepMesh_Classifier::RegisterWire` | Takes `NCollection_Sequence<const gp_Pnt2d*>`: raw pointers to the caller's points, which no C# collection holds (a C# struct has no fixed address). |
| `TNaming_UsedShapes::Map` | The attribute's map to the raw `TNaming_RefShape` pointers it owns: no C# collection holds raw pointers. |
| `MoniTool_Timer::Dictionary` | A map keyed by C strings (`const char*`) whose storage OCCT owns: no C# collection holds them. |
| `BVH::RadixSorter` | Its flat name, `BVH_RadixSorter`, is the file-scope template's too. |

### Standard library types

| Member | Reason |
|---|---|
| `OSD_FileSystem`, `OSD_CachedFileSystem`, `OSD_FileSystemSelector`, `OSD_LocalFileSystem`: `OpenIStream`, `OpenOStream`, `OpenStreamBuffer`, `IsOpenIStream`, `IsOpenOStream`; the `OSD_IStreamBuffer`, `OSD_OStreamBuffer`, `OSD_IOStreamBuffer` constructors; `RWGltf_GltfJsonParser::SetStream`; `RWPly_PlyWriterContext::Open`'s stream (a call opens the file) | A `std::shared_ptr` to a native stream or stream buffer: OCCT shares it and keeps it past the call, while NetOcc lends a C# stream for one call (Streams.i) and gives C# no view of OCCT's streams. |
| `BinTools_IStream::Stream`, `Message_PrinterOStream::GetStream` | A mutable reference to a native stream: C# has no view of OCCT's streams (Streams.i). |
| `Message_Messenger_StreamBuffer::Stream` | The `std::stringstream` C++'s `<<` writes into, sent when the buffer goes; C# sends text with `Message_Messenger.Send`. |
| `BRepGraphInc_Storage::CurrentShapesMutex`, `DE_Wrapper::GlobalLoadMutex`, `Aspect_VKeySet::Mutex`, `SelectMgr_BVHThreadPool_BVHThread::BVHMutex`, `StdPrs_BRepFont::Mutex` | A `std::mutex`/`std::shared_mutex` for C++ code to lock around its own work: C# can't hold a C++ lock, which lives within one call. |
| `Select3D_SensitivePrimitiveArray::GetVertex` | Returns `std::array<NCollection_Vec3<float>, 3>`: Std.i's arrays hold numbers, not structs. |
| `MathUtils_Polynomial(std::initializer_list<double>)`, `MathUtils_Rational(std::initializer_list<double>, ...)` | Only the compiler builds a `std::initializer_list`, from a braced list; the `math_Vector` constructors take the same coefficients. |
| `Standard_ErrorHandler::Error`, `Standard_ErrorHandler::Label` | `Standard_ErrorHandler` is the C++ side of OCCT's signal handling (`OCC_CATCH_SIGNALS`): a `std::variant` of signal exceptions and a setjmp buffer. C# gets OCCT's errors as `OcctException`. |

### Wrapped by hand

| Member | Reason |
|---|---|
| `ShapeProcess::ToOperationFlag` | Returns `std::pair<ShapeProcess::Operation, bool>`. A pair's accessors are `%extend`, which SWIG casts with `enum ShapeProcess_Operation`, and a nested enum's flat alias can't follow that. The companion `extras/ShapeProcess.i` wraps it: `ShapeProcess.ToOperationFlag(name, ref known)`. |

### References and overloads

| Member | Reason |
|---|---|
| `Font_Rect::TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` (the overloads taking a vector) | They fill the vector they're given and return it by reference, into a C# struct; C# gets the overloads without it, which return the value. |
| `NCollection_Mat3<T>::Map`, `NCollection_Mat4<T>::Map` (`BVH_Mat4d`, `BVH_Mat4f`) | A static function viewing a raw array as a matrix, by reference: memory C# doesn't own. The matrices' members and constructors take the values. |
| `NCollection_Mat3<T>::Multiply(m)`, `NCollection_Mat4<T>::Multiply(m)` (`BVH_Mat4d`, `BVH_Mat4f`) | Next to the static `Multiply(m1, m2)`: SWIG's wrappers take the object as the first argument, so the two collide, and SWIG keeps the static one; `a = Multiply(a, m)` does the same. |
| `Font_FontMgr::ToUseUnicodeSubsetFallback` | A static function returning a reference to a process-wide flag: C# refs come from objects (References.i), and the flag has no setter. |
| `gp_XY::ChangeCoord`, `gp_XYZ::ChangeCoord`, `gp_Pnt::ChangeCoord`, `gp_Pnt2d::ChangeCoord`, `gp_Mat::ChangeValue`, `gp_Mat2d::ChangeValue`, `Poly_Triangle::ChangeValue`, `Quantity_ColorRGBA::ChangeRGB` | A reference into a C# struct, which the GC may move: C# can't return a ref to a struct's own field on .NET Framework; `SetCoord`, `SetValue` and `SetRGB` write the same. |
| `BOPAlgo_ParallelAlgo::Perform` | C++ can't call it either: the private `Perform(const Message_ProgressRange& = {})` makes `Perform()` ambiguous. Subclasses implement it. |
| `Standard_Transient::IncrementRefCounter`, `DecrementRefCounter`, `Delete` | Excluded in the config: the proxy owns one reference (SWIG's `ref`/`unref`), and calling these from C# would free the object under a live proxy. |

## Outside the wrapped modules

### Renderer internals, one platform's input, FFmpeg (17)

Visualization is wrapped without OpenGl's renderer classes (contexts, windows, raytracing: they declare per platform; the driver is wrapped), WNT (Windows' window and input package: a native window comes from `Aspect_Window.FromNativeHandle`), and FFmpeg's types (`AVStream`, `AVRational`: NetOcc's OCCT build has no FFmpeg). These take or return one (in parentheses).

- **AIS:** `AIS_ViewController::Update3dMouse (WNT_HIDSpaceMouse)`
- **Aspect:** `Aspect_WindowInputListener::Update3dMouse (WNT_HIDSpaceMouse)`, `Aspect_WindowInputListener::update3dMouseKeys (WNT_HIDSpaceMouse)`, `Aspect_WindowInputListener::update3dMouseRotation (WNT_HIDSpaceMouse)`, `Aspect_WindowInputListener::update3dMouseTranslation (WNT_HIDSpaceMouse)`
- **Media:** `Media_CodecContext::Init (AVStream)`, `Media_FormatContext::SecondsToUnits (AVRational)`, `Media_FormatContext::Stream (AVStream)`, `Media_FormatContext::StreamSecondsToUnits (AVStream)`, `Media_FormatContext::StreamUnitsToSeconds (AVStream)`, `Media_FormatContext::UnitsToSeconds (AVRational)`
- **OpenGl:** `OpenGl_GraphicDriver::CreateRenderWindow (OpenGl_Window)`, `OpenGl_GraphicDriver::GetSharedContext (OpenGl_Context)`, `OpenGl_GraphicDriver::GetStateCounter (OpenGl_StateCounter)`, `OpenGl_Raytrace::IsRaytracedElement (OpenGl_Element)`, `OpenGl_Raytrace::IsRaytracedElement (OpenGl_ElementNode)`, `OpenGl_Raytrace::IsRaytracedGroup (OpenGl_Group)`

## Headers OCCT ships broken

### Headers left out (13)

OCCT installs these without a header they include, or they don't compile, or they're Windows-only (in parentheses, why): the parse leaves them out, and their declarations with them.

- **BOPDS:** `BOPDS_DataMapOfIntegerListOfPaveBlock.hxx ('BOPDS_ListOfPaveBlock.hxx' file not found)`, `BOPDS_DataMapOfPaveBlockListOfPaveBlock.hxx ('BOPDS_ListOfPaveBlock.hxx' file not found)`, `BOPDS_IndexedDataMapOfPaveBlockListOfPaveBlock.hxx ('BOPDS_ListOfPaveBlock.hxx' file not found)`, `BOPDS_VectorOfListOfPaveBlock.hxx ('BOPDS_ListOfPaveBlock.hxx' file not found)`
- **GeomBndLib:** `GeomBndLib_Curve.hxx (includes GeomBndLib_Line.hxx, which doesn't compile: 'GeomBndLib_InfiniteHelpers.pxx' file not found)`, `GeomBndLib_Curve2d.hxx (includes GeomBndLib_Line2d.hxx, which doesn't compile: 'GeomBndLib_InfiniteHelpers.pxx' file not found)`, `GeomBndLib_Line.hxx ('GeomBndLib_InfiniteHelpers.pxx' file not found)`, `GeomBndLib_Line2d.hxx ('GeomBndLib_InfiniteHelpers.pxx' file not found)`
- **Graphic3d:** `Graphic3d_MapIteratorOfMapOfStructure.hxx ('Graphic3d_MapOfStructure.hxx' file not found)`
- **MathLin:** `MathLin_Jacobi.hxx (no member named 'NbIterations' in 'MathLin::EigenResult')`
- **OSD:** `OSD_WNT.hxx (Windows only (OCCT's WNT headers))`
- **OpenGl:** `OpenGl_GLESExtensions.hxx (expected ')')`
- **TObj:** `TObj_Container.hxx ('TObj_SequenceOfObject.hxx' file not found)`

## Objects C# can't create or keep

### Streams a class keeps (11)

A constructor, or a class with a stream member, may keep the stream past the call, while NetOcc lends a C# stream for one call (Streams.i).

- **BinObjMgt:** `BinObjMgt_Persistent::Read`, `BinObjMgt_Persistent::SetIStream`, `BinObjMgt_Persistent::SetOStream`, `BinObjMgt_Persistent::Write`, `BinObjMgt_Position::BinObjMgt_Position`
- **BinTools:** `BinTools_IStream::BinTools_IStream`, `BinTools_OStream::BinTools_OStream`
- **DE:** `DE_Provider_ReadStreamNode::DE_Provider_ReadStreamNode`, `DE_Provider_WriteStreamNode::DE_Provider_WriteStreamNode`
- **VrmlData:** `VrmlData_InBuffer::VrmlData_InBuffer`, `VrmlData_Scene::Dump`

### Allocated by OCCT only (9)

The class declares only placement forms of `operator new` (NCollection nodes, mesh data): OCCT allocates them from its allocators, and C# gets them from OCCT.

- **BRepMeshData:** `BRepMeshData_Curve::BRepMeshData_Curve`, `BRepMeshData_Edge::BRepMeshData_Edge`, `BRepMeshData_Face::BRepMeshData_Face`, `BRepMeshData_PCurve::BRepMeshData_PCurve`, `BRepMeshData_Wire::BRepMeshData_Wire`
- **Message:** `Message_LazyProgressScope::Message_LazyProgressScope`
- **NCollection:** `NCollection_ListNode::NCollection_ListNode`, `NCollection_SeqNode::NCollection_SeqNode`
- **Poly:** `Poly_CoherentTriPtr::Poly_CoherentTriPtr`

### Destructors C# can't call (7)

The destructor isn't public or isn't exported: a proxy that owned one couldn't release it (its finalizer would throw).

- **BRepGraphInc:** `BRepGraphInc_Reconstruct_Cache_TempScope::BRepGraphInc_Reconstruct_Cache_TempScope`
- **RWObj:** `RWObj_MtlReader::RWObj_MtlReader`
- **StdStorage:** `StdStorage_Bucket::StdStorage_Bucket`, `StdStorage_BucketOfPersistent::StdStorage_BucketOfPersistent`
- **StepFile:** `StepFile_ReadData::StepFile_ReadData`
- **Storage:** `Storage_Bucket::Storage_Bucket`, `Storage_BucketOfPersistent::Storage_BucketOfPersistent`

## Operators

### Operators of proxies (341)

C# proxies have no operators. OCCT's mostly repeat a named member (`operator*` is `Multiplied`, `operator()` is `Value`), which C# has; shapes compare with `Equals`.

- **AIS:** `AIS_WalkDelta::operator[]`
- **AdvApp2Var:** `AdvApp2Var_EvaluatorFunc2Var::operator()`, `AdvApp2Var_Network::operator()`
- **AdvApprox:** `AdvApprox_EvaluatorFunction::operator()`
- **BOPDS:** `BOPDS_Pair::operator<`, `BOPDS_Pair::operator==`, `BOPDS_Pave::operator<`, `BOPDS_Pave::operator==`
- **BOPTools:** `BOPTools_Set::operator==`
- **BRepGraph:** `BRepGraph_MutGuard_BRepGraphInc_ChildRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_ChildRef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_CoEdgeDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_CoEdgeDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_CompSolidDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_CompSolidDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_CompoundDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_CompoundDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_EdgeDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_EdgeDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_FaceDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_FaceDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_FaceRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_FaceRef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_OccurrenceDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_OccurrenceDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_OccurrenceRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_OccurrenceRef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_ProductDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_ProductDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_ShellDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_ShellDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_ShellRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_ShellRef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_SolidDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_SolidDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_SolidRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_SolidRef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_VertexDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_VertexDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_VertexRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_VertexRef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_WireDef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_WireDef::operator->`, `BRepGraph_MutGuard_BRepGraphInc_WireRef::operator*`, `BRepGraph_MutGuard_BRepGraphInc_WireRef::operator->`, `BRepGraph_UsagePath::operator==`
- **BRepMesh:** `BRepMesh_DataStructureOfDelaun::operator()`, `BRepMesh_Edge::operator==`, `BRepMesh_EdgeDiscret::operator()`, `BRepMesh_FaceChecker::operator()`, `BRepMesh_ModelHealer::operator()`, `BRepMesh_OrientedEdge::operator==`, `BRepMesh_Vertex::operator==`
- **BSplCLib:** `BSplCLib_EvaluatorFunction::operator()`
- **BSplSLib:** `BSplSLib_EvaluatorFunction::operator()`
- **BinObjMgt:** `BinObjMgt_Persistent::operator!`, `BinObjMgt_Persistent::operator<<`, `BinObjMgt_Persistent::operator>>`
- **BinTools:** `BinTools_IStream::operator>>`, `BinTools_OStream::operator<<`
- **BlendFunc:** `BlendFunc_Tensor::operator()`
- **Bnd:** `Bnd_Range::operator==`
- **CDM:** `CDM_Document::operator<<`, `CDM_MetaData::operator<<`
- **ChFiDS:** `ChFiDS_Map::operator()`, `ChFiDS_StripeMap::operator()`
- **ExtremaPC:** `ExtremaPC_Result::operator[]`
- **Font:** `Font_SystemFont::operator==`
- **Geom2d:** `Geom2d_Direction::operator^`, `Geom2d_Transformation::operator*`, `Geom2d_VectorWithMagnitude::operator*=`, `Geom2d_VectorWithMagnitude::operator+`, `Geom2d_VectorWithMagnitude::operator-`, `Geom2d_VectorWithMagnitude::operator/`, `Geom2d_VectorWithMagnitude::operator^`
- **Geom2dConvert:** `Geom2dConvert_PPoint::operator!=`, `Geom2dConvert_PPoint::operator==`
- **Geom2dHatch:** `Geom2dHatch_Elements::operator()`
- **GeomFill:** `GeomFill_Tensor::operator()`
- **Graphic3d:** `Graphic3d_BSDF::operator==`, `Graphic3d_CameraTile::operator==`, `Graphic3d_CubeMapOrder::operator[]`, `Graphic3d_FrameStatsData::operator[]`, `Graphic3d_FrameStatsDataTmp::operator[]`, `Graphic3d_Fresnel::operator==`, `Graphic3d_MaterialAspect::operator!=`, `Graphic3d_MaterialAspect::operator==`, `Graphic3d_PBRMaterial::operator==`, `Graphic3d_ValidatedCubeMapOrder::operator->`, `Graphic3d_WorldViewProjState::operator!=`, `Graphic3d_WorldViewProjState::operator==`
- **IGESData:** `IGESData_IGESType::operator==`
- **IntPolyh:** `IntPolyh_ArrayOfEdges::operator[]`, `IntPolyh_ArrayOfPointNormal::operator[]`, `IntPolyh_ArrayOfPoints::operator[]`, `IntPolyh_ArrayOfSectionLines::operator[]`, `IntPolyh_ArrayOfTangentZones::operator[]`, `IntPolyh_ArrayOfTriangles::operator[]`, `IntPolyh_Couple::operator==`, `IntPolyh_Point::operator*`, `IntPolyh_Point::operator+`, `IntPolyh_Point::operator-`, `IntPolyh_Point::operator/`, `IntPolyh_SectionLine::operator[]`
- **IntTools:** `IntTools_CurveRangeSample::operator==`, `IntTools_SurfaceRangeSample::operator==`
- **Interface:** `Interface_ParamList::operator()`
- **Intf:** `Intf_SectionLine::operator==`, `Intf_SectionPoint::operator==`, `Intf_TangentZone::operator==`
- **LDOM:** `LDOMBasicString::operator!=`, `LDOMBasicString::operator==`, `LDOM_Document::operator!=`, `LDOM_Document::operator==`, `LDOM_Node::operator!=`, `LDOM_Node::operator==`, `LDOM_NodeList::operator!=`, `LDOM_NodeList::operator==`
- **MAT:** `MAT_ListOfBisector::operator()`, `MAT_ListOfEdge::operator()`
- **MAT2d:** `MAT2d_BiInt::operator==`
- **MathRoot:** `MathRoot_MultipleGetValueFn::operator()`, `MathRoot_MultipleNoExtraHandler::operator()`, `MathRoot_MultipleResult::operator[]`
- **MathUtils:** `MathUtils_PolyResult::operator[]`
- **MeshVS:** `MeshVS_SymmetricPairHasher::operator()`, `MeshVS_TwoColors::operator==`
- **Message:** `Message_ExecStatus::operator&=`, `Message_ExecStatus::operator|=`, `Message_Messenger_StreamBuffer::operator<<`, `Message_Msg::operator<<`
- **NCollection:** `BVH_Mat4d::operator!=`, `BVH_Mat4d::operator()`, `BVH_Mat4d::operator*`, `BVH_Mat4d::operator+`, `BVH_Mat4d::operator-`, `BVH_Mat4d::operator/`, `BVH_Mat4d::operator==`, `BVH_Mat4f::operator!=`, `BVH_Mat4f::operator()`, `BVH_Mat4f::operator*`, `BVH_Mat4f::operator+`, `BVH_Mat4f::operator-`, `BVH_Mat4f::operator/`, `BVH_Mat4f::operator==`, `BVH_Vec2d::operator!=`, `BVH_Vec2d::operator*`, `BVH_Vec2d::operator+=`, `BVH_Vec2d::operator-`, `BVH_Vec2d::operator-=`, `BVH_Vec2d::operator/`, `BVH_Vec2d::operator==`, `BVH_Vec2f::operator!=`, `BVH_Vec2f::operator*`, `BVH_Vec2f::operator+=`, `BVH_Vec2f::operator-`, `BVH_Vec2f::operator-=`, `BVH_Vec2f::operator/`, `BVH_Vec2f::operator==`, `BVH_Vec2i::operator!=`, `BVH_Vec2i::operator*`, `BVH_Vec2i::operator+=`, `BVH_Vec2i::operator-`, `BVH_Vec2i::operator-=`, `BVH_Vec2i::operator/`, `BVH_Vec2i::operator==`, `BVH_Vec3d::operator!=`, `BVH_Vec3d::operator*`, `BVH_Vec3d::operator+=`, `BVH_Vec3d::operator-`, `BVH_Vec3d::operator-=`, `BVH_Vec3d::operator/`, `BVH_Vec3d::operator==`, `BVH_Vec3f::operator!=`, `BVH_Vec3f::operator*`, `BVH_Vec3f::operator+=`, `BVH_Vec3f::operator-`, `BVH_Vec3f::operator-=`, `BVH_Vec3f::operator/`, `BVH_Vec3f::operator==`, `BVH_Vec3i::operator!=`, `BVH_Vec3i::operator*`, `BVH_Vec3i::operator+=`, `BVH_Vec3i::operator-`, `BVH_Vec3i::operator-=`, `BVH_Vec3i::operator/`, `BVH_Vec3i::operator==`, `BVH_Vec4d::operator!=`, `BVH_Vec4d::operator*`, `BVH_Vec4d::operator+=`, `BVH_Vec4d::operator-`, `BVH_Vec4d::operator-=`, `BVH_Vec4d::operator/`, `BVH_Vec4d::operator==`, `BVH_Vec4f::operator!=`, `BVH_Vec4f::operator*`, `BVH_Vec4f::operator+=`, `BVH_Vec4f::operator-`, `BVH_Vec4f::operator-=`, `BVH_Vec4f::operator/`, `BVH_Vec4f::operator==`, `BVH_Vec4i::operator!=`, `BVH_Vec4i::operator*`, `BVH_Vec4i::operator+=`, `BVH_Vec4i::operator-`, `BVH_Vec4i::operator-=`, `BVH_Vec4i::operator/`, `BVH_Vec4i::operator==`, `Graphic3d_Vec2ub::operator!=`, `Graphic3d_Vec2ub::operator*`, `Graphic3d_Vec2ub::operator+=`, `Graphic3d_Vec2ub::operator-`, `Graphic3d_Vec2ub::operator-=`, `Graphic3d_Vec2ub::operator/`, `Graphic3d_Vec2ub::operator==`, `Graphic3d_Vec3ub::operator!=`, `Graphic3d_Vec3ub::operator*`, `Graphic3d_Vec3ub::operator+=`, `Graphic3d_Vec3ub::operator-`, `Graphic3d_Vec3ub::operator-=`, `Graphic3d_Vec3ub::operator/`, `Graphic3d_Vec3ub::operator==`, `Graphic3d_Vec4ub::operator!=`, `Graphic3d_Vec4ub::operator*`, `Graphic3d_Vec4ub::operator+=`, `Graphic3d_Vec4ub::operator-`, `Graphic3d_Vec4ub::operator-=`, `Graphic3d_Vec4ub::operator/`, `Graphic3d_Vec4ub::operator==`, `NCollection_BaseList_Iterator::operator==`, `NCollection_Mat3_double::operator!=`, `NCollection_Mat3_double::operator()`, `NCollection_Mat3_double::operator*`, `NCollection_Mat3_double::operator+`, `NCollection_Mat3_double::operator-`, `NCollection_Mat3_double::operator/`, `NCollection_Mat3_double::operator==`, `NCollection_Mat3_float::operator!=`, `NCollection_Mat3_float::operator()`, `NCollection_Mat3_float::operator*`, `NCollection_Mat3_float::operator+`, `NCollection_Mat3_float::operator-`, `NCollection_Mat3_float::operator/`, `NCollection_Mat3_float::operator==`, `NCollection_SparseArray_int::operator()`, `NCollection_String::operator!=`, `NCollection_String::operator+=`, `NCollection_String::operator==`, `NCollection_String::operator[]`, `NCollection_UtfIterator_char16_t::operator*`, `NCollection_UtfIterator_char16_t::operator++`, `NCollection_UtfIterator_char16_t::operator==`, `NCollection_UtfIterator_char32_t::operator*`, `NCollection_UtfIterator_char32_t::operator++`, `NCollection_UtfIterator_char32_t::operator==`, `NCollection_UtfIterator_char::operator*`, `NCollection_UtfIterator_char::operator++`, `NCollection_UtfIterator_char::operator==`, `NCollection_UtfString_char16_t::operator!=`, `NCollection_UtfString_char16_t::operator+=`, `NCollection_UtfString_char16_t::operator==`, `NCollection_UtfString_char16_t::operator[]`, `NCollection_UtfString_char32_t::operator!=`, `NCollection_UtfString_char32_t::operator+=`, `NCollection_UtfString_char32_t::operator==`, `NCollection_UtfString_char32_t::operator[]`, `NCollection_Vec2_bool::operator!=`, `NCollection_Vec2_bool::operator*`, `NCollection_Vec2_bool::operator+=`, `NCollection_Vec2_bool::operator-`, `NCollection_Vec2_bool::operator-=`, `NCollection_Vec2_bool::operator/`, `NCollection_Vec2_bool::operator==`, `NCollection_Vec3_bool::operator!=`, `NCollection_Vec3_bool::operator*`, `NCollection_Vec3_bool::operator+=`, `NCollection_Vec3_bool::operator-`, `NCollection_Vec3_bool::operator-=`, `NCollection_Vec3_bool::operator/`, `NCollection_Vec3_bool::operator==`
- **Poly:** `Poly_ArrayOfNodes::operator[]`, `Poly_ArrayOfUVNodes::operator[]`, `Poly_MakeLoops_Hasher::operator()`
- **Quantity:** `Quantity_Date::operator+`, `Quantity_Date::operator-`, `Quantity_Date::operator<`, `Quantity_Date::operator==`, `Quantity_Date::operator>`, `Quantity_Period::operator+`, `Quantity_Period::operator-`, `Quantity_Period::operator<`, `Quantity_Period::operator==`, `Quantity_Period::operator>`
- **Standard:** `Standard_CStringHasher::operator()`
- **StdObjMgt:** `StdObjMgt_ReadData::operator>>`, `StdObjMgt_WriteData::operator<<`
- **StepToTopoDS:** `StepToTopoDS_PointPair::operator==`
- **StepVisual:** `NCollection_Handle_NCollection_Array1_Handle_StepVisual_TessellatedItem::operator*`, `NCollection_Handle_NCollection_Array1_Handle_StepVisual_TessellatedItem::operator->`, `NCollection_Handle_NCollection_DynamicArray_Handle_NCollection_HSequence_int::operator*`, `NCollection_Handle_NCollection_DynamicArray_Handle_NCollection_HSequence_int::operator->`
- **Storage:** `Storage_BaseDriver::operator<<`, `Storage_BaseDriver::operator>>`
- **TDF:** `TDF_Attribute::operator<<`, `TDF_AttributeDelta::operator<<`, `TDF_Data::operator<<`, `TDF_DataSet::operator<<`, `TDF_Label::operator!=`, `TDF_Label::operator<<`, `TDF_Label::operator==`
- **TDataStd:** `TDataStd_BooleanArray::operator()`, `TDataStd_ByteArray::operator()`, `TDataStd_ExtStringArray::operator()`, `TDataStd_IntegerArray::operator()`, `TDataStd_RealArray::operator()`, `TDataStd_ReferenceArray::operator()`
- **TFunction:** `TFunction_DriverTable::operator<<`
- **TObj:** `NCollection_SparseArray_int_ConstIterator::operator()`
- **TopLoc:** `TopLoc_Location::operator!=`, `TopLoc_Location::operator*`, `TopLoc_Location::operator/`, `TopLoc_Location::operator==`
- **TopTools:** `TopTools_ShapeMapHasher::operator()`
- **TopoDS:** `TopoDS_Shape::operator!=`, `TopoDS_Shape::operator==`
- **Transfer:** `Transfer_FindHasher::operator()`
- **Units:** `Units_Measurement::operator*`, `Units_Measurement::operator+`, `Units_Measurement::operator-`, `Units_Measurement::operator/`
- **VrmlData:** `VrmlData_Scene::operator<<`
- **XCAFDoc:** `XCAFDoc_AssemblyItemId::operator==`
- **XCAFPrs:** `XCAFPrs_DocumentNode::operator==`, `XCAFPrs_Style::operator==`
- **math:** `PSO_Particle::operator<`, `math_DoubleTab::operator()`, `math_Matrix::operator()`, `math_Matrix::operator*`, `math_Matrix::operator+`, `math_Matrix::operator-`, `math_Matrix::operator/`

### Operators of structs (212)

C# structs get OCCT's const `+`, `-`, `*`, `/` and `^` (and unary `-`); comparisons, calls, increments and the rest stay out, and so do operators that change the struct.

- **BRepGraph:** `BRepGraph_ChildRefId::operator!=`, `BRepGraph_ChildRefId::operator++`, `BRepGraph_ChildRefId::operator<`, `BRepGraph_ChildRefId::operator<=`, `BRepGraph_ChildRefId::operator==`, `BRepGraph_ChildRefId::operator>`, `BRepGraph_ChildRefId::operator>=`, `BRepGraph_CoEdgeId::operator!=`, `BRepGraph_CoEdgeId::operator++`, `BRepGraph_CoEdgeId::operator<`, `BRepGraph_CoEdgeId::operator<=`, `BRepGraph_CoEdgeId::operator==`, `BRepGraph_CoEdgeId::operator>`, `BRepGraph_CoEdgeId::operator>=`, `BRepGraph_CompSolidId::operator!=`, `BRepGraph_CompSolidId::operator++`, `BRepGraph_CompSolidId::operator<`, `BRepGraph_CompSolidId::operator<=`, `BRepGraph_CompSolidId::operator==`, `BRepGraph_CompSolidId::operator>`, `BRepGraph_CompSolidId::operator>=`, `BRepGraph_CompoundId::operator!=`, `BRepGraph_CompoundId::operator++`, `BRepGraph_CompoundId::operator<`, `BRepGraph_CompoundId::operator<=`, `BRepGraph_CompoundId::operator==`, `BRepGraph_CompoundId::operator>`, `BRepGraph_CompoundId::operator>=`, `BRepGraph_EdgeId::operator!=`, `BRepGraph_EdgeId::operator++`, `BRepGraph_EdgeId::operator<`, `BRepGraph_EdgeId::operator<=`, `BRepGraph_EdgeId::operator==`, `BRepGraph_EdgeId::operator>`, `BRepGraph_EdgeId::operator>=`, `BRepGraph_FaceId::operator!=`, `BRepGraph_FaceId::operator++`, `BRepGraph_FaceId::operator<`, `BRepGraph_FaceId::operator<=`, `BRepGraph_FaceId::operator==`, `BRepGraph_FaceId::operator>`, `BRepGraph_FaceId::operator>=`, `BRepGraph_FaceRefId::operator!=`, `BRepGraph_FaceRefId::operator++`, `BRepGraph_FaceRefId::operator<`, `BRepGraph_FaceRefId::operator<=`, `BRepGraph_FaceRefId::operator==`, `BRepGraph_FaceRefId::operator>`, `BRepGraph_FaceRefId::operator>=`, `BRepGraph_NodeId::operator!=`, `BRepGraph_NodeId::operator++`, `BRepGraph_NodeId::operator<`, `BRepGraph_NodeId::operator==`, `BRepGraph_OccurrenceId::operator!=`, `BRepGraph_OccurrenceId::operator++`, `BRepGraph_OccurrenceId::operator<`, `BRepGraph_OccurrenceId::operator<=`, `BRepGraph_OccurrenceId::operator==`, `BRepGraph_OccurrenceId::operator>`, `BRepGraph_OccurrenceId::operator>=`, `BRepGraph_OccurrenceRefId::operator!=`, `BRepGraph_OccurrenceRefId::operator++`, `BRepGraph_OccurrenceRefId::operator<`, `BRepGraph_OccurrenceRefId::operator<=`, `BRepGraph_OccurrenceRefId::operator==`, `BRepGraph_OccurrenceRefId::operator>`, `BRepGraph_OccurrenceRefId::operator>=`, `BRepGraph_ProductId::operator!=`, `BRepGraph_ProductId::operator++`, `BRepGraph_ProductId::operator<`, `BRepGraph_ProductId::operator<=`, `BRepGraph_ProductId::operator==`, `BRepGraph_ProductId::operator>`, `BRepGraph_ProductId::operator>=`, `BRepGraph_RefId::operator!=`, `BRepGraph_RefId::operator++`, `BRepGraph_RefId::operator<`, `BRepGraph_RefId::operator==`, `BRepGraph_ShellId::operator!=`, `BRepGraph_ShellId::operator++`, `BRepGraph_ShellId::operator<`, `BRepGraph_ShellId::operator<=`, `BRepGraph_ShellId::operator==`, `BRepGraph_ShellId::operator>`, `BRepGraph_ShellId::operator>=`, `BRepGraph_ShellRefId::operator!=`, `BRepGraph_ShellRefId::operator++`, `BRepGraph_ShellRefId::operator<`, `BRepGraph_ShellRefId::operator<=`, `BRepGraph_ShellRefId::operator==`, `BRepGraph_ShellRefId::operator>`, `BRepGraph_ShellRefId::operator>=`, `BRepGraph_SolidId::operator!=`, `BRepGraph_SolidId::operator++`, `BRepGraph_SolidId::operator<`, `BRepGraph_SolidId::operator<=`, `BRepGraph_SolidId::operator==`, `BRepGraph_SolidId::operator>`, `BRepGraph_SolidId::operator>=`, `BRepGraph_SolidRefId::operator!=`, `BRepGraph_SolidRefId::operator++`, `BRepGraph_SolidRefId::operator<`, `BRepGraph_SolidRefId::operator<=`, `BRepGraph_SolidRefId::operator==`, `BRepGraph_SolidRefId::operator>`, `BRepGraph_SolidRefId::operator>=`, `BRepGraph_UsagePath_Step::operator==`, `BRepGraph_VersionStamp::operator!=`, `BRepGraph_VersionStamp::operator==`, `BRepGraph_VertexId::operator!=`, `BRepGraph_VertexId::operator++`, `BRepGraph_VertexId::operator<`, `BRepGraph_VertexId::operator<=`, `BRepGraph_VertexId::operator==`, `BRepGraph_VertexId::operator>`, `BRepGraph_VertexId::operator>=`, `BRepGraph_VertexRefId::operator!=`, `BRepGraph_VertexRefId::operator++`, `BRepGraph_VertexRefId::operator<`, `BRepGraph_VertexRefId::operator<=`, `BRepGraph_VertexRefId::operator==`, `BRepGraph_VertexRefId::operator>`, `BRepGraph_VertexRefId::operator>=`, `BRepGraph_WireId::operator!=`, `BRepGraph_WireId::operator++`, `BRepGraph_WireId::operator<`, `BRepGraph_WireId::operator<=`, `BRepGraph_WireId::operator==`, `BRepGraph_WireId::operator>`, `BRepGraph_WireId::operator>=`, `BRepGraph_WireRefId::operator!=`, `BRepGraph_WireRefId::operator++`, `BRepGraph_WireRefId::operator<`, `BRepGraph_WireRefId::operator<=`, `BRepGraph_WireRefId::operator==`, `BRepGraph_WireRefId::operator>`, `BRepGraph_WireRefId::operator>=`
- **BRepGraphInc:** `BRepGraph_CoEdgeCurve2DRepId::operator!=`, `BRepGraph_CoEdgeCurve2DRepId::operator++`, `BRepGraph_CoEdgeCurve2DRepId::operator<`, `BRepGraph_CoEdgeCurve2DRepId::operator<=`, `BRepGraph_CoEdgeCurve2DRepId::operator==`, `BRepGraph_CoEdgeCurve2DRepId::operator>`, `BRepGraph_CoEdgeCurve2DRepId::operator>=`, `BRepGraph_CoEdgePolygon2DRepId::operator!=`, `BRepGraph_CoEdgePolygon2DRepId::operator++`, `BRepGraph_CoEdgePolygon2DRepId::operator<`, `BRepGraph_CoEdgePolygon2DRepId::operator<=`, `BRepGraph_CoEdgePolygon2DRepId::operator==`, `BRepGraph_CoEdgePolygon2DRepId::operator>`, `BRepGraph_CoEdgePolygon2DRepId::operator>=`, `BRepGraph_CoEdgePolygonOnTriRepId::operator!=`, `BRepGraph_CoEdgePolygonOnTriRepId::operator++`, `BRepGraph_CoEdgePolygonOnTriRepId::operator<`, `BRepGraph_CoEdgePolygonOnTriRepId::operator<=`, `BRepGraph_CoEdgePolygonOnTriRepId::operator==`, `BRepGraph_CoEdgePolygonOnTriRepId::operator>`, `BRepGraph_CoEdgePolygonOnTriRepId::operator>=`, `BRepGraph_EdgeCurve3DRepId::operator!=`, `BRepGraph_EdgeCurve3DRepId::operator++`, `BRepGraph_EdgeCurve3DRepId::operator<`, `BRepGraph_EdgeCurve3DRepId::operator<=`, `BRepGraph_EdgeCurve3DRepId::operator==`, `BRepGraph_EdgeCurve3DRepId::operator>`, `BRepGraph_EdgeCurve3DRepId::operator>=`, `BRepGraph_EdgePolygon3DRepId::operator!=`, `BRepGraph_EdgePolygon3DRepId::operator++`, `BRepGraph_EdgePolygon3DRepId::operator<`, `BRepGraph_EdgePolygon3DRepId::operator<=`, `BRepGraph_EdgePolygon3DRepId::operator==`, `BRepGraph_EdgePolygon3DRepId::operator>`, `BRepGraph_EdgePolygon3DRepId::operator>=`, `BRepGraph_FaceSurfaceRepId::operator!=`, `BRepGraph_FaceSurfaceRepId::operator++`, `BRepGraph_FaceSurfaceRepId::operator<`, `BRepGraph_FaceSurfaceRepId::operator<=`, `BRepGraph_FaceSurfaceRepId::operator==`, `BRepGraph_FaceSurfaceRepId::operator>`, `BRepGraph_FaceSurfaceRepId::operator>=`, `BRepGraph_FaceTriangulationRepId::operator!=`, `BRepGraph_FaceTriangulationRepId::operator++`, `BRepGraph_FaceTriangulationRepId::operator<`, `BRepGraph_FaceTriangulationRepId::operator<=`, `BRepGraph_FaceTriangulationRepId::operator==`, `BRepGraph_FaceTriangulationRepId::operator>`, `BRepGraph_FaceTriangulationRepId::operator>=`, `BRepGraph_RepId::operator!=`, `BRepGraph_RepId::operator<`, `BRepGraph_RepId::operator==`
- **BRepMesh:** `BRepMesh_Triangle::operator==`
- **BVH:** `BVH_BitComparator::operator()`, `BVH_BitPredicate::operator()`
- **Geom2dHash:** `Geom2dHash_CurveHasher::operator()`
- **GeomHash:** `GeomHash_CurveHasher::operator()`, `GeomHash_Polygon2DHasher::operator()`, `GeomHash_Polygon3DHasher::operator()`, `GeomHash_PolygonOnTriHasher::operator()`, `GeomHash_SurfaceHasher::operator()`, `GeomHash_TriangulationHasher::operator()`
- **Graphic3d:** `Graphic3d_PolygonOffset::operator==`
- **MeshVS:** `MeshVS_TwoNodes::operator==`
- **Poly:** `Poly_MakeLoops_Link::operator==`, `Poly_Triangle::operator()`
- **Quantity:** `Quantity_Color::operator!=`, `Quantity_Color::operator==`, `Quantity_ColorRGBA::operator!=`, `Quantity_ColorRGBA::operator==`
- **StdPrs:** `StdPrs_Isolines_SegOnIso::operator<`
- **gp:** `gp_GTrsf2d::operator()`, `gp_GTrsf::operator()`, `gp_Mat2d::operator()`, `gp_Mat::operator()`

## Unlinkable

### Declared, never defined (175)

OCCT declares these (most `Standard_EXPORT`) but defines them nowhere.

- **AppDef:** `AppDef_MultiLine::SetParameter`, `AppDef_ResConstraintOfMyGradientOfCompute::Error`, `AppDef_ResConstraintOfMyGradientbisOfBSplineCompute::Error`, `AppDef_ResConstraintOfTheGradient::Error`, `AppDef_TheResol::Error`
- **BOPAlgo:** `BOPAlgo_PaveFiller::Iterator`
- **BRepApprox:** `BRepApprox_Approx::Perform`, `BRepApprox_ResConstraintOfMyGradientOfTheComputeLineBezierOfApprox::Error`, `BRepApprox_ResConstraintOfMyGradientbisOfTheComputeLineOfApprox::Error`, `BRepApprox_TheImpPrmSvSurfacesOfApprox::FillInitialVectorOfSolution`
- **BRepBlend:** `BRepBlend_CSWalking::IsDone`, `BRepBlend_CSWalking::Line`
- **BRepClass3d:** `BRepClass3d_BndBoxTreeSelectorLine::Accept`, `BRepClass3d_BndBoxTreeSelectorPoint::Accept`
- **BRepExtrema:** `BRepExtrema_OverlapTool::BRepExtrema_OverlapTool`, `BRepExtrema_OverlapTool::LoadTriangleSets`, `BRepExtrema_OverlapTool::Perform`, `BRepExtrema_ProximityDistTool::LoadAdditionalPointsFirstSet`
- **BRepFeat:** `BRepFeat::IsInOut`, `BRepFeat_MakeLinearForm::TransformShapeFU`
- **BRepGProp:** `BRepGProp_VinertGK::GetAbsolutError`
- **BRepGraph:** `BRepGraph_CopyRemap::BRepGraph_CopyRemap`, `BRepGraph_UsagePath::HashCode`, `BRepGraph_UsagePath::IsEqual`
- **BRepLib:** `BRepLib_ValidateEdge::SetExitIfToleranceExceeded`
- **BRepMesh:** `BRepMesh_ConeRangeSplitter::GetSplitSteps`, `BRepMesh_MeshTool::DumpTriangles`, `BRepMesh_MeshTool::EraseTriangles`
- **BRepOffset:** `BRepOffset_MakeOffset::GetAnalyse`, `BRepOffset_MakeSimpleOffset::GetSafeOffset`
- **BRepOffsetAPI:** `BRepOffsetAPI_FindContigousEdges::NbEdges`
- **BRepTools:** `BRepTools_PurgeLocations::ModifiedShape`
- **BSplCLib:** `BSplCLib::DN`
- **BinTools:** `BinTools_Curve2dSet::Dump`
- **BlendFunc:** `BlendFunc::Knots`, `BlendFunc::Mults`
- **CDF:** `CDF_DirectoryIterator::CDF_DirectoryIterator`
- **ChFi2d:** `FilletPoint::Copy`, `FilletPoint::FilletPoint`, `FilletPoint::FilterPoints`, `FilletPoint::appendValue`, `FilletPoint::calculateDiff`, `FilletPoint::hasSolution`, `FilletPoint::remove`
- **DsgPrs:** `DsgPrs_RadiusPresentation::Add`
- **Geom2dAPI:** `Geom2dAPI_Interpolate::ClearTangents`
- **Geom2dGcc:** `Geom2dGcc_FunctionTanCuCuCu::Geom2dGcc_FunctionTanCuCuCu`, `Geom2dGcc_Lin2dTanObl::IsParallel2`
- **Geom2dHatch:** `Geom2dHatch_Hatcher::IsDone`
- **Geom2dInt:** `Geom2dInt_Geom2dCurveTool::IsComposite`
- **GeomAPI:** `GeomAPI_Interpolate::ClearTangents`
- **GeomFill:** `GeomFill_FunctionGuide::Deriv2T`, `GeomFill_SweepSectionGenerator::GeomFill_SweepSectionGenerator`, `GeomFill_SweepSectionGenerator::Init`
- **GeomInt:** `GeomInt_IntSS::SetTolFixTangents`, `GeomInt_IntSS::TolFixTangents`, `GeomInt_ResConstraintOfMyGradientOfTheComputeLineBezierOfWLApprox::Error`, `GeomInt_ResConstraintOfMyGradientbisOfTheComputeLineOfWLApprox::Error`, `GeomInt_TheImpPrmSvSurfacesOfWLApprox::FillInitialVectorOfSolution`, `GeomInt_WLApprox::Perform`
- **HLRBRep:** `HLRBRep_BSurfaceTool::Axis`, `HLRBRep_Surface::UIntervalContinuity`, `HLRBRep_Surface::VIntervalContinuity`
- **IFSelect:** `IFSelect_ContextModif::Search`, `IFSelect_EditForm::NbTouched`, `IFSelect_IntParam::StaticName`
- **IGESSelect:** `IGESSelect_SelectBasicGeom::CurvesOnly`
- **IGESToBRep:** `IGESToBRep_TopoSurface::TransferPlaneSurface`
- **IntAna2d:** `MyDirectPolynomialRoots::MyDirectPolynomialRoots`
- **IntCurve:** `Interval::IntersectionWithBounded`, `Interval::Interval`, `Interval::Length`, `PeriodicInterval::FirstIntersection`, `PeriodicInterval::SecondIntersection`
- **IntImpParGen:** `IntImpParGen_ImpTool::D1`, `IntImpParGen_ImpTool::D2`, `IntImpParGen_ImpTool::Distance`, `IntImpParGen_ImpTool::FindParameter`, `IntImpParGen_ImpTool::GradDistance`, `IntImpParGen_ImpTool::Value`
- **IntPatch:** `IntPatch_Polyhedron::HasUMaxSingularity`, `IntPatch_Polyhedron::HasUMinSingularity`, `IntPatch_Polyhedron::HasVMaxSingularity`, `IntPatch_Polyhedron::HasVMinSingularity`, `IntPatch_Polyhedron::Perform`, `IntPatch_Polyhedron::UMaxSingularity`, `IntPatch_Polyhedron::UMinSingularity`, `IntPatch_Polyhedron::VMaxSingularity`, `IntPatch_Polyhedron::VMinSingularity`, `IntPatch_RLine::SetParamOnS1`, `IntPatch_RLine::SetParamOnS2`
- **IntPolyh:** `IntPolyh_MaillageAffinage::GetFinTE`, `IntPolyh_MaillageAffinage::GetFinTT`
- **IntTools:** `IntTools_PntOnFace::IsValid`
- **LDOM:** `LDOM_BasicElement::Create`, `LDOM_CharReference::Decode`, `LDOM_CharReference::Encode`, `LDOM_MemManager::CompareStrings`, `LDOM_MemManager::HashedAllocate`, `LDOM_XmlReader::CreateElement`, `LDOM_XmlReader::LDOM_XmlReader`, `LDOM_XmlReader::ReadRecord`, `LDOM_XmlReader::getInteger`
- **Law:** `Law_Interpolate::ClearTangents`
- **LocOpe:** `LocOpe_Revol::LocOpe_Revol`, `LocOpe_RevolutionForm::LocOpe_RevolutionForm`
- **MAT2d:** `MAT2d_CutCurve::Perform`, `MAT2d_CutCurve::PerformInf`
- **OSD:** `OSD_Path::LocateExecFile`
- **PCDM:** `PCDM_DOMHeaderParser::SetEndElementName`, `PCDM_DOMHeaderParser::SetStartElementName`, `PCDM_DOMHeaderParser::endElement`, `PCDM_DOMHeaderParser::startElement`
- **RWObj:** `RWObj_MtlReader::Read`
- **STEPSelections:** `STEPSelections_Counter::POP`, `STEPSelections_Counter::POP2`
- **SelectMgr:** `SelectMgr_SelectionImageFiller::CreateFiller`, `SelectMgr_TriangularFrustumSet::SelectMgr_TriangularFrustumSet`
- **ShapeAnalysis:** `ShapeAnalysis_BoxBndTreeSelector::Accept`, `ShapeAnalysis_BoxBndTreeSelector::Reject`
- **ShapeFix:** `ShapeFix_WireSegment::ShapeFix_WireSegment`
- **StdLPersistent:** `StdLPersistent_NamedData::Import`
- **StdObject:** `StdObject_Location::Import`
- **StdPersistent:** `StdPersistent_DataXtd_Constraint::Import`, `StdPersistent_DataXtd_PatternStd::Import`
- **StdStorage:** `StdStorage_Bucket::Clear`, `StdStorage_BucketIterator::Init`, `StdStorage_BucketIterator::Next`, `StdStorage_BucketIterator::Reset`, `StdStorage_BucketIterator::StdStorage_BucketIterator`, `StdStorage_BucketOfPersistent::Append`, `StdStorage_BucketOfPersistent::Clear`, `StdStorage_BucketOfPersistent::Value`
- **StepData:** `StepData_FreeFormEntity::StepData_FreeFormEntity`, `StepData_UndefinedEntity::Super`
- **StepFEA:** `StepFEA_SymmetricTensor43d::SetFeaIsotropicSymmetricTensor43d`
- **StepFile:** `StepFile_ReadData::AddError`, `StepFile_ReadData::AddNewScope`, `StepFile_ReadData::ClearRecorder`, `StepFile_ReadData::CreateErrorArg`, `StepFile_ReadData::CreateNewArg`, `StepFile_ReadData::CreateNewText`, `StepFile_ReadData::ErrorHandle`, `StepFile_ReadData::FinalOfHead`, `StepFile_ReadData::FinalOfScope`, `StepFile_ReadData::GetArgDescription`, `StepFile_ReadData::GetFileNbR`, `StepFile_ReadData::GetLastError`, `StepFile_ReadData::GetModePrint`, `StepFile_ReadData::GetNbRecord`, `StepFile_ReadData::GetRecordDescription`, `StepFile_ReadData::NextRecord`, `StepFile_ReadData::PrepareNewArg`, `StepFile_ReadData::PrintCurrentRecord`, `StepFile_ReadData::RecordIdent`, `StepFile_ReadData::RecordListStart`, `StepFile_ReadData::RecordNewEntity`, `StepFile_ReadData::RecordType`, `StepFile_ReadData::RecordTypeText`, `StepFile_ReadData::SetModePrint`, `StepFile_ReadData::SetTypeArg`
- **Storage:** `Storage_Bucket::Clear`, `Storage_BucketIterator::Init`, `Storage_BucketIterator::Next`, `Storage_BucketIterator::Reset`, `Storage_BucketIterator::Storage_BucketIterator`, `Storage_BucketOfPersistent::Append`, `Storage_BucketOfPersistent::Clear`, `Storage_BucketOfPersistent::Value`
- **TopOpeBRepBuild:** `TopOpeBRepBuild_Builder1::GFillSplitsPVS`
- **TransferBRep:** `TransferBRep::BRepCheck`
- **VrmlData:** `VrmlData_IndexedFaceSet::GetNormal`
- **XCAFDoc:** `XCAFDoc_GeomTolerance::XCAFDoc_GeomTolerance`
- **math:** `math_NewtonFunctionSetRoot::StateNumber`, `math_NewtonMinimum::IsConvex`

### Inline bodies calling unexported functions (68)

Defined in the headers, but calling a function the libraries don't export (in parentheses).

- **BRepGraphInc:** `BRepGraphInc_Reconstruct_Cache::BRepGraphInc_Reconstruct_Cache (Cache::Cache)`
- **BRepMesh:** `BRepMesh_Delaun::FreeEdges (BRepMesh_Delaun::getEdgesByType)`, `BRepMesh_Delaun::Frontier (BRepMesh_Delaun::getEdgesByType)`, `BRepMesh_Delaun::InternalEdges (BRepMesh_Delaun::getEdgesByType)`, `BRepMesh_Delaun::ProcessConstraints (BRepMesh_Delaun::frontierAdjust)`
- **LDOM:** `LDOM_BasicAttribute::SetValue (LDOMString::LDOMString)`, `LDOM_BasicText::SetData (LDOMString::LDOMString)`, `LDOM_MemManager::Hash (HashTable::Hash)`
- **NCollection:** `NCollection_IncAllocator_IBlock::NCollection_IncAllocator_IBlock (IBlock::IBlock)`
- **ShapePersistent:** `ShapePersistent_BRep_Curve3D::PChildren (Curve3D::PChildren)`, `ShapePersistent_BRep_Curve3D::Read (Curve3D::Read)`, `ShapePersistent_BRep_Curve3D::Write (Curve3D::Write)`, `ShapePersistent_BRep_Curve3D::import (Curve3D::import)`, `ShapePersistent_BRep_CurveOn2Surfaces::PChildren (CurveOn2Surfaces::PChildren)`, `ShapePersistent_BRep_CurveOn2Surfaces::Read (CurveOn2Surfaces::Read)`, `ShapePersistent_BRep_CurveOn2Surfaces::Write (CurveOn2Surfaces::Write)`, `ShapePersistent_BRep_CurveOn2Surfaces::import (CurveOn2Surfaces::import)`, `ShapePersistent_BRep_CurveOnClosedSurface::PChildren (CurveOnClosedSurface::PChildren)`, `ShapePersistent_BRep_CurveOnClosedSurface::Read (CurveOnClosedSurface::Read)`, `ShapePersistent_BRep_CurveOnClosedSurface::Write (CurveOnClosedSurface::Write)`, `ShapePersistent_BRep_CurveOnClosedSurface::import (CurveOnClosedSurface::import)`, `ShapePersistent_BRep_CurveOnSurface::PChildren (CurveOnSurface::PChildren)`, `ShapePersistent_BRep_CurveOnSurface::Read (CurveOnSurface::Read)`, `ShapePersistent_BRep_CurveOnSurface::Write (CurveOnSurface::Write)`, `ShapePersistent_BRep_CurveOnSurface::import (CurveOnSurface::import)`, `ShapePersistent_BRep_CurveRepresentation::PChildren (CurveRepresentation::PChildren)`, `ShapePersistent_BRep_GCurve::Read (GCurve::Read)`, `ShapePersistent_BRep_GCurve::Write (GCurve::Write)`, `ShapePersistent_BRep_PointOnCurve::PChildren (PointOnCurve::PChildren)`, `ShapePersistent_BRep_PointOnCurve::Read (PointOnCurve::Read)`, `ShapePersistent_BRep_PointOnCurve::Write (PointOnCurve::Write)`, `ShapePersistent_BRep_PointOnCurve::import (PointOnCurve::import)`, `ShapePersistent_BRep_PointOnCurveOnSurface::PChildren (PointOnCurveOnSurface::PChildren)`, `ShapePersistent_BRep_PointOnCurveOnSurface::Read (PointOnCurveOnSurface::Read)`, `ShapePersistent_BRep_PointOnCurveOnSurface::Write (PointOnCurveOnSurface::Write)`, `ShapePersistent_BRep_PointOnCurveOnSurface::import (PointOnCurveOnSurface::import)`, `ShapePersistent_BRep_PointOnSurface::Read (PointOnSurface::Read)`, `ShapePersistent_BRep_PointOnSurface::Write (PointOnSurface::Write)`, `ShapePersistent_BRep_PointOnSurface::import (PointOnSurface::import)`, `ShapePersistent_BRep_PointRepresentation::PChildren (PointRepresentation::PChildren)`, `ShapePersistent_BRep_PointsOnSurface::PChildren (PointsOnSurface::PChildren)`, `ShapePersistent_BRep_PointsOnSurface::Read (PointsOnSurface::Read)`, `ShapePersistent_BRep_PointsOnSurface::Write (PointsOnSurface::Write)`, `ShapePersistent_BRep_Polygon3D::PChildren (Polygon3D::PChildren)`, `ShapePersistent_BRep_Polygon3D::Read (Polygon3D::Read)`, `ShapePersistent_BRep_Polygon3D::Write (Polygon3D::Write)`, `ShapePersistent_BRep_Polygon3D::import (Polygon3D::import)`, `ShapePersistent_BRep_PolygonOnClosedSurface::PChildren (PolygonOnClosedSurface::PChildren)`, `ShapePersistent_BRep_PolygonOnClosedSurface::Read (PolygonOnClosedSurface::Read)`, `ShapePersistent_BRep_PolygonOnClosedSurface::Write (PolygonOnClosedSurface::Write)`, `ShapePersistent_BRep_PolygonOnClosedSurface::import (PolygonOnClosedSurface::import)`, `ShapePersistent_BRep_PolygonOnClosedTriangulation::PChildren (PolygonOnClosedTriangulation::PChildren)`, `ShapePersistent_BRep_PolygonOnClosedTriangulation::Read (PolygonOnClosedTriangulation::Read)`, `ShapePersistent_BRep_PolygonOnClosedTriangulation::Write (PolygonOnClosedTriangulation::Write)`, `ShapePersistent_BRep_PolygonOnClosedTriangulation::import (PolygonOnClosedTriangulation::import)`, `ShapePersistent_BRep_PolygonOnSurface::PChildren (PolygonOnSurface::PChildren)`, `ShapePersistent_BRep_PolygonOnSurface::Read (PolygonOnSurface::Read)`, `ShapePersistent_BRep_PolygonOnSurface::Write (PolygonOnSurface::Write)`, `ShapePersistent_BRep_PolygonOnSurface::import (PolygonOnSurface::import)`, `ShapePersistent_BRep_PolygonOnTriangulation::PChildren (PolygonOnTriangulation::PChildren)`, `ShapePersistent_BRep_PolygonOnTriangulation::Read (PolygonOnTriangulation::Read)`, `ShapePersistent_BRep_PolygonOnTriangulation::Write (PolygonOnTriangulation::Write)`, `ShapePersistent_BRep_PolygonOnTriangulation::import (PolygonOnTriangulation::import)`
- **StdPersistent:** `StdPersistent_Naming_NamedShape::Import (NamedShape::Import)`, `StdPersistent_PPrsStd_AISPresentation::Import (AISPresentation::Import)`, `StdPersistent_PPrsStd_AISPresentation_1::Import (AISPresentation_1::Import)`, `StdPersistent_TopLoc_Datum3D::Read (Datum3D::Read)`, `StdPersistent_TopLoc_Datum3D::Write (Datum3D::Write)`

### Vtables naming unexported functions (29)

An inline constructor or copy of a class the libraries don't export makes MSVC emit its vtable in the shim, and the vtable names a virtual function that isn't exported (in parentheses). An inline delegating constructor references the vtable without emitting it, and the libraries export the class's virtual functions but not its vtable.

- **BRepClass3d:** `BRepClass3d_BndBoxTreeSelectorLine::BRepClass3d_BndBoxTreeSelectorLine (BRepClass3d_BndBoxTreeSelectorLine::Accept)`, `BRepClass3d_BndBoxTreeSelectorPoint::BRepClass3d_BndBoxTreeSelectorPoint (BRepClass3d_BndBoxTreeSelectorPoint::Accept)`
- **PCDM:** `PCDM_DOMHeaderParser::PCDM_DOMHeaderParser (PCDM_DOMHeaderParser::startElement)`
- **Select3D:** `Select3D_SensitiveCircle::Select3D_SensitiveCircle`
- **ShapeAnalysis:** `ShapeAnalysis_BoxBndTreeSelector::ShapeAnalysis_BoxBndTreeSelector (ShapeAnalysis_BoxBndTreeSelector::Reject)`
- **ShapePersistent:** `ShapePersistent_BRep_Curve3D::ShapePersistent_BRep_Curve3D (Curve3D::Read)`, `ShapePersistent_BRep_CurveOn2Surfaces::ShapePersistent_BRep_CurveOn2Surfaces (CurveOn2Surfaces::Read)`, `ShapePersistent_BRep_CurveOnClosedSurface::ShapePersistent_BRep_CurveOnClosedSurface (CurveOnClosedSurface::Read)`, `ShapePersistent_BRep_CurveOnSurface::ShapePersistent_BRep_CurveOnSurface (CurveOnSurface::Read)`, `ShapePersistent_BRep_CurveRepresentation::ShapePersistent_BRep_CurveRepresentation (CurveRepresentation::PChildren)`, `ShapePersistent_BRep_GCurve::ShapePersistent_BRep_GCurve (GCurve::Read)`, `ShapePersistent_BRep_PointOnCurve::ShapePersistent_BRep_PointOnCurve (PointOnCurve::Read)`, `ShapePersistent_BRep_PointOnCurveOnSurface::ShapePersistent_BRep_PointOnCurveOnSurface (PointOnCurveOnSurface::Read)`, `ShapePersistent_BRep_PointOnSurface::ShapePersistent_BRep_PointOnSurface (PointOnSurface::Read)`, `ShapePersistent_BRep_PointRepresentation::ShapePersistent_BRep_PointRepresentation (PointRepresentation::PChildren)`, `ShapePersistent_BRep_PointsOnSurface::ShapePersistent_BRep_PointsOnSurface (PointsOnSurface::Read)`, `ShapePersistent_BRep_Polygon3D::ShapePersistent_BRep_Polygon3D (Polygon3D::Read)`, `ShapePersistent_BRep_PolygonOnClosedSurface::ShapePersistent_BRep_PolygonOnClosedSurface (PolygonOnClosedSurface::Read)`, `ShapePersistent_BRep_PolygonOnClosedTriangulation::ShapePersistent_BRep_PolygonOnClosedTriangulation (PolygonOnClosedTriangulation::Read)`, `ShapePersistent_BRep_PolygonOnSurface::ShapePersistent_BRep_PolygonOnSurface (PolygonOnSurface::Read)`, `ShapePersistent_BRep_PolygonOnTriangulation::ShapePersistent_BRep_PolygonOnTriangulation (PolygonOnTriangulation::Read)`, `ShapePersistent_BRep_TEdge_pTObjectT::ShapePersistent_BRep_TEdge_pTObjectT (pTEdge::createTShape)`, `ShapePersistent_BRep_TFace_pTObjectT::ShapePersistent_BRep_TFace_pTObjectT (pTFace::createTShape)`, `ShapePersistent_BRep_TVertex_pTObjectT::ShapePersistent_BRep_TVertex_pTObjectT (pTVertex::createTShape)`, `ShapePersistent_Poly_Polygon2D::ShapePersistent_Poly_Polygon2D (pPolygon2D::Import)`, `ShapePersistent_Poly_Polygon3D::ShapePersistent_Poly_Polygon3D (pPolygon3D::Import)`, `ShapePersistent_Poly_PolygonOnTriangulation::ShapePersistent_Poly_PolygonOnTriangulation (pPolygonOnTriangulation::Import)`, `ShapePersistent_Poly_Triangulation::ShapePersistent_Poly_Triangulation (pTriangulation::Import)`
- **StdPersistent:** `StdPersistent_TopLoc_Datum3D::StdPersistent_TopLoc_Datum3D (Datum3D::Read)`
