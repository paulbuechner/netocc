// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

/*
 * math companion: math_Vector and math_IntegerVector (math_VectorBase<double>, <int>), which netocc-gen
 * instantiates with %occt_math_vector(NAME, TYPE, CSTYPE) like a collection. Here, not in Collections.i,
 * because math_VectorBase.hxx includes math_Matrix.hxx. Modules that use the vectors import math.i.
 *
 * C#: indexer from Lower() to Upper(), IEnumerable<CSTYPE>, new NAME(CSTYPE[]) (Lower() = 1), ToArray(),
 * and the operators + - * / (vector * vector is the dot product).
 */

%{
#include <math_VectorBase.hxx>
%}

template <class TheItemType> class math_VectorBase {
public:
  math_VectorBase(const int theLower, const int theUpper);
  math_VectorBase(const int theLower, const int theUpper, const TheItemType theInitialValue);
  void Init(const TheItemType theInitialValue);
  int Length() const;
  int Lower() const;
  int Upper() const;
  double Norm() const;
  double Norm2() const;
  int Max() const;
  int Min() const;
  void Normalize();
  math_VectorBase< TheItemType > Normalized() const;
  void Invert();
  math_VectorBase< TheItemType > Inverse() const;
  math_VectorBase< TheItemType > Slice(const int theI1, const int theI2) const;
  void Multiply(const TheItemType theRight);
  math_VectorBase< TheItemType > Multiplied(const TheItemType theRight) const;
  TheItemType Multiplied(const math_VectorBase< TheItemType >& theRight) const;
  void Divide(const TheItemType theRight);
  math_VectorBase< TheItemType > Divided(const TheItemType theRight) const;
  void Add(const math_VectorBase< TheItemType >& theRight);
  math_VectorBase< TheItemType > Added(const math_VectorBase< TheItemType >& theRight) const;
  void Subtract(const math_VectorBase< TheItemType >& theRight);
  math_VectorBase< TheItemType > Subtracted(const math_VectorBase< TheItemType >& theRight) const;
  math_VectorBase< TheItemType > Opposite();
  const TheItemType& Value(const int theNum) const;
  %extend {
    void SetValue(const int theNum, const TheItemType theItem) { $self->Value(theNum) = theItem; }
    void NetOcc_CopyTo(TheItemType* theArray, int theLength) const {
      if (theLength != $self->Length()) throw Standard_OutOfRange("math_VectorBase: the C# array has another length");
      for (int i = 0; i < theLength; ++i) theArray[i] = $self->Value($self->Lower() + i);
    }
    void NetOcc_CopyFrom(const TheItemType* theArray, int theLength) {
      if (theLength != $self->Length()) throw Standard_OutOfRange("math_VectorBase: the C# array has another length");
      for (int i = 0; i < theLength; ++i) $self->Value($self->Lower() + i) = theArray[i];
    }
  }
};

%define %occt_math_vector(NAME, TYPE, CSTYPE)
// no default constructor: by-value results go through SWIG's value wrapper
%feature("valuewrapper") math_VectorBase< TYPE >;
%typemap(csinterfaces) math_VectorBase< TYPE > "global::System.IDisposable, global::System.Collections.Generic.IEnumerable<CSTYPE>"
%typemap(cscode) math_VectorBase< TYPE > %{
  public int Count => Length();

  public CSTYPE this[int index] {
    get => Value(index);
    set => SetValue(index, value);
  }

  public NAME(CSTYPE[] items) : this(1, items.Length) {
    NetOcc_CopyFrom(items, items.Length);
  }

  public CSTYPE[] ToArray() {
    var items = new CSTYPE[Length()];
    NetOcc_CopyTo(items, items.Length);
    return items;
  }

  public static NAME operator +(NAME left, NAME right) => left.Added(right);

  public static NAME operator -(NAME left, NAME right) => left.Subtracted(right);

  public static NAME operator -(NAME vector) => vector.Opposite();

  public static NAME operator *(NAME vector, CSTYPE scalar) => vector.Multiplied(scalar);

  public static NAME operator *(CSTYPE scalar, NAME vector) => vector.Multiplied(scalar);

  public static CSTYPE operator *(NAME left, NAME right) => left.Multiplied(right);

  public static NAME operator /(NAME vector, CSTYPE scalar) => vector.Divided(scalar);

  public global::System.Collections.Generic.IEnumerator<CSTYPE> GetEnumerator() {
    int upper = Upper();
    for (int i = Lower(); i <= upper; i++) {
      yield return Value(i);
    }
  }

  global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
%}
%occt_valueclass(math_VectorBase< TYPE >)
%template(NAME) math_VectorBase< TYPE >;
%enddef
