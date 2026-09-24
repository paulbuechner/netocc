// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: stand-ins for OCCT's handle and the root of its handle hierarchy, and a legacy handle class
// (DEFINE_STANDARD_HANDLE), which C# covers with the proxy: neither a proxy nor a skip.
#pragma once

namespace opencascade
{
template <class T>
class handle
{
public:
  handle() : myEntity(nullptr) {}
  T* get() const { return myEntity; }

private:
  T* myEntity;
};
} // namespace opencascade

class Standard_Transient
{
public:
  Standard_Transient() : myRefCount(0) {}
  virtual ~Standard_Transient() {}
  int GetRefCount() const { return myRefCount; }
  void IncrementRefCounter() { ++myRefCount; }

private:
  int myRefCount;
};

class [[deprecated("use the handle")]] Handle_Standard_Transient : public opencascade::handle<Standard_Transient>
{
};
