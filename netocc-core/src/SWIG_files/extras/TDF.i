// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * TDF: companion of the generated TDF.i, included before its classes. TDF_Label is a copyable
 * value class (a pointer to a node owned by its TDF_Data); attributes are transients found by GUID.
 *
 * Tree keep-alive: label, attribute and TNaming_Builder proxies hold one native
 * reference on the TDF_Data of their tree and drop it in Dispose, after their own
 * object. ~TDF_Data frees the label nodes but never detaches attributes that are
 * still referenced: a surviving attribute keeps a dangling label plus its sibling
 * chain, and releasing a TNaming_NamedShape then reads freed memory
 * (Clear() -> Label().Root()). Finalizers run in any order, so the tree has to
 * outlive every proxy that points into it.
 *
 * Holders of attributes (a delta of the undo stack, a relocation table, a data set, a
 * TDF_CopyLabel once it performed) crash the same way when released after their tree: their
 * proxies keep every tree they refer to (a copy between documents refers to two), taken again
 * after the calls that fill them.
 */

%{
// Entry points of OCC.Core.TDF.DataReference (hand-written P/Invoke).
extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireLabelData(void* theLabel) {
  const TDF_Label* aLabel = static_cast<const TDF_Label*>(theLabel);
  if (aLabel == nullptr || aLabel->IsNull()) {
    return nullptr;
  }
  TDF_Data* aData = aLabel->Data().get();
  if (aData != nullptr) {
    aData->IncrementRefCounter();
  }
  return aData;
}

extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireAttributeData(void* theAttribute) {
  if (theAttribute == nullptr) {
    return nullptr;
  }
  const TDF_Label aLabel = static_cast<const TDF_Attribute*>(theAttribute)->Label();
  return NetOcc_TDF_AcquireLabelData((void*)&aLabel);
}

extern "C" SWIGEXPORT void SWIGSTDCALL NetOcc_TDF_ReleaseData(void* theData) {
  TDF_Data* aData = static_cast<TDF_Data*>(theData);
  if (aData != nullptr && aData->DecrementRefCounter() == 0) {
    aData->Delete();
  }
}

#include <vector>

// The trees of the labels and attributes a holder refers to, each held once.
struct NetOcc_TDF_Trees {
  std::vector<occ::handle<TDF_Data>> Data;

  void Add(const TDF_Label& theLabel) {
    if (theLabel.IsNull()) {
      return;
    }
    const occ::handle<TDF_Data> aData = theLabel.Data();
    for (const occ::handle<TDF_Data>& aHeld : Data) {
      if (aHeld == aData) {
        return;
      }
    }
    if (!aData.IsNull()) {
      Data.push_back(aData);
    }
  }

  void Add(const occ::handle<TDF_Attribute>& theAttribute) {
    if (!theAttribute.IsNull()) {
      Add(theAttribute->Label());
    }
  }

  void Add(TDF_RelocationTable& theTable) {
    for (NCollection_DataMap<TDF_Label, TDF_Label>::Iterator anIt(theTable.LabelTable()); anIt.More(); anIt.Next()) {
      Add(anIt.Key());
      Add(anIt.Value());
    }
    for (NCollection_DataMap<occ::handle<TDF_Attribute>, occ::handle<TDF_Attribute>>::Iterator anIt(theTable.AttributeTable());
         anIt.More(); anIt.Next()) {
      Add(anIt.Key());
      Add(anIt.Value());
    }
    NCollection_IndexedDataMap<occ::handle<Standard_Transient>, occ::handle<Standard_Transient>>& aTransients =
      theTable.TransientTable();
    for (int anIndex = 1; anIndex <= aTransients.Extent(); ++anIndex) {
      Add(occ::handle<TDF_Attribute>::DownCast(aTransients.FindKey(anIndex)));
      Add(occ::handle<TDF_Attribute>::DownCast(aTransients.FindFromIndex(anIndex)));
    }
  }

  // null for none: an empty holder needs no tree
  void* Take() {
    if (Data.empty()) {
      delete this;
      return nullptr;
    }
    return this;
  }
};

extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireDeltaTrees(void* theDelta) {
  NetOcc_TDF_Trees* aTrees = new NetOcc_TDF_Trees();
  if (theDelta != nullptr) {
    for (NCollection_List<occ::handle<TDF_AttributeDelta>>::Iterator anIt(static_cast<TDF_Delta*>(theDelta)->AttributeDeltas());
         anIt.More(); anIt.Next()) {
      aTrees->Add(anIt.Value()->Label());
    }
  }
  return aTrees->Take();
}

extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireAttributeDeltaTrees(void* theDelta) {
  NetOcc_TDF_Trees* aTrees = new NetOcc_TDF_Trees();
  if (theDelta != nullptr) {
    aTrees->Add(static_cast<TDF_AttributeDelta*>(theDelta)->Label());
  }
  return aTrees->Take();
}

extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireRelocationTrees(void* theTable) {
  NetOcc_TDF_Trees* aTrees = new NetOcc_TDF_Trees();
  if (theTable != nullptr) {
    aTrees->Add(*static_cast<TDF_RelocationTable*>(theTable));
  }
  return aTrees->Take();
}

extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireDataSetTrees(void* theDataSet) {
  NetOcc_TDF_Trees* aTrees = new NetOcc_TDF_Trees();
  if (theDataSet != nullptr) {
    TDF_DataSet* aDataSet = static_cast<TDF_DataSet*>(theDataSet);
    for (NCollection_Map<TDF_Label>::Iterator anIt(aDataSet->Labels()); anIt.More(); anIt.Next()) {
      aTrees->Add(anIt.Key());
    }
    for (NCollection_Map<occ::handle<TDF_Attribute>>::Iterator anIt(aDataSet->Attributes()); anIt.More(); anIt.Next()) {
      aTrees->Add(anIt.Key());
    }
  }
  return aTrees->Take();
}

// a copy holds attributes once it performed, in its relocation table and its external references,
// which are in the source tree the table maps
extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireCopyTrees(void* theCopy) {
  if (theCopy == nullptr) {
    return nullptr;
  }
  const occ::handle<TDF_RelocationTable>& aTable = static_cast<TDF_CopyLabel*>(theCopy)->RelocationTable();
  return aTable.IsNull() ? nullptr : NetOcc_TDF_AcquireRelocationTrees(aTable.get());
}

extern "C" SWIGEXPORT void SWIGSTDCALL NetOcc_TDF_ReleaseTrees(void* theTrees) {
  delete static_cast<NetOcc_TDF_Trees*>(theTrees);
}
%}

