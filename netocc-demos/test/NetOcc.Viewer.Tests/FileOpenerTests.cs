// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

// NUnit
using NUnit.Framework;

namespace NetOcc.Viewer.Tests;

/// <summary>
/// What the viewers' status line says when they open a file; no view needed (the viewer function
/// returns none).
/// </summary>
[TestFixture]
public class FileOpenerTests
{
  private string _directory = null!;

  [SetUp]
  public void CreateDirectory()
  {
    _directory = Path.Combine(Path.GetTempPath(), $"netocc-viewer Ünïcödé {Guid.NewGuid():N}");
    Directory.CreateDirectory(_directory);
  }

  [TearDown]
  public void DeleteDirectory() => Directory.Delete(_directory, true);

  [Test]
  public async Task Open_AFileThatIsntStep_ReportsIt()
  {
    // Arrange (the viewers crashed on this: the error must be a status, not an exception)
    var path = Path.Combine(_directory, "notes.txt");
    await File.WriteAllTextAsync(path, "not a STEP file");
    List<string> progress = [];

    // Act
    var status = await new FileOpener().OpenAsync(path, () => null, progress.Add);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(status, Does.StartWith("notes.txt: ").And.Contain("no STEP shapes"));
      Assert.That(progress, Is.EqualTo(new[] { "Reading notes.txt..." }));
    }
  }

  [Test]
  public async Task Open_WithoutAView_ReadsTheFileAndSaysSo()
  {
    // Arrange
    var path = Path.Combine(_directory, "sample Ünïcödé.step");
    SampleModel.Write(path);

    // Act
    var status = await new FileOpener().OpenAsync(path, () => null, _ => { });

    // Assert
    Assert.That(status, Is.EqualTo("sample Ünïcödé.step: there's no view to show it in."));
  }

  [Test]
  public async Task OpenSample_WritesAndReadsTheSample()
  {
    // Arrange
    List<string> progress = [];

    // Act
    var status = await new FileOpener().OpenSampleAsync(() => null, progress.Add);

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(status, Does.EndWith(": there's no view to show it in."));
      Assert.That(progress, Has.Count.EqualTo(2));
      Assert.That(progress[0], Is.EqualTo("Writing the sample..."));
    }
  }

  [Test]
  public async Task Open_WhileAnotherOpens_IsRefused()
  {
    // Arrange (the second open starts from the first one's progress report, while the first one
    // runs)
    var path = Path.Combine(_directory, "sample.step");
    SampleModel.Write(path);
    var opener = new FileOpener();
    Task<string>? second = null;

    // Act
    await opener.OpenAsync(path, () => null,
                           _ => second ??= opener.OpenAsync(path, () => null, _ => { }));
    var refused = await second!;

    // Assert
    using (Assert.EnterMultipleScope())
    {
      Assert.That(refused, Is.EqualTo("Another file is still opening."));
      Assert.That(opener.IsBusy, Is.False);
    }
  }
}
