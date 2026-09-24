// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;

//
using OCC.Core;

namespace NetOcc.Viewer;

/// <summary>
/// Opens STEP files into a viewer, one at a time: reads them off the UI thread and shows them on
/// it. Returns what the status line says; a file that doesn't read is a status, not an exception.
/// Call it from the UI thread.
/// </summary>
public sealed class FileOpener
{
  private const string Controls = "Left button: rotate, middle: pan, right or wheel: zoom.";

  // per process: two viewers don't write the same sample file
  private static readonly string SamplePath =
    Path.Combine(Path.GetTempPath(), $"netocc-sample-{Environment.ProcessId}.step");

  /// <summary>An open is running; another one is refused until it ends.</summary>
  public bool IsBusy { get; private set; }

  /// <summary>Reads <paramref name="path"/> and shows it.</summary>
  /// <param name="viewer">
  /// The viewer to show the document in, asked once it's read: null when the view has gone.
  /// </param>
  /// <param name="status">Takes the progress for the status line.</param>
  public Task<string> OpenAsync(string path, Func<OcctViewer?> viewer, Action<string> status) =>
    Exclusive(() => ReadAndShowAsync(path, viewer, status));

  /// <summary>Writes the sample assembly as STEP, then opens it like any other file.</summary>
  public Task<string> OpenSampleAsync(Func<OcctViewer?> viewer, Action<string> status) =>
    Exclusive(async () =>
    {
      status("Writing the sample...");
      try
      {
        await Task.Run(() => SampleModel.Write(SamplePath));
      }
      catch (Exception exception) when (exception is InvalidOperationException or OcctException)
      {
        return exception.Message;
      }

      return await ReadAndShowAsync(SamplePath, viewer, status);
    });

  private async Task<string> Exclusive(Func<Task<string>> open)
  {
    if (IsBusy)
    {
      return "Another file is still opening.";
    }

    IsBusy = true;
    try
    {
      return await open();
    }
    finally
    {
      IsBusy = false;
    }
  }

  private static async Task<string> ReadAndShowAsync(string path, Func<OcctViewer?> viewer,
                                                     Action<string> status)
  {
    var name = Path.GetFileName(path);
    status($"Reading {name}...");
    StepDocument document;
    try
    {
      document = await Task.Run(() => StepDocument.Read(path));
    }
    catch (Exception exception) when (exception is InvalidDataException or OcctException)
    {
      return $"{name}: {exception.Message}";
    }

    // asked after the read: Avalonia creates the view again when its control moves
    if (viewer() is not { } target)
    {
      document.Dispose();
      return $"{name}: there's no view to show it in.";
    }

    target.Show(document);
    return $"{name}: {document.Roots.Count} shape(s). {Controls}";
  }
}
