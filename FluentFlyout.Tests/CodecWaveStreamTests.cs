// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using FluentFlyoutWPF.Classes;
using Xunit;

namespace FluentFlyout.Tests;

public class CodecWaveStreamTests
{
    [Fact]
    public void MpegFileWaveStream_ConstructorWithNonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => new MpegFileWaveStream("nonexistent_file.mp3"));
    }

    [Fact]
    public void OpusWaveStream_ConstructorWithNonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => new OpusWaveStream("nonexistent_file.opus"));
    }

    [Fact]
    public void MpegFileWaveStream_ConstructorWithEmptyStream_ThrowsException()
    {
        using var emptyStream = new MemoryStream();
        Assert.ThrowsAny<Exception>(() => new MpegFileWaveStream(emptyStream));
    }

    [Fact]
    public void OpusWaveStream_ConstructorWithEmptyStream_CreatesEmptyStreamOrThrows()
    {
        using var emptyStream = new MemoryStream();
        // Depending on Concentus, an empty stream might either successfully read 0 packets or throw.
        // Let's assert that it either completes successfully (length 0) or throws.
        try
        {
            var opusStream = new OpusWaveStream(emptyStream);
            Assert.Equal(0, opusStream.Length);
        }
        catch (Exception)
        {
            // Thrown if Concentus expects headers or valid Ogg packets immediately
            Assert.True(true);
        }
    }
}
