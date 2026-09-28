// SPDX-FileCopyrightText: 2026 Paul Büchner
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace NetOcc.Tests;

/// <summary>
/// Another stream forward only, without Length or Position, like a network, decompressing or
/// encrypting stream: it reads, writes, or neither.
/// </summary>
internal sealed class NonSeekableStream(Stream inner, bool canRead = true, bool canWrite = false)
  : Stream
{
  public override bool CanRead => canRead;
  public override bool CanSeek => false;
  public override bool CanWrite => canWrite;
  public override long Length => throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override int Read(byte[] buffer, int offset, int count) =>
    inner.Read(buffer, offset, count);

  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();

  public override void Write(byte[] buffer, int offset, int count)
  {
    if (!canWrite)
    {
      throw new NotSupportedException();
    }

    inner.Write(buffer, offset, count);
  }

  public override void Flush() => inner.Flush();
}