// Base proxy class TYPE holding a TDF_Data reference; ACQUIRE is a C# expression of cPtr, RELEASE
// the DataReference method that drops what it returned.
%define %occt_tdf_proxy(TYPE, ACQUIRE, RELEASE)
%typemap(csbody) TYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  protected bool swigCMemOwn;
  private global::System.IntPtr netoccData;
  // a borrowed object's owner, or what the object keeps (see References.i)
  internal object netoccOwner;

  internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) {
    swigCMemOwn = cMemoryOwn;
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
    netoccData = ACQUIRE;
  }

  // A call wrote another value into this proxy (TYPE& parameter): follow its tree.
  internal void NetOccRetainData() {
    global::System.IntPtr cPtr = swigCPtr.Handle;
    global::System.IntPtr previous = netoccData;
    netoccData = ACQUIRE;
    global::OCC.Core.TDF.DataReference.RELEASE(previous);
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }
%}
%typemap(csdisposing, methodname="Dispose", methodmodifiers="protected", parameters="bool disposing") TYPE {
    lock(this) {
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
      }
      global::OCC.Core.TDF.DataReference.RELEASE(netoccData);
      netoccData = global::System.IntPtr.Zero;
    }
  }
%enddef

// The same for a transient TYPE: its proxy derives from Standard_Transient's.
%define %occt_tdf_proxy_derived(TYPE, ACQUIRE, RELEASE)
%typemap(csbody_derived) TYPE %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  private global::System.IntPtr netoccData;

  internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) : base($imclassname.$csclazznameSWIGUpcast(cPtr), cMemoryOwn) {
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
    netoccData = ACQUIRE;
  }

  // A call changed what the object holds: follow its trees.
  internal void NetOccRetainData() {
    global::System.IntPtr cPtr = swigCPtr.Handle;
    global::System.IntPtr previous = netoccData;
    netoccData = ACQUIRE;
    global::OCC.Core.TDF.DataReference.RELEASE(previous);
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }
%}
%typemap(csdisposing_derived, methodname="Dispose", methodmodifiers="protected", parameters="bool disposing") TYPE {
    lock(this) {
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
      }
      base.Dispose(disposing);
      global::OCC.Core.TDF.DataReference.RELEASE(netoccData);
      netoccData = global::System.IntPtr.Zero;
    }
  }
