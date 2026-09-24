// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/* TNaming: companion of the generated TNaming.i, included before its classes. */

%{
extern "C" void* SWIGSTDCALL NetOcc_TDF_AcquireAttributeData(void* theAttribute);

// TNaming_Builder holds handles to TNaming_UsedShapes and its TNaming_NamedShape:
// deleting it after its tree died crashes like a surviving attribute (see extras/TDF.i).
extern "C" SWIGEXPORT void* SWIGSTDCALL NetOcc_TDF_AcquireBuilderData(void* theBuilder) {
  if (theBuilder == nullptr) {
    return nullptr;
  }
  occ::handle<TNaming_NamedShape> aNamed = static_cast<const TNaming_Builder*>(theBuilder)->NamedShape();
  return NetOcc_TDF_AcquireAttributeData(aNamed.get());
}
%}

%occt_tdf_proxy(TNaming_Builder, global::OCC.Core.TDF.DataReference.AcquireFromBuilder(cPtr))
