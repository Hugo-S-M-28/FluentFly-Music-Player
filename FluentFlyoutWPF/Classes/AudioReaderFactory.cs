// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using NAudio.Wave;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// Factory and utility class for audio readers and format validation.
/// </summary>
public static class AudioReaderFactory
{
    private static readonly string[] SupportedExtensions = new[]
    {
        ".mp3", ".mp2", ".mp1", ".wav", ".wave", ".aif", ".aiff",
        ".flac", ".m4a", ".m4b", ".mp4", ".aac", ".wma", ".asf",
        ".ogg", ".oga", ".opus"
    };

    public enum DecoderKind
    {
        Native,
        SystemFallback
    }

    public sealed record ReaderSelection(WaveStream Reader, string FormatName, DecoderKind DecoderKind);

    /// <summary>
    /// Checks if the file extension is supported by the audio readers.
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <returns>True if supported, false otherwise.</returns>
    public static bool IsSupportedExtension(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext))
                return false;

            return SupportedExtensions.Contains(ext.ToLowerInvariant());
        }
        catch
        {
            return false;
        }
    }

    public static string[] GetSupportedExtensions() => SupportedExtensions.ToArray();

    public static ReaderSelection CreateReader(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Audio file not found.", filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (TryCreateNativeReader(filePath, extension, out var nativeSelection))
            return nativeSelection;

        return new ReaderSelection(
            new MediaFoundationReader(filePath),
            extension switch
            {
                ".m4a" or ".m4b" => "AAC/ALAC in MP4",
                ".mp4" => "Audio in MP4",
                ".aac" => "AAC",
                ".wma" or ".asf" => "Windows Media Audio",
                _ => "System decoder fallback"
            },
            DecoderKind.SystemFallback);
    }

    private static bool TryCreateNativeReader(string filePath, string extension, out ReaderSelection selection)
    {
        var container = DetectContainer(filePath, extension);
        switch (container)
        {
            case AudioContainer.Mpeg:
                selection = new ReaderSelection(new MpegFileWaveStream(filePath), "MPEG Audio", DecoderKind.Native);
                return true;
            case AudioContainer.Flac:
                selection = new ReaderSelection(new NAudio.Flac.FlacReader(filePath), "FLAC", DecoderKind.Native);
                return true;
            case AudioContainer.OggOpus:
                selection = new ReaderSelection(new OpusWaveStream(filePath), "Ogg Opus", DecoderKind.Native);
                return true;
            case AudioContainer.OggVorbis:
                selection = new ReaderSelection(new NAudio.Vorbis.VorbisWaveReader(filePath), "Ogg Vorbis", DecoderKind.Native);
                return true;
            case AudioContainer.Wave:
                selection = new ReaderSelection(new WaveFileReader(filePath), "WAV", DecoderKind.Native);
                return true;
            case AudioContainer.Aiff:
                selection = new ReaderSelection(new AiffFileReader(filePath), "AIFF", DecoderKind.Native);
                return true;
            default:
                selection = null!;
                return false;
        }
    }

    private static AudioContainer DetectContainer(string filePath, string extension)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[16];
        int bytesRead = stream.Read(header);

        if (bytesRead >= 12)
        {
            if (header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
                header[8] == (byte)'W' && header[9] == (byte)'A' && header[10] == (byte)'V' && header[11] == (byte)'E')
            {
                return AudioContainer.Wave;
            }

            if (header[0] == (byte)'F' && header[1] == (byte)'O' && header[2] == (byte)'R' && header[3] == (byte)'M' &&
                header[8] == (byte)'A' && header[9] == (byte)'I' && header[10] == (byte)'F' &&
                (header[11] == (byte)'F' || header[11] == (byte)'C'))
            {
                return AudioContainer.Aiff;
            }
        }

        if (bytesRead >= 4)
        {
            if (header[0] == (byte)'f' && header[1] == (byte)'L' && header[2] == (byte)'a' && header[3] == (byte)'C')
                return AudioContainer.Flac;

            if (header[0] == (byte)'O' && header[1] == (byte)'g' && header[2] == (byte)'g' && header[3] == (byte)'S')
            {
                var oggCodec = DetectOggCodec(stream, header[..bytesRead].ToArray());
                return oggCodec;
            }
        }

        return extension switch
        {
            ".mp1" or ".mp2" or ".mp3" => AudioContainer.Mpeg,
            ".flac" => AudioContainer.Flac,
            ".wav" or ".wave" => AudioContainer.Wave,
            ".aif" or ".aiff" => AudioContainer.Aiff,
            ".ogg" or ".oga" => AudioContainer.OggVorbis,
            ".opus" => AudioContainer.OggOpus,
            _ => AudioContainer.Unknown
        };
    }

    private static AudioContainer DetectOggCodec(Stream stream, byte[] firstBytes)
    {
        const int ProbeSize = 64 * 1024;
        stream.Position = 0;

        byte[] buffer = new byte[Math.Min((int)Math.Max(stream.Length, firstBytes.Length), ProbeSize)];
        int read = stream.Read(buffer, 0, buffer.Length);
        var probe = new ReadOnlySpan<byte>(buffer, 0, read);

        if (probe.IndexOf("OpusHead"u8) >= 0)
            return AudioContainer.OggOpus;

        if (probe.IndexOf(new byte[] { 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }) >= 0)
            return AudioContainer.OggVorbis;

        return AudioContainer.OggVorbis;
    }

    private enum AudioContainer
    {
        Unknown,
        Mpeg,
        Wave,
        Aiff,
        Flac,
        OggVorbis,
        OggOpus
    }
}