%enddef

%occt_tdf_proxy(TDF_Label, global::OCC.Core.TDF.DataReference.AcquireFromLabel(cPtr), Release)

// TDF_Label& arguments may come back holding a label of another tree.
%typemap(csin, post="      if ($csinput != null) $csinput.NetOccRetainData();") TDF_Label& "TDF_Label.getCPtr($csinput)"
%typemap(csin) const TDF_Label& "TDF_Label.getCPtr($csinput)"

// Attributes: the reference is taken once in the TDF_Attribute base constructor and
// dropped after the most derived class released the attribute.
%typemap(csbody_derived) TDF_Attribute %{
  private global::System.Runtime.InteropServices.HandleRef swigCPtr;
  private global::System.IntPtr netoccData;

  internal $csclassname(global::System.IntPtr cPtr, bool cMemoryOwn) : base($imclassname.$csclazznameSWIGUpcast(cPtr), cMemoryOwn) {
    swigCPtr = new global::System.Runtime.InteropServices.HandleRef(this, cPtr);
    netoccData = global::OCC.Core.TDF.DataReference.AcquireFromAttribute(cPtr);
  }

  internal static global::System.Runtime.InteropServices.HandleRef getCPtr($csclassname obj) {
    return (obj == null) ? new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero) : obj.swigCPtr;
  }
%}
%typemap(csdisposing_derived, methodname="Dispose", methodmodifiers="protected", parameters="bool disposing") TDF_Attribute {
    lock(this) {
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(null, global::System.IntPtr.Zero);
      }
      base.Dispose(disposing);
      global::OCC.Core.TDF.DataReference.Release(netoccData);
      netoccData = global::System.IntPtr.Zero;
    }
  }

// Holders: an undo record and its attribute deltas are filled when OCCT hands them out; a relocation
// table, a data set and a copy take their trees again after the calls that fill them, their own and
// the OCCT calls they are passed to (TDF_ClosureTool, TDF_CopyTool). Labels alone hold nothing.
%occt_tdf_proxy_derived(TDF_Delta, global::OCC.Core.TDF.DataReference.AcquireFromDelta(cPtr), ReleaseTrees)
%occt_tdf_proxy_derived(TDF_AttributeDelta, global::OCC.Core.TDF.DataReference.AcquireFromAttributeDelta(cPtr), ReleaseTrees)
%occt_tdf_proxy_derived(TDF_RelocationTable, global::OCC.Core.TDF.DataReference.AcquireFromRelocationTable(cPtr), ReleaseTrees)
%occt_tdf_proxy_derived(TDF_DataSet, global::OCC.Core.TDF.DataReference.AcquireFromDataSet(cPtr), ReleaseTrees)
%occt_tdf_proxy(TDF_CopyLabel, global::OCC.Core.TDF.DataReference.AcquireFromCopy(cPtr), ReleaseTrees)

%typemap(csout, excode=SWIGEXCODE) void TDF_CopyLabel::Perform, void TDF_RelocationTable::SetRelocation,
                                   void TDF_RelocationTable::SetTransientRelocation, void TDF_DataSet::AddAttribute {
    $imcall;$excode
    NetOccRetainData();
  }

%typemap(csin, post="      $csinput?.NetOccRetainData();") opencascade::handle< TDF_DataSet >,
         const opencascade::handle< TDF_DataSet >& "TDF_DataSet.getCPtr($csinput)"
%typemap(csin, post="      $csinput?.NetOccRetainData();") opencascade::handle< TDF_RelocationTable >,
         const opencascade::handle< TDF_RelocationTable >& "TDF_RelocationTable.getCPtr($csinput)"

%csmethodmodifiers TDF_Label::NetOcc_HashCode "internal";

%extend TDF_Label {
  int NetOcc_HashCode() const { return (int)std::hash<TDF_Label>{}(*$self); }
  %proxycode %{
  // C++ operator== is IsEqual (same label node).
  public override bool Equals(object obj) {
    var other = obj as TDF_Label;
    return !ReferenceEquals(other, null) && IsEqual(other);
  }

  public override int GetHashCode() {
    return NetOcc_HashCode();
  }
  %}
}
