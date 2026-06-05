// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using NAudio.Wave;
using NLayer;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// A WaveStream wrapper around NLayer's MpegFile to natively decode MP1, MP2, and MP3 files.
/// </summary>
public class MpegFileWaveStream : WaveStream
{
    private readonly MpegFile _mpegFile;
    private readonly WaveFormat _waveFormat;
    private readonly long _length;

    public MpegFileWaveStream(string filePath)
    {
        _mpegFile = new MpegFile(filePath);
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_mpegFile.SampleRate, _mpegFile.Channels);
        _length = CalculateAlignedLength();
    }

    public MpegFileWaveStream(Stream stream)
    {
        _mpegFile = new MpegFile(stream);
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_mpegFile.SampleRate, _mpegFile.Channels);
        _length = CalculateAlignedLength();
    }

    public override WaveFormat WaveFormat => _waveFormat;

    public override long Length => _length;

    public override long Position
    {
        get
        {
            try
            {
                return (long)(_mpegFile.Time.TotalSeconds * _waveFormat.AverageBytesPerSecond);
            }
            catch
            {
                return 0;
            }
        }
        set
        {
            lock (this)
            {
                try
                {
                    long safeValue = AlignToBlock(Math.Clamp(value, 0, Length));
                    double targetSeconds = (double)safeValue / _waveFormat.AverageBytesPerSecond;
                    double maxSeekSeconds = GetSafeMaxSeekSeconds();
                    _mpegFile.Time = TimeSpan.FromSeconds(Math.Clamp(targetSeconds, 0, maxSeekSeconds));
                }
                catch
                {
                    // Ignore seek errors or ObjectDisposedException to prevent crash
                }
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (this)
        {
            // Convert byte count to float count
            int floatCount = count / 4;
            if (floatCount <= 0) return 0;

            float[] floatBuffer = new float[floatCount];
            int readSamples = 0;
            int attempts = 0;

            while (attempts < 3)
            {
                try
                {
                    readSamples = _mpegFile.ReadSamples(floatBuffer, 0, floatCount);
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Stream closed/disposed, return 0 bytes read to end stream cleanly
                    return 0;
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or InvalidDataException)
                {
                    attempts++;
                    if (attempts >= 3)
                    {
                        // Fill with silence to avoid crash and prevent infinite looping
                        Array.Clear(floatBuffer, 0, floatBuffer.Length);
                        readSamples = floatCount;
                        break;
                    }

                    // Attempt recovery: seek to current time to force NLayer to re-align with frame headers
                    try
                    {
                        var curTime = _mpegFile.Time;
                        _mpegFile.Time = curTime;
                    }
                    catch
                    {
                        // If seeking fails, return silence for this buffer
                        Array.Clear(floatBuffer, 0, floatBuffer.Length);
                        readSamples = floatCount;
                        break;
                    }
                }
            }

            if (readSamples <= 0) return 0;

            // Copy float buffer to byte buffer
            Buffer.BlockCopy(floatBuffer, 0, buffer, offset, readSamples * 4);

            return readSamples * 4;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _mpegFile.Dispose();
        }
        base.Dispose(disposing);
    }

    private long CalculateAlignedLength()
    {
        long estimatedLength = (long)Math.Round(_mpegFile.Duration.TotalSeconds * _waveFormat.AverageBytesPerSecond);
        return AlignToBlock(Math.Max(0, estimatedLength));
    }

    private long AlignToBlock(long value)
    {
        int blockAlign = Math.Max(1, _waveFormat.BlockAlign);
        return value - (value % blockAlign);
    }

    private double GetSafeMaxSeekSeconds()
    {
        double durationSeconds = _mpegFile.Duration.TotalSeconds;
        if (durationSeconds <= 0)
        {
            return 0;
        }

        double seekFrameSeconds = (double)Math.Max(1, _waveFormat.BlockAlign) / _waveFormat.AverageBytesPerSecond;
        return Math.Max(0, durationSeconds - seekFrameSeconds);
    }
}
