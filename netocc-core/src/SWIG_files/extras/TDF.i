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
%}

// Base proxy class TYPE holding a TDF_Data reference; ACQUIRE is a C# expression of cPtr.
%define %occt_tdf_proxy(TYPE, ACQUIRE)
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
    global::OCC.Core.TDF.DataReference.Release(previous);
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
      global::OCC.Core.TDF.DataReference.Release(netoccData);
      netoccData = global::System.IntPtr.Zero;
    }
  }
%enddef

%occt_tdf_proxy(TDF_Label, global::OCC.Core.TDF.DataReference.AcquireFromLabel(cPtr))

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
