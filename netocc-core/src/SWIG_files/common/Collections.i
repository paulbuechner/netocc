// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * NCollection templates. netocc-gen instantiates the ones signatures use, each named by OCCT's
 * alias (NCollection_Array1<gp_Pnt> is TColgp_Array1OfPnt) in the alias's package. Every macro
 * takes (NAME, TYPE, CSTYPE): the C# class, the element's C++ type and its C# type.
 *
 *   %occt_array1            indexer from Lower() to Upper(), IEnumerable<CSTYPE>
 *   %occt_array1_blittable  the same plus bulk copies, for elements C# can copy as bytes:
 *                           new NAME(CSTYPE[]) (Lower() = 1) and ToArray()
 *   %occt_array2            indexer [row, col]
 *   %occt_list              IEnumerable<CSTYPE>; no index access, OCCT's list is linked
 *   %occt_sequence          indexer from 1 to Length(), as in OCCT; IEnumerable<CSTYPE>
 *   %occt_linearvector      OCCT 8's contiguous vector: indexer from 0, Append, IEnumerable<CSTYPE>;
 *                           %occt_linearvector_blittable adds new NAME(CSTYPE[]) and ToArray()
 *   %occt_dynamicarray      OCCT 8's block array (NCollection_Vector): indexer from 0, Append, IEnumerable<CSTYPE>
 *   %occt_harray1, %occt_harray2, %occt_hsequence
 *                           the handle-managed variants: subclasses of the plain collection,
 *                           which must be instantiated first, with Handle(NAME) <-> NAME and DownCast
 *
 * Maps take their hasher too, which C# doesn't see: (NAME, KEY, HASHER, CSKEY) and
 * (NAME, KEY, ITEM, HASHER, CSKEY, CSITEM). An argument that holds a comma comes through %arg(...), and the macros pass
 * their types on through %arg too.
 *
 *   %occt_map               IEnumerable<CSKEY>
 *   %occt_indexedmap        indexer from 1 to Extent() (the keys), IEnumerable<CSKEY>
 *   %occt_datamap           indexer by key (Find/Bind), ContainsKey, TryGetValue,
 *                           IEnumerable<KeyValuePair<CSKEY, CSITEM>>
 *   %occt_indexeddatamap    indexer from 1 to Extent() (the items), IEnumerable<KeyValuePair<...>>
 *                           in index order
 *   %occt_flatmap, %occt_flatdatamap
 *                           OCCT 8's open-addressing maps, as %occt_map and %occt_datamap
 *
 * The plain collections are value classes, so a const& return is an owned copy. A bad index
 * throws OcctException (Standard_OutOfRange). Changing a collection while enumerating it is
 * undefined, as in C++.
 */

%{
#include <algorithm>
#include <NCollection_Array1.hxx>
#include <NCollection_Array2.hxx>
#include <NCollection_HArray1.hxx>
#include <NCollection_HArray2.hxx>
#include <NCollection_DataMap.hxx>
#include <NCollection_DynamicArray.hxx>
#include <NCollection_FlatDataMap.hxx>
#include <NCollection_FlatMap.hxx>
#include <NCollection_HSequence.hxx>
#include <NCollection_IndexedDataMap.hxx>
#include <NCollection_IndexedMap.hxx>
#include <NCollection_LinearVector.hxx>
#include <NCollection_List.hxx>
#include <NCollection_Map.hxx>
#include <NCollection_Sequence.hxx>

// an OCCT iterator the C# enumerator of a linked collection or a map owns: an address (Types.i's void*)
typedef void* NetOcc_Iterator;
%}

typedef void* NetOcc_Iterator;

/*
 * OCCT 8's range-for protocol (NCollection_ForwardRange.hxx): begin() and end() over a class's own More(), Next()
 * and accessor (Value, Current or CurrentId). In C#, IEnumerable<CSTYPE> over the same: foreach advances the
 * iterator itself, as range-for does in C++. netocc-gen calls this before the class and leaves begin/end out.
 */
