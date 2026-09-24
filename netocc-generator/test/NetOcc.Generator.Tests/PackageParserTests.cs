// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Emit;
using NetOcc.Generator.Model;
using NetOcc.Generator.Parsing;

namespace NetOcc.Generator.Tests;

/// <summary>
/// libclang on a small self-contained header: what the parser keeps and how it spells types.
/// </summary>
[TestFixture]
public class PackageParserTests
{
  private const string Header = """
    #pragma once
    typedef double Demo_Real;
    typedef unsigned long long size_t;

    enum Demo_Kind { Demo_First = 1, Demo_Second = 4 };

    class Demo_Base { public: virtual ~Demo_Base() {} };
    class Demo_Middle : public Demo_Base {};

    class Demo_Thing : public Demo_Middle
    {
    public:
      Demo_Thing();
      Demo_Thing(int theCount, bool theFlag = true);
      Demo_Real Size() const;
      size_t Count() const;
      static int Instances();
      void Scale(double theFactor = 1.5);
    protected:
      void Hidden();
    private:
      int myCount;
    };
    """;

  private TempDirectory _directory = null!;
  private PackageModel _model = null!;

  [OneTimeSetUp]
  public void Parse()
  {
    _directory = new TempDirectory("netocc-gen-parse");
    _directory.Write("Demo_Thing.hxx", Header);
    _model = new PackageParser([_directory.FullName], []).Parse("Demo", ["Demo_Thing.hxx"]);
  }

  [OneTimeTearDown]
  public void DeleteDirectory() => _directory.Dispose();

  // the export tables are MSVC's: with them, the parser mangles names for its target
  private PackageModel ParseOne(string header, string file, LibraryExports? exports = null,
                                IReadOnlyCollection<string>? classes = null)
  {
    _directory.Write(file, header);
    string[] arguments = exports is null ? [] : ["--target=x86_64-pc-windows-msvc"];
    return new PackageParser([_directory.FullName], arguments, exports).Parse(
      "Demo", [file], classes: classes);
  }

  [Test]
  public void Parse_ReadsEnumValues()
  {
    // Arrange
    var expected = new[] { new EnumConstant("Demo_First", 1), new EnumConstant("Demo_Second", 4) };

    // Act
    var kind = _model.Enums.Single(e => e.Name == "Demo_Kind");

    // Assert
    Assert.That(kind.Constants, Is.EqualTo(expected));
  }

