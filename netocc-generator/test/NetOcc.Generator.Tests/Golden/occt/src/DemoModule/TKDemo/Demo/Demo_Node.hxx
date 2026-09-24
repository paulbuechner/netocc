// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

// Golden fixture: allocated only through an allocator, like NCollection nodes: no constructors, no deleting destructor.
#pragma once

typedef decltype(sizeof(0)) Demo_Size;

class Demo_Allocator
{
public:
  Demo_Allocator() {}
};

class Demo_Node
{
public:
  Demo_Node() : myIndex(0) {}
  void* operator new(Demo_Size theSize, Demo_Allocator& theAllocator) { return nullptr; }
  void operator delete(void* theAddress, Demo_Allocator& theAllocator) {}
  int Index() const { return myIndex; }

private:
  int myIndex;
};
