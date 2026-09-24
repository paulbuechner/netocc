// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;

// NUnit
using NUnit.Framework;

//
using NetOcc.Generator.Parsing;

namespace NetOcc.Generator.Tests;

/// <summary>
/// netocc-gen docs: Doxygen comments as XML documentation, matched to C# members. Our own text:
/// OCCT's stays out.
/// </summary>
[TestFixture]
public class DocsTests
{
  private const string Header = """
    #pragma once
    //! A demo curve.
    //!
    //! Its second paragraph.
    class Demo_Curve
    {
    public:
      //! Makes a unit curve.
      //! @code
      //!   Demo_Curve aCurve;
      //! @endcode
      Demo_Curve();

      //! Evaluates the curve.
      //! @param theU the parameter
      //! @return the value at @c theU
      //! @note Slow & exact.
      double Value(double theU) const;

      double Undocumented() const;
    };

    //! Kinds of curves.
    enum Demo_Kind
    {
      Demo_Kind_Line, //!< a straight line
      Demo_Kind_Circle
    };
    """;

  private Dictionary<string, TypeDocs> _docs = null!;

  [OneTimeSetUp]
  public void Parse()
  {
    using var directory = new TempDirectory("netocc-gen-docs");
    directory.Write("Demo_Curve.hxx", Header);
    _docs = new PackageParser([directory.FullName], []).ParseDocs(
      "Demo", ["Demo_Curve.hxx"], [], []);
  }

  [Test]
  public void ParseDocs_TakesTheFirstParagraphAsTheSummary()
  {
    // Act
    var xml = _docs["Demo_Curve"].Xml;

    // Assert
    Assert.That(xml, Is.EqualTo(
                  "<summary>A demo curve.</summary><remarks><para>Its second paragraph.</para></remarks>"));
  }