  [Test]
  public void Parse_KeepsPublicMembersOnly()
  {
    // Arrange
    var thing = _model.Classes.Single(c => c.Name == "Demo_Thing");

    // Act
    var names = thing.Methods.Select(m => m.Name).ToList();

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(names, Is.EqualTo(new[] { "Size", "Count", "Instances", "Scale" }));
      Assert.That(thing.Constructors, Has.Count.EqualTo(2),
                  "user-declared constructors, no implicit copy or move");
    }
  }

  [Test]
  public void Parse_KeepsDefaultArgumentsAsWritten()
  {
    // Arrange
    var thing = _model.Classes.Single(c => c.Name == "Demo_Thing");

    // Act
    var defaults = thing.Constructors[1]
      .Parameters.Select(p => p.Default)
      .Append(thing.Methods.Single(m => m.Name == "Scale").Parameters[0].Default);

    // Assert
    Assert.That(defaults, Is.EqualTo(new string?[] { null, "true", "1.5" }));
  }

  [Test]
  public void Parse_ResolvesTypedefsButKeepsSizeT()
  {
    // Arrange
    var thing = _model.Classes.Single(c => c.Name == "Demo_Thing");

    // Act
    var size = thing.Methods.Single(m => m.Name == "Size").Return;
    var count = thing.Methods.Single(m => m.Name == "Count").Return;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(size, Is.EqualTo(new BuiltinType("double")));
      Assert.That(count, Is.EqualTo(new BuiltinType("size_t")));
    }
  }

  [Test]
  public void Parse_TakesMembersFromUsingDeclarations()
  {
    // Arrange
    const string header = """
      #pragma once
      class Demo_Options
      {
      public:
        bool HasErrors() const { return false; }
        void SetFuzzy(double theFuzz) { myFuzz = theFuzz; }
        void Unlisted() {}
      private:
        double myFuzz = 0.0;
      };

      class Demo_Algo : protected Demo_Options
      {
      public:
        using Demo_Options::HasErrors;
        using Demo_Options::SetFuzzy;
      protected:
        using Demo_Options::Unlisted;
      };
      """;

    // Act
    var algo = ParseOne(header, "Demo_Algo.hxx").Classes.Single(c => c.Name == "Demo_Algo");

    // Assert
    Assert.That(algo.Methods.Select(m => m.Name), Is.EqualTo(new[] { "HasErrors", "SetFuzzy" }));
  }

  [Test]
  public void Parse_TakesTheValueOfADefaultAMacroWrites()
  {
    // Arrange (a macro body's tokens aren't where the default is: its constant's value, where a
    // literal of it fits)
    const string header = """
      #pragma once
      #define DEMO_LEVEL (2 * 3)
      #define DEMO_ANGLE (3.0 / 2.0)
      #define DEMO_SETTER(N) void N(int theLevel = DEMO_LEVEL, const char* theName = "", Demo_Mode theMode = Demo_Fast) {}
      enum Demo_Mode { Demo_Fast, Demo_Slow };
      class Demo_Settings
      {
      public:
        void Set(int theLevel = DEMO_LEVEL, double theAngle = DEMO_ANGLE, int theCount = 4) {}
        DEMO_SETTER(Apply)
      };
      """;

    // Act
    var settings = ParseOne(header, "Demo_Settings.hxx")
      .Classes.Single(c => c.Name == "Demo_Settings");
    var set = settings.Methods.Single(m => m.Name == "Set").Parameters.Select(p => p.Default);
    var apply = settings.Methods.Single(m => m.Name == "Apply").Parameters.Select(p => p.Default);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(set, Is.EqualTo(new[] { "6", "1.5", "4" }));
      Assert.That(apply, Is.EqualTo(new[] { "6", "\"\"", "" }), "a number doesn't fit an enum");
    }
  }

  [Test]
  public void Parse_ValueInitializedDefault_IsTheTypesConstruction()
  {
    // Arrange (SWIG can't parse "= {}"; the shims compile the default too)
    const string header = """
      #pragma once
      struct Demo_Options { int Level = 0; };
      class Demo_Reader
      {
      public:
        void Set(const Demo_Options& theOptions = {}, int* thePointer = {}) {}
      };
      """;

    // Act
    var reader = ParseOne(header, "Demo_Reader.hxx").Classes.Single(c => c.Name == "Demo_Reader");
    var defaults = reader.Methods.Single(m => m.Name == "Set").Parameters.Select(p => p.Default);

    // Assert
    Assert.That(defaults, Is.EqualTo(new[] { "Demo_Options()", "nullptr" }));
  }

  [Test]
  public void Parse_KeepsBitFieldsOutOfPlainData()
  {
    // Arrange (six bytes packed into two ints: no C# field mirrors a bit field)
    const string header = """
      #pragma once
      struct Demo_Colors { unsigned int r1 : 8; unsigned int g1 : 8; unsigned int b1 : 8; unsigned int r2 : 8; unsigned int g2 : 8; unsigned int b2 : 8; };
      """;

    // Act
    var colors = ParseOne(header, "Demo_Colors.hxx").Classes.Single(c => c.Name == "Demo_Colors");

    // Assert
    Assert.That(colors.IsPlainData, Is.False);
  }

  [Test]
  public void Parse_TakesOnlyTheListedClasses()
  {
    // Arrange (classes: [Demo_Driver]; Demo_Renderer adds nothing, not the instance only it uses
    // either)
    const string header = """
      #pragma once
      template <class T> class Demo_Tree { public: T Value; };
      class Demo_Driver { public: int Count() const; };
      class Demo_Renderer { public: Demo_Tree<float>* Tree() const; };
      """;

    // Act
    var model = ParseOne(header, "Demo_Listed.hxx", classes: ["Demo_Driver"]);

    // Assert
    Assert.That(model.Classes.Select(c => c.Name), Is.EqualTo(new[] { "Demo_Driver" }));
  }

  [Test]
  public void Parse_KeepsWindowHandlesByName()
  {
    // Arrange (a typedef whose target differs per platform, as Aspect_Drawable's does)
    const string header = """
      #pragma once
      typedef void* Aspect_Drawable;
      class Demo_Window
      {
      public:
        Aspect_Drawable NativeHandle() const;
      };
      """;

    // Act
    var window = ParseOne(header, "Demo_Window.hxx").Classes.Single(c => c.Name == "Demo_Window");
    var returned = window.Methods.Single().Return;

    // Assert
    Assert.That(returned, Is.EqualTo(new BuiltinType("intptr_t", false, "Aspect_Drawable")));
  }

  [Test]
  public void Parse_NamesHiddenConstructorsEmpty()
  {
    // Arrange (clang names a nested class's constructors Inner, its flat name is Demo_Outer_Inner,
    // and an alias may rename a class later: the writers look constructors up by the empty name)
    const string header = """
      #pragma once
      class Demo_Outer
      {
      public:
        class Inner
        {
        public:
          Inner(int theLevel = 0);
        private:
          Inner(double theScale);
        };
      };
      """;

    // Act
    var inner = ParseOne(header, "Demo_Outer.hxx")
      .Classes.Single(c => c.Name == "Demo_Outer_Inner");

    // Assert
    Assert.That(MemberRules.HiddenConstructors(inner).Select(p => p.Single().Name),
                Is.EqualTo(new[] { "theScale" }));
  }

  [Test]
  public void Parse_KeepsTheUnderlyingTypeADeclarationFixes()
  {
    // Arrange (without a written type the ABI picks one: MSVC int, the Itanium ABI unsigned int for
    // these values)
    const string header = """
      #pragma once
      enum Demo_Flags { Demo_Flags_A = 1, Demo_Flags_B = 2 };
      enum Demo_Small : unsigned char { Demo_Small_X = 1 };
      enum class Demo_Scoped { A, B };
      """;

    // Act
    var enums = ParseOne(header, "Demo_Flags.hxx")
      .Enums.ToDictionary(e => e.Name, e => e.Underlying);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(enums["Demo_Flags"], Is.Null, "int");
      Assert.That(enums["Demo_Small"], Is.EqualTo("unsigned char"));
      Assert.That(enums["Demo_Scoped"], Is.Null, "an enum class is on int");
    }
  }

  [Test]
  public void Parse_HoldsTheReferencesOfEveryBase()
  {
    // Arrange (a protected base and a template instance base hold the view and the graph; a
    // collection's pointer is its storage, a handle is counted)
    const string header = """
      #pragma once
      namespace opencascade { template <class T> class handle { T* myEntity; }; }
      template <class T> class NCollection_Array1 { T* myData; };
      class Demo_Graph {};
      class Demo_View {};
      class Demo_Mesh {};
      template <class T> class Demo_Walker { protected: const T* myGraph; };
      class Demo_Base { protected: Demo_View& myView; Demo_Base(Demo_View& theView) : myView(theView) {} };
      class Demo_Iterator : protected Demo_Base, public Demo_Walker<Demo_Graph>, public NCollection_Array1<Demo_View>
      {
      public:
        Demo_Iterator(Demo_View& theView) : Demo_Base(theView) {}
      private:
        opencascade::handle<Demo_Mesh> myMesh;
        int* myCounts;
      };
      """;

    // Act
    var iterator = ParseOne(header, "Demo_Iterator.hxx")
      .Classes.Single(c => c.Name == "Demo_Iterator");

    // Assert
    Assert.That(iterator.Held?.Select(t => t.Spelling),
                Is.EquivalentTo(new[] { "int*", "Demo_View&", "const Demo_Graph*" }));
  }

  [Test]
  public void Parse_ChecksOutOfLineMembersAgainstTheExportTables()
  {
    // Arrange
    const string header = """
      #pragma once
      class Demo_Exported
      {
      public:
        __declspec(dllexport) void Present();
        __declspec(dllexport) void Missing();
        void Inline() {}
        virtual void Pure() = 0;
      };
      """;
    var exports = new LibraryExports(["?Present@Demo_Exported@@QEAAXXZ"], libraries: 1);

    // Act
    var model = ParseOne(header, "Demo_Exported.hxx", exports);
    var callable = model.Classes.Single().Methods.ToDictionary(m => m.Name, m => m.IsCallable);

    // Assert
    Assert.That(callable, Is.EqualTo(new Dictionary<string, bool>
    {
      ["Present"] = true,
      ["Missing"] = false,
      ["Inline"] = true,
      ["Pure"] = true,
    }), "declared exported is not enough: OCCT has members it never defines");
  }

  [Test]
  public void Parse_LooksUpDestructorsByTheirExportedSymbol()
  {
    // Arrange
    const string header = """
      #pragma once
      class Demo_Deletable
      {
      public:
        __declspec(dllexport) ~Demo_Deletable();
      };

      class Demo_Undeletable
      {
      public:
        __declspec(dllexport) ~Demo_Undeletable();
      };
      """;
    var exports = new LibraryExports(["??1Demo_Deletable@@QEAA@XZ"], libraries: 1);

    // Act
    var model = ParseOne(header, "Demo_Deletable.hxx", exports);
    var deletable = model.Classes.ToDictionary(c => c.Name, c => c.Traits.HasPublicDestructor);

    // Assert
    Assert.That(deletable, Is.EqualTo(new Dictionary<string, bool>
    {
      ["Demo_Deletable"] = true,
      ["Demo_Undeletable"] = false,
    }), "libclang's getMangling names the ??_D destructor; the DLLs export ??1");
  }

  [Test]
  public void Parse_ChecksTheVtableOfAClassTheLibrariesDontExport()
  {
    // Arrange (Demo_Selector's inline constructor, and its implicit copy, emit the vtable in the
    // shim, which names Reject: declared, not exported)
    const string header = """
      #pragma once
      class Demo_Base
      {
      public:
        virtual ~Demo_Base() {}
        virtual bool Reject(int theValue) const = 0;
      };

      class Demo_Selector : public Demo_Base
      {
      public:
        Demo_Selector() {}
        bool Reject(int theValue) const override;
      };

      class Demo_InlineSelector : public Demo_Base
      {
      public:
        Demo_InlineSelector() {}
        bool Reject(int theValue) const override { return theValue < 0; }
      };
      """;
    var exports = new LibraryExports([], libraries: 1);

    // Act
    var model = ParseOne(header, "Demo_Selector.hxx", exports);
    var constructors = model.Classes.Where(c => c.Constructors.Count > 0)
      .ToDictionary(c => c.Name,
                    c => (c.Constructors[0].IsCallable, c.Constructors[0].Unlinked,
                      c.Traits.IsCopyable));

    // Assert
    Assert.That(constructors, Is.EqualTo(new Dictionary<string, (bool, string?, bool)>
    {
      ["Demo_Selector"] = (false, "Demo_Selector::Reject", false),
      ["Demo_InlineSelector"] = (true, null, true),
    }), "a pure function isn't called through the vtable, an inline one links");
  }

  [Test]
  public void Parse_GivesAClassWithoutConstructorsItsImplicitDefaultOne()
  {
    // Arrange (like BRep_Builder; Demo_Holder's member has no default constructor, so neither has
    // Demo_Holder)
    const string header = """
      #pragma once
      class Demo_Builder
      {
      public:
        void MakeVertex() const {}
      };

      class Demo_Part
      {
      public:
        Demo_Part(int theId) : myId(theId) {}

      private:
        int myId;
      };

      class Demo_Holder
      {
      private:
        Demo_Part myPart;
      };
      """;

    // Act
    var model = ParseOne(header, "Demo_Builder.hxx");
    var constructors = model.Classes.ToDictionary(c => c.Name,
                                                  c => c.Constructors
                                                    .Select(k => k.Parameters.Count)
                                                    .ToArray());

    // Assert
    Assert.That(constructors, Is.EqualTo(new Dictionary<string, int[]>
    {
      ["Demo_Builder"] = [0],
      ["Demo_Part"] = [1],
      ["Demo_Holder"] = [],
    }));
  }

  [Test]
  public void Parse_ChecksWhatInlineDefinitionsCall()
  {
    // Arrange
    const string header = """
      #pragma once
      template <class T> class Demo_Set
      {
      public:
        virtual T Size() const { return T(); }
        T Count() const { return T(); }
        T Width() const { return T(); }
      };

      class Demo_Inline : public Demo_Set<double>
      {
      public:
        using Demo_Set<double>::Width;
        __declspec(dllexport) void Exported();
        __declspec(dllexport) void Internal();
        void CallsExported() { Exported(); }
        void CallsInternal() { Internal(); }
        double CallsTemplate() const { return Demo_Set<double>::Count(); }
        double Size() const override { return Demo_Set<double>::Size(); }
      };
      """;
    var exports = new LibraryExports(["?Exported@Demo_Inline@@QEAAXXZ"], libraries: 1);

    // Act
    var model = ParseOne(header, "Demo_Inline.hxx", exports);
    var unlinked = model.Classes.Single(c => c.Name == "Demo_Inline")
      .Methods.ToDictionary(m => m.Name, m => m.Unlinked);

    // Assert
    Assert.That(unlinked, Is.EqualTo(new Dictionary<string, string?>
                {
                  ["Width"] = null,
                  ["Exported"] = null,
                  ["Internal"] = "Demo_Inline::Internal",
                  ["CallsExported"] = null,
                  ["CallsInternal"] = "Demo_Inline::Internal",
                  ["CallsTemplate"] = null,
                  ["Size"] = null,
                }),
                "an inline definition links when its calls do; template members are instantiated in the shim");
  }

  [Test]
  public void Parse_TakesPlainDataOfOneSizeOnEveryTarget()
  {
    // Arrange
    const string header = """
      #pragma once
      typedef unsigned long long size_t;
      namespace std { template <class T, size_t N> struct array { T _Elems[N]; }; }
      struct Demo_Fixed { int Count; std::array<double, 2> Roots; };
      struct Demo_Sized { size_t Count; double Value; };
      """;

    // Act
    var model = ParseOne(header, "Demo_Plain.hxx");
    var fixedSize = model.Classes.Single(c => c.Name == "Demo_Fixed");
    var sized = model.Classes.Single(c => c.Name == "Demo_Sized");

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(fixedSize.IsPlainData, Is.True);
      Assert.That(fixedSize.Fields![1].Type,
                  Is.EqualTo(new ArrayType(new BuiltinType("double"), 2)),
                  "a std::array is the C array it holds");
      Assert.That(sized.IsPlainData, Is.False, "a size_t has 4 bytes on x86");
    }
  }

  [Test]
  public void Parse_ListsAncestorsNearestFirst()
  {
    // Arrange
    var thing = _model.Classes.Single(c => c.Name == "Demo_Thing");

    // Act
    var ancestors = thing.Ancestors;

    // Assert
    Assert.That(ancestors, Is.EqualTo(new[] { "Demo_Middle", "Demo_Base" }));
  }

  [Test]
  public void Parse_WithAFatalErrorInASystemHeader_Fails()
  {
    // Arrange (not a header to leave out: every header of the package would go)
    using var system = new TempDirectory("netocc-gen-system");
    system.Write("demo_system.h", "#pragma once\n#include <demo_builtin.h>\n");
    _directory.Write("Demo_System.hxx", "#pragma once\n#include <demo_system.h>\n");
    var parser = new PackageParser([_directory.FullName], ["-isystem", system.FullName]);

    // Act
    Action parse = () => parser.Parse("Demo", ["Demo_System.hxx"]);

    // Assert
    Assert.That(
      parse,
      Throws.InvalidOperationException.With.Message.Contains(
        "demo_system.h: 'demo_builtin.h' file not found (a system header"));
  }
}