%define %occt_forward_range(CLASS, CSTYPE, ACCESSOR)
%typemap(csinterfaces) CLASS "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<CSTYPE>"
%typemap(csinterfaces_derived) CLASS "global::System.Collections.Generic.IEnumerable<CSTYPE>"
%typemap(cscode) CLASS %{
  public global::System.Collections.Generic.IEnumerator<CSTYPE> GetEnumerator() {
    for (; More(); Next()) {
      yield return ACCESSOR();
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%enddef

// plumbing for the C# members the macros add
%csmethodmodifiers NetOcc_Begin "internal";
%csmethodmodifiers NetOcc_More "internal";
%csmethodmodifiers NetOcc_Next "internal";
%csmethodmodifiers NetOcc_Key "internal";
%csmethodmodifiers NetOcc_Value "internal";
%csmethodmodifiers NetOcc_End "internal";
%csmethodmodifiers NetOcc_CopyTo "internal";
%csmethodmodifiers NetOcc_CopyFrom "internal";

/*
 * The enumerator of a linked collection or a map: an OCCT iterator the C# enumerator owns. The template's %extend
 * declares its plumbing (%netocc_iterator: NetOcc_Next returns ACCESSOR() and advances; %netocc_pair_iterator: Key
 * and Value apart), the instance's cscode enumerates it.
 */
%define %netocc_iterator(ITERATOR, ITEM, ACCESSOR)
    NetOcc_Iterator NetOcc_Begin() const { return new ITERATOR(*$self); }
    bool NetOcc_More(NetOcc_Iterator theIterator) const { return static_cast<ITERATOR*>(theIterator)->More(); }
    const ITEM& NetOcc_Next(NetOcc_Iterator theIterator) const {
      ITERATOR* anIterator = static_cast<ITERATOR*>(theIterator);
      const ITEM& aValue = anIterator->ACCESSOR();
      anIterator->Next();
      return aValue;
    }
    void NetOcc_End(NetOcc_Iterator theIterator) const { delete static_cast<ITERATOR*>(theIterator); }
%enddef

%define %netocc_pair_iterator(ITERATOR, KEY, ITEM)
    NetOcc_Iterator NetOcc_Begin() const { return new ITERATOR(*$self); }
    bool NetOcc_More(NetOcc_Iterator theIterator) const { return static_cast<ITERATOR*>(theIterator)->More(); }
    const KEY& NetOcc_Key(NetOcc_Iterator theIterator) const { return static_cast<ITERATOR*>(theIterator)->Key(); }
    const ITEM& NetOcc_Value(NetOcc_Iterator theIterator) const { return static_cast<ITERATOR*>(theIterator)->Value(); }
    void NetOcc_Next(NetOcc_Iterator theIterator) const { static_cast<ITERATOR*>(theIterator)->Next(); }
    void NetOcc_End(NetOcc_Iterator theIterator) const { delete static_cast<ITERATOR*>(theIterator); }
%enddef

// IEnumerable<CSTYPE> over %netocc_iterator; COUNT: the element count
%define %netocc_iterator_cscode(CLASS, CSTYPE, COUNT)
%typemap(csinterfaces) CLASS "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<CSTYPE>"
%typemap(cscode) CLASS %{
  public int Count => COUNT;

  public global::System.Collections.Generic.IEnumerator<CSTYPE> GetEnumerator() {
    global::System.IntPtr iterator = NetOcc_Begin();
    try {
      while (NetOcc_More(iterator)) {
        yield return NetOcc_Next(iterator);
      }
    } finally {
      NetOcc_End(iterator);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%enddef

// an indexer over Value and SetValue, IEnumerable<CSTYPE> in index order: from FIRST to LAST
%define %netocc_indexed_cscode(CLASS, CSTYPE, FIRST, LAST)
%typemap(csinterfaces) CLASS "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<CSTYPE>"
%typemap(cscode) CLASS %{
  public int Count => Length();

  public CSTYPE this[int index] {
    get => Value(index);
    set => SetValue(index, value);
  }

  public global::System.Collections.Generic.IEnumerator<CSTYPE> GetEnumerator() {
    int last = LAST;
    for (int i = FIRST; i <= last; i++) {
      yield return Value(i);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%enddef

// a map from keys to items over %netocc_pair_iterator: indexer by key, ContainsKey, TryGetValue, IEnumerable<KeyValuePair>
%define %netocc_datamap_cscode(CLASS, CSKEY, CSITEM)
%typemap(csinterfaces) CLASS "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<CSKEY, CSITEM>>"
%typemap(cscode) CLASS %{
  public int Count => Extent();

  public CSITEM this[CSKEY key] {
    get => Find(key);
    set => Bind(key, value);
  }

  public bool ContainsKey(CSKEY key) => IsBound(key);

  public bool TryGetValue(CSKEY key, out CSITEM value) {
    bool found = IsBound(key);
    value = found ? Find(key) : default(CSITEM);
    return found;
  }

  public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<CSKEY, CSITEM>> GetEnumerator() {
    global::System.IntPtr iterator = NetOcc_Begin();
    try {
      while (NetOcc_More(iterator)) {
        yield return new global::System.Collections.Generic.KeyValuePair<CSKEY, CSITEM>(NetOcc_Key(iterator), NetOcc_Value(iterator));
        NetOcc_Next(iterator);
      }
    } finally {
      NetOcc_End(iterator);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%enddef

template <class TheItemType> class NCollection_Array1 {
public:
  NCollection_Array1();
  NCollection_Array1(const int theLower, const int theUpper);
  void Init(const TheItemType& theValue);
  int Length() const;
  bool IsEmpty() const;
  int Lower() const;
  int Upper() const;
  const TheItemType& Value(const int theIndex) const;
  void SetValue(const int theIndex, const TheItemType& theItem);
  void UpdateLowerBound(const int theLower);
  void Resize(const int theLower, const int theUpper, const bool theToCopyData);
  %extend {
    // unchecked in OCCT
    const TheItemType& First() const {
      if ($self->IsEmpty()) throw Standard_OutOfRange("NCollection_Array1::First: the array is empty");
      return $self->First();
    }
    const TheItemType& Last() const {
      if ($self->IsEmpty()) throw Standard_OutOfRange("NCollection_Array1::Last: the array is empty");
      return $self->Last();
    }
  }
};

%define %occt_array1(NAME, TYPE, CSTYPE)
%netocc_indexed_cscode(%arg(NCollection_Array1< TYPE >), CSTYPE, Lower(), Upper())
%occt_valueclass(%arg(NCollection_Array1< TYPE >))
%template(NAME) NCollection_Array1< TYPE >;
%enddef

%define %occt_array1_blittable(NAME, TYPE, CSTYPE)
%extend NCollection_Array1< TYPE > {
  void NetOcc_CopyTo(TYPE* theArray, int theLength) const {
    if (theLength != $self->Length()) throw Standard_OutOfRange("NCollection_Array1: the C# array has another length");
    std::copy($self->begin(), $self->end(), theArray);
  }
  void NetOcc_CopyFrom(const TYPE* theArray, int theLength) {
    if (theLength != $self->Length()) throw Standard_OutOfRange("NCollection_Array1: the C# array has another length");
    std::copy(theArray, theArray + theLength, $self->begin());
  }
  %proxycode %{
  public NAME(CSTYPE[] items) : this(1, items.Length) {
    NetOcc_CopyFrom(items, items.Length);
  }

  public CSTYPE[] ToArray() {
    var items = new CSTYPE[Length()];
    NetOcc_CopyTo(items, items.Length);
    return items;
  }
  %}
}
%occt_array1(NAME, %arg(TYPE), CSTYPE)
%enddef

template <class TheItemType> class NCollection_Array2 {
public:
  NCollection_Array2();
  NCollection_Array2(const int theRowLower, const int theRowUpper, const int theColLower, const int theColUpper);
  void Init(const TheItemType& theValue);
  int Length() const;
  int NbRows() const;
  int NbColumns() const;
  int LowerRow() const;
  int UpperRow() const;
  int LowerCol() const;
  int UpperCol() const;
  void Resize(int theRowLower, int theRowUpper, int theColLower, int theColUpper, bool theToCopyData);
  %extend {
    // OCCT checks the flat position only: a column past the end would reach into the next row
    const TheItemType& Value(const int theRow, const int theCol) const {
      if (theRow < $self->LowerRow() || theRow > $self->UpperRow() || theCol < $self->LowerCol() || theCol > $self->UpperCol()) {
        throw Standard_OutOfRange("NCollection_Array2::Value");
      }
      return $self->Value(theRow, theCol);
    }
    void SetValue(const int theRow, const int theCol, const TheItemType& theItem) {
      if (theRow < $self->LowerRow() || theRow > $self->UpperRow() || theCol < $self->LowerCol() || theCol > $self->UpperCol()) {
        throw Standard_OutOfRange("NCollection_Array2::SetValue");
      }
      $self->SetValue(theRow, theCol, theItem);
    }
  }
};

%define %occt_array2(NAME, TYPE, CSTYPE)
%typemap(cscode) NCollection_Array2< TYPE > %{
  public CSTYPE this[int row, int col] {
    get => Value(row, col);
    set => SetValue(row, col, value);
  }
%}
%occt_valueclass(%arg(NCollection_Array2< TYPE >))
%template(NAME) NCollection_Array2< TYPE >;
%enddef

template <class TheItemType> class NCollection_List {
public:
  NCollection_List();
  int Extent() const;
  bool IsEmpty() const;
  void Clear();
  const TheItemType& First() const;
  const TheItemType& Last() const;
  void Append(const TheItemType& theItem);
  void Prepend(const TheItemType& theItem);
  void RemoveFirst();
  void Reverse();
  %extend {
    %netocc_iterator(NCollection_List< TheItemType >::Iterator, TheItemType, Value)
  }
};

%define %occt_list(NAME, TYPE, CSTYPE)
%netocc_iterator_cscode(%arg(NCollection_List< TYPE >), CSTYPE, Extent())
%occt_valueclass(%arg(NCollection_List< TYPE >))
%template(NAME) NCollection_List< TYPE >;
%enddef

template <class TheItemType> class NCollection_Sequence {
public:
  NCollection_Sequence();
  int Length() const;
  bool IsEmpty() const;
  void Clear();
  void Reverse();
  void Exchange(const int I, const int J);
  const TheItemType& First() const;
  const TheItemType& Last() const;
  const TheItemType& Value(const int theIndex) const;
  void Append(const TheItemType& theItem);
  void Prepend(const TheItemType& theItem);
  void InsertBefore(const int theIndex, const TheItemType& theItem);
  void InsertAfter(const int theIndex, const TheItemType& theItem);
  void Remove(const int theIndex);
  void Remove(const int theFromIndex, const int theToIndex);
  %extend {
    void SetValue(const int theIndex, const TheItemType& theItem) { $self->ChangeValue(theIndex) = theItem; }
  }
};

// the enumerator walks forward, which the sequence serves in O(1) per step
%define %occt_sequence(NAME, TYPE, CSTYPE)
%netocc_indexed_cscode(%arg(NCollection_Sequence< TYPE >), CSTYPE, 1, Length())
%occt_valueclass(%arg(NCollection_Sequence< TYPE >))
%template(NAME) NCollection_Sequence< TYPE >;
%enddef

/*
 * The vectors: OCCT's accessors don't check the index. The C# members check it (from 0 to Count - 1, OcctException
 * Standard_OutOfRange as OCCT's would) and call OCCT's, which are internal: no %extend, whose calls SWIG casts with
 * "enum X" for a vector of enums, which a nested enum's flat alias can't follow.
 */
%define %netocc_vector_internals(TEMPLATE)
%csmethodmodifiers TEMPLATE::Value "internal";
%csmethodmodifiers TEMPLATE::SetValue "internal";
%csmethodmodifiers TEMPLATE::InsertBefore "internal";
%rename(NetOcc_EraseLast) TEMPLATE::EraseLast;
%csmethodmodifiers TEMPLATE::EraseLast "internal";
%enddef

%define %netocc_vector_cscode(CLASS, CSTYPE)
%typemap(csinterfaces) CLASS "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<CSTYPE>"
%typemap(cscode) CLASS %{
  public int Count => (int)Size();

  public CSTYPE this[int index] {
    get => Value(index);
    set => SetValue(index, value);
  }

  public CSTYPE Value(int index) => Value(Checked(index));

  public void SetValue(int index, CSTYPE item) => SetValue(Checked(index), item);

  public void InsertBefore(int index, CSTYPE item) {
    if (index == Count) {
      Append(item);
    } else {
      InsertBefore(Checked(index), item);
    }
  }

  public CSTYPE First() => Value(0);

  public CSTYPE Last() => Value(Count - 1);

  public void EraseLast() {
    Checked(Count - 1);
    NetOcc_EraseLast();
  }

  public global::System.Collections.Generic.IEnumerator<CSTYPE> GetEnumerator() {
    int n = Count;
    for (int i = 0; i < n; i++) {
      yield return Value(i);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

  private ulong Checked(int index) =>
    index >= 0 && index < Count ? (ulong)index : throw new global::OCC.Core.OcctException("Standard_OutOfRange", "the index is out of range");
%}
%enddef

%netocc_vector_internals(NCollection_LinearVector)
%csmethodmodifiers NCollection_LinearVector::Erase "internal";

template <class TheItemType> class NCollection_LinearVector {
public:
  NCollection_LinearVector();
  size_t Size() const;
  bool IsEmpty() const;
  size_t Capacity() const;
  void Reserve(const size_t theCapacity);
  void Clear(const bool theReleaseMemory = false);
  void Append(const TheItemType& theValue);
  const TheItemType& Value(const size_t theIndex) const;
  void SetValue(const size_t theIndex, const TheItemType& theValue);
  void InsertBefore(const size_t theIndex, const TheItemType& theValue);
  void Erase(const size_t theIndex);
  void EraseLast();
};

%define %occt_linearvector(NAME, TYPE, CSTYPE)
%netocc_vector_cscode(%arg(NCollection_LinearVector< TYPE >), CSTYPE)
%extend NCollection_LinearVector< TYPE > {
  %proxycode %{
  public void Erase(int index) => Erase(Checked(index));
  %}
}
%occt_valueclass(%arg(NCollection_LinearVector< TYPE >))
%template(NAME) NCollection_LinearVector< TYPE >;
%enddef

%define %occt_linearvector_blittable(NAME, TYPE, CSTYPE)
%extend NCollection_LinearVector< TYPE > {
  void NetOcc_CopyTo(TYPE* theArray, int theLength) const {
    if ((size_t)theLength != $self->Size()) throw Standard_OutOfRange("NCollection_LinearVector: the C# array has another length");
    std::copy($self->begin(), $self->end(), theArray);
  }
  void NetOcc_CopyFrom(const TYPE* theArray, int theLength) {
    $self->Clear();
    $self->Reserve((size_t)theLength);
    for (int i = 0; i < theLength; i++) $self->Append(theArray[i]);
  }
  %proxycode %{
  public NAME(CSTYPE[] items) : this() {
    NetOcc_CopyFrom(items, items.Length);
  }

  public CSTYPE[] ToArray() {
    var items = new CSTYPE[Count];
    NetOcc_CopyTo(items, items.Length);
    return items;
  }
  %}
}
%occt_linearvector(NAME, %arg(TYPE), CSTYPE)
%enddef

%netocc_vector_internals(NCollection_DynamicArray)

template <class TheItemType> class NCollection_DynamicArray {
public:
  NCollection_DynamicArray();
  size_t Size() const;
  int Length() const;
  bool IsEmpty() const;
  void Clear(const bool theReleaseMemory = false);
  void Append(const TheItemType& theValue);
  const TheItemType& Value(const size_t theIndex) const;
  void SetValue(const size_t theIndex, const TheItemType& theValue);
  void InsertBefore(const size_t theIndex, const TheItemType& theValue);
  void EraseLast();
};

%define %occt_dynamicarray(NAME, TYPE, CSTYPE)
%netocc_vector_cscode(%arg(NCollection_DynamicArray< TYPE >), CSTYPE)
%occt_valueclass(%arg(NCollection_DynamicArray< TYPE >))
%template(NAME) NCollection_DynamicArray< TYPE >;
%enddef

// the handle-managed variants, declared with the collection base only (see %occt_transient_instance)
template <class TheItemType> class NCollection_HArray1 : public NCollection_Array1< TheItemType > {
public:
  NCollection_HArray1();
  NCollection_HArray1(const int theLower, const int theUpper);
  NCollection_HArray1(const int theLower, const int theUpper, const TheItemType& theValue);
  NCollection_HArray1(const NCollection_Array1< TheItemType >& theOther);
};

template <class TheItemType> class NCollection_HArray2 : public NCollection_Array2< TheItemType > {
public:
  NCollection_HArray2(const int theRowLower, const int theRowUpper, const int theColLower, const int theColUpper);
  NCollection_HArray2(const int theRowLower, const int theRowUpper, const int theColLower, const int theColUpper, const TheItemType& theValue);
  NCollection_HArray2(const NCollection_Array2< TheItemType >& theOther);
};

template <class TheItemType> class NCollection_HSequence : public NCollection_Sequence< TheItemType > {
public:
  NCollection_HSequence();
  NCollection_HSequence(const NCollection_Sequence< TheItemType >& theOther);
};

%define %occt_harray1(NAME, TYPE, CSTYPE)
%occt_transient_instance(%arg(NCollection_HArray1< TYPE >), NAME)
%template(NAME) NCollection_HArray1< TYPE >;
%enddef

%define %occt_harray2(NAME, TYPE, CSTYPE)
%occt_transient_instance(%arg(NCollection_HArray2< TYPE >), NAME)
%template(NAME) NCollection_HArray2< TYPE >;
%enddef

%define %occt_hsequence(NAME, TYPE, CSTYPE)
%occt_transient_instance(%arg(NCollection_HSequence< TYPE >), NAME)
%template(NAME) NCollection_HSequence< TYPE >;
%enddef

// maps: the hasher is an argument (by default NCollection_DefaultHasher<Key>)
template <class TheKeyType> struct NCollection_DefaultHasher;

template <class TheKeyType, class Hasher> class NCollection_Map {
public:
  NCollection_Map();
  int Extent() const;
  bool IsEmpty() const;
  void Clear();
  bool Add(const TheKeyType& theKey);
  bool Contains(const TheKeyType& theKey) const;
  bool Remove(const TheKeyType& theKey);
  %extend {
    %netocc_iterator(%arg(NCollection_Map< TheKeyType, Hasher >::Iterator), TheKeyType, Key)
  }
};

%define %occt_map(NAME, KEY, HASHER, CSKEY)
%netocc_iterator_cscode(%arg(NCollection_Map< KEY, HASHER >), CSKEY, Extent())
%occt_valueclass(%arg(NCollection_Map< KEY, HASHER >))
%template(NAME) NCollection_Map< KEY, HASHER >;
%enddef

template <class TheKeyType, class Hasher> class NCollection_FlatMap {
public:
  NCollection_FlatMap();
  size_t Size() const;
  bool IsEmpty() const;
  void Clear(bool doReleaseMemory = false);
  bool Add(const TheKeyType& theKey);
  bool Contains(const TheKeyType& theKey) const;
  bool Remove(const TheKeyType& theKey);
  %extend {
    %netocc_iterator(%arg(NCollection_FlatMap< TheKeyType, Hasher >::Iterator), TheKeyType, Key)
  }
};

%define %occt_flatmap(NAME, KEY, HASHER, CSKEY)
%netocc_iterator_cscode(%arg(NCollection_FlatMap< KEY, HASHER >), CSKEY, (int)Size())
%occt_valueclass(%arg(NCollection_FlatMap< KEY, HASHER >))
%template(NAME) NCollection_FlatMap< KEY, HASHER >;
%enddef

template <class TheKeyType, class Hasher> class NCollection_IndexedMap {
public:
  NCollection_IndexedMap();
  int Extent() const;
  bool IsEmpty() const;
  void Clear();
  int Add(const TheKeyType& theKey1);
  bool Contains(const TheKeyType& theKey1) const;
  int FindIndex(const TheKeyType& theKey1) const;
  const TheKeyType& FindKey(const int theIndex) const;
  void RemoveLast();
  void RemoveFromIndex(const int theIndex);
  bool RemoveKey(const TheKeyType& theKey1);
  void Swap(const int theIndex1, const int theIndex2);
};

%define %occt_indexedmap(NAME, KEY, HASHER, CSKEY)
%typemap(csinterfaces) NCollection_IndexedMap< KEY, HASHER > "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<CSKEY>"
%typemap(cscode) NCollection_IndexedMap< KEY, HASHER > %{
  public int Count => Extent();

  public CSKEY this[int index] => FindKey(index);

  public global::System.Collections.Generic.IEnumerator<CSKEY> GetEnumerator() {
    int n = Extent();
    for (int i = 1; i <= n; i++) {
      yield return FindKey(i);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%occt_valueclass(%arg(NCollection_IndexedMap< KEY, HASHER >))
%template(NAME) NCollection_IndexedMap< KEY, HASHER >;
%enddef

template <class TheKeyType, class TheItemType, class Hasher> class NCollection_DataMap {
public:
  NCollection_DataMap();
  int Extent() const;
  bool IsEmpty() const;
  void Clear();
  bool Bind(const TheKeyType& theKey, const TheItemType& theItem);
  bool IsBound(const TheKeyType& theKey) const;
  bool UnBind(const TheKeyType& theKey);
  const TheItemType& Find(const TheKeyType& theKey) const;
  %extend {
    %netocc_pair_iterator(%arg(NCollection_DataMap< TheKeyType, TheItemType, Hasher >::Iterator), TheKeyType, TheItemType)
  }
};

%define %occt_datamap(NAME, KEY, ITEM, HASHER, CSKEY, CSITEM)
%netocc_datamap_cscode(%arg(NCollection_DataMap< KEY, ITEM, HASHER >), CSKEY, CSITEM)
%occt_valueclass(%arg(NCollection_DataMap< KEY, ITEM, HASHER >))
%template(NAME) NCollection_DataMap< KEY, ITEM, HASHER >;
%enddef

template <class TheKeyType, class TheItemType, class Hasher> class NCollection_FlatDataMap {
public:
  NCollection_FlatDataMap();
  int Extent() const;
  bool IsEmpty() const;
  void Clear(bool doReleaseMemory = false);
  bool Bind(const TheKeyType& theKey, const TheItemType& theItem);
  bool IsBound(const TheKeyType& theKey) const;
  bool UnBind(const TheKeyType& theKey);
  const TheItemType& Find(const TheKeyType& theKey) const;
  %extend {
    %netocc_pair_iterator(%arg(NCollection_FlatDataMap< TheKeyType, TheItemType, Hasher >::Iterator), TheKeyType, TheItemType)
  }
};

%define %occt_flatdatamap(NAME, KEY, ITEM, HASHER, CSKEY, CSITEM)
%netocc_datamap_cscode(%arg(NCollection_FlatDataMap< KEY, ITEM, HASHER >), CSKEY, CSITEM)
%occt_valueclass(%arg(NCollection_FlatDataMap< KEY, ITEM, HASHER >))
%template(NAME) NCollection_FlatDataMap< KEY, ITEM, HASHER >;
%enddef

template <class TheKeyType, class TheItemType, class Hasher> class NCollection_IndexedDataMap {
public:
  NCollection_IndexedDataMap();
  int Extent() const;
  bool IsEmpty() const;
  void Clear();
  int Add(const TheKeyType& theKey1, const TheItemType& theItem);
  bool Contains(const TheKeyType& theKey1) const;
  int FindIndex(const TheKeyType& theKey1) const;
  const TheKeyType& FindKey(const int theIndex) const;
  const TheItemType& FindFromIndex(const int theIndex) const;
  const TheItemType& FindFromKey(const TheKeyType& theKey1) const;
  void RemoveLast();
  void RemoveFromIndex(const int theIndex);
  void Swap(const int theIndex1, const int theIndex2);
  %extend {
    void SetFromIndex(const int theIndex, const TheItemType& theItem) { $self->ChangeFromIndex(theIndex) = theItem; }
  }
};

%define %occt_indexeddatamap(NAME, KEY, ITEM, HASHER, CSKEY, CSITEM)
%typemap(csinterfaces) NCollection_IndexedDataMap< KEY, ITEM, HASHER > "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<CSKEY, CSITEM>>"
%typemap(cscode) NCollection_IndexedDataMap< KEY, ITEM, HASHER > %{
  public int Count => Extent();

  public CSITEM this[int index] {
    get => FindFromIndex(index);
    set => SetFromIndex(index, value);
  }

  public global::System.Collections.Generic.IEnumerator<global::System.Collections.Generic.KeyValuePair<CSKEY, CSITEM>> GetEnumerator() {
    int n = Extent();
    for (int i = 1; i <= n; i++) {
      yield return new global::System.Collections.Generic.KeyValuePair<CSKEY, CSITEM>(FindKey(i), FindFromIndex(i));
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%occt_valueclass(%arg(NCollection_IndexedDataMap< KEY, ITEM, HASHER >))
%template(NAME) NCollection_IndexedDataMap< KEY, ITEM, HASHER >;
%enddef