  [Test]
  public void ParseDocs_ConvertsParametersReturnsAndNotes()
  {
    // Act
    var value = _docs["Demo_Curve"].Members.Find(m => m.Name == "Value");

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(value?.Parameters, Is.EqualTo(new[] { "theU" }));
      Assert.That(value?.Xml,
                  Is.EqualTo(
                    "<summary>Evaluates the curve.</summary><param name=\"theU\">the parameter</param>"
                    + "<returns>the value at <c>theU</c></returns><remarks><para>Note: Slow &amp; exact.</para></remarks>"));
    }
  }

  [Test]
  public void ParseDocs_KeepsCodeBlocksAndLeavesOutUndocumentedMembers()
  {
    // Act
    var members = _docs["Demo_Curve"].Members;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(members.Find(m => m.Name == "#ctor")?.Xml,
                  Is.EqualTo(
                    "<summary>Makes a unit curve.</summary><remarks><para><code>Demo_Curve aCurve;</code></para></remarks>"));
      Assert.That(members.Exists(m => m.Name == "Undocumented"), Is.False);
    }
  }

  [Test]
  public void ParseDocs_DocumentsEnumsAndTheirConstants()
  {
    // Act
    var kind = _docs["Demo_Kind"];

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(kind.Xml, Is.EqualTo("<summary>Kinds of curves.</summary>"));
      Assert.That(kind.Members.Find(m => m.Name == "Demo_Kind_Line")?.Xml,
                  Is.EqualTo("<summary>a straight line</summary>"));
    }
  }

  [Test]
  public void ParseDocs_LeavesOutAHeaderWhoseIncludeIsMissing()
  {
    // Arrange (a fatal error hides the headers after it: the parse goes again without that one)
    using var directory = new TempDirectory("netocc-gen-docs");
    directory.Write("Demo_Broken.hxx", "#pragma once\n#include <Demo_Missing.hxx>\n");
    directory.Write("Demo_Curve.hxx", Header);
    var parser = new PackageParser([directory.FullName], []);

    // Act
    var docs = parser.ParseDocs("Demo", ["Demo_Broken.hxx", "Demo_Curve.hxx"], [], []);

    // Assert
    Assert.That(docs.Keys, Is.EquivalentTo(new[] { "Demo_Curve", "Demo_Kind" }));
  }

  [Test]
  public void ParseDocs_WithoutAHeaderThatCompiles_Fails()
  {
    // Arrange
    using var directory = new TempDirectory("netocc-gen-docs");
    directory.Write("Demo_Broken.hxx", "#pragma once\n#include <Demo_Missing.hxx>\n");
    var parser = new PackageParser([directory.FullName], []);

    // Act
    Action parse = () => parser.ParseDocs("Demo", ["Demo_Broken.hxx"], [], []);

    // Assert
    Assert.That(
      parse,
      Throws.InvalidOperationException.With.Message.EqualTo(
        "Demo: no header compiles, e.g. Demo_Broken.hxx: 'Demo_Missing.hxx' file not found"));
  }

  [Test]
  public void ParseDocs_WithAFatalErrorInASystemHeader_Fails()
  {
    // Arrange (a system header that includes one the toolchain lacks, as the C++ library's
    // <cstddef> includes the stddef.h libclang has none of on Linux: no OCCT header is to blame)
    using var directory = new TempDirectory("netocc-gen-docs");
    using var system = new TempDirectory("netocc-gen-system");
    system.Write("demo_system.h", "#pragma once\n#include <demo_builtin.h>\n");
    directory.Write("Demo_Curve.hxx",
                    "#pragma once\n#include <demo_system.h>\nclass Demo_Curve {};\n");
    var parser = new PackageParser([directory.FullName], ["-isystem", system.FullName]);

    // Act
    Action parse = () => parser.ParseDocs("Demo", ["Demo_Curve.hxx"], [], []);

    // Assert
    Assert.That(
      parse,
      Throws.InvalidOperationException.With.Message.Contains(
        "demo_system.h: 'demo_builtin.h' file not found (a system header"));
  }

  [Test]
  public void Match_FindsTheOverloadByItsParameterNames()
  {
    // Arrange (a default argument makes the shorter C# overload of the longer C++ one)
    var type = new TypeDocs();
    type.Members.Add(new MemberDocs("Value", ["theU"], "<summary>u</summary>"));
    type.Members.Add(new MemberDocs("Value", ["theU", "theV", "theTolerance"],
                                    "<summary>uv</summary>"));

    // Act
    string?[] matched =
      [Docs.Match(type, "Value", ["theU"]), Docs.Match(type, "Value", ["theU", "theV"])];

    // Assert
    Assert.That(matched, Is.EqualTo(new[] { "<summary>u</summary>", "<summary>uv</summary>" }));
  }

  [Test]
  public void Match_GuessesNothingBetweenDifferentOverloads()
  {
    // Arrange (C# names no C++ overload has, and two of that many arguments documented differently)
    var type = new TypeDocs();
    type.Members.Add(new MemberDocs("Set", ["theX"], "<summary>x</summary>"));
    type.Members.Add(new MemberDocs("Set", ["theY"], "<summary>y</summary>"));

    // Act
    var matched = Docs.Match(type, "Set", ["value"]);

    // Assert
    Assert.That(matched, Is.Null);
  }

  [Test]
  public void Match_MakesParameterNamesSafeAsTheGeneratorDoes()
  {
    // Arrange (a C# keyword gets a trailing _ in the .i, which SWIG keeps)
    var type = new TypeDocs();
    type.Members.Add(new MemberDocs("Move", ["base", "theDistance"], "<summary>moved</summary>"));
    type.Members.Add(new MemberDocs("Move", ["theFrom", "theDistance"],
                                    "<summary>other</summary>"));

    // Act
    var matched = Docs.Match(type, "Move", ["base_", "theDistance"]);

    // Assert
    Assert.That(matched, Is.EqualTo("<summary>moved</summary>"));
  }
}
