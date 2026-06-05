// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using NAudio.Wave;
using Concentus;
using Concentus.Oggfile;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// A WaveStream wrapper around Concentus to decode Opus-encoded audio in Ogg container files.
/// </summary>
public class OpusWaveStream : WaveStream
{
    private const int OpusSampleRate = 48000;
    private const int OpusChannels = 2;
    private readonly RawSourceWaveStream _rawStream;

    public OpusWaveStream(string filePath) : this(OpenRead(filePath), leaveOpen: false)
    {
    }

    public OpusWaveStream(Stream stream) : this(stream, leaveOpen: true)
    {
    }

    private OpusWaveStream(Stream stream, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            var decoder = OpusCodecFactory.CreateDecoder(OpusSampleRate, OpusChannels, TextWriter.Null);
            var oggIn = new OpusOggReadStream(decoder, stream);
            var pcmStream = new MemoryStream();

            while (oggIn.HasNextPacket)
            {
                short[]? packet = oggIn.DecodeNextPacket();
                if (packet is null || packet.Length == 0)
                    continue;

                byte[] buffer = new byte[packet.Length * sizeof(short)];
                Buffer.BlockCopy(packet, 0, buffer, 0, buffer.Length);
                pcmStream.Write(buffer, 0, buffer.Length);
            }

            pcmStream.Position = 0;
            _rawStream = new RawSourceWaveStream(pcmStream, new WaveFormat(OpusSampleRate, 16, OpusChannels));
        }
        catch
        {
            if (!leaveOpen)
            {
                stream.Dispose();
            }

            throw;
        }

        if (!leaveOpen)
        {
            stream.Dispose();
        }
    }

    public override WaveFormat WaveFormat => _rawStream.WaveFormat;

    public override long Length => _rawStream.Length;

    public override long Position
    {
        get => _rawStream.Position;
        set
        {
            lock (this)
            {
                long safeValue = AlignToBlock(Math.Clamp(value, 0, Length));
                _rawStream.Position = safeValue;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (this)
        {
            return _rawStream.Read(buffer, offset, count);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rawStream.Dispose();
        }
        base.Dispose(disposing);
    }

    private static FileStream OpenRead(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private long AlignToBlock(long value)
    {
        int blockAlign = Math.Max(1, WaveFormat.BlockAlign);
        return value - (value % blockAlign);
    }
}
