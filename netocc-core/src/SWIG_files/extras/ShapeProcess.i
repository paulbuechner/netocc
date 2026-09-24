// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * ShapeProcess companion: ToOperationFlag returns std::pair<Operation, bool>, and a pair's accessors are %extend, whose
 * casts SWIG writes with "enum ShapeProcess_Operation", which a nested enum's flat alias can't follow. This accessor
 * returns the operation and reports through theIsKnown whether the name is one.
 */
%rename(ToOperationFlag) ShapeProcess::NetOcc_ToOperationFlag;
%extend ShapeProcess {
  static ShapeProcess_Operation NetOcc_ToOperationFlag(const char* theName, bool& theIsKnown) {
    const std::pair<ShapeProcess::Operation, bool> aFlag = ShapeProcess::ToOperationFlag(theName);
    theIsKnown = aFlag.second;
    return aFlag.first;
  }
}
