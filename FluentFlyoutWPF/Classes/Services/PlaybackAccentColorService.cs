// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace FluentFlyoutWPF.Classes.Services;

internal class PlaybackAccentColorResult
{
    public SolidColorBrush? AccentBrush { get; }
    public bool HasReliableAlbumArtAccent { get; }
    public string AccentKey { get; }
    public bool IsCurrent { get; }

    public PlaybackAccentColorResult(SolidColorBrush? accentBrush, bool hasReliableAlbumArtAccent, string accentKey, bool isCurrent)
    {
        AccentBrush = accentBrush;
        HasReliableAlbumArtAccent = hasReliableAlbumArtAccent;
        AccentKey = accentKey;
        IsCurrent = isCurrent;
    }
}

internal class PlaybackAccentColorService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly object _lock = new();
    private static PlaybackAccentColorService? _instance;

    public static PlaybackAccentColorService Instance
    {
        get
        {
            lock (_lock)
            {
                return _instance ??= new PlaybackAccentColorService();
            }
        }
    }

    private readonly object _stateLock = new();
    private string _currentRequestKey = string.Empty;

    private PlaybackAccentColorService() { }

    public string GetCurrentRequestKey()
    {
        lock (_stateLock)
        {
            return _currentRequestKey;
        }
    }

    public async Task<PlaybackAccentColorResult> ProcessPlaybackAccentColorAsync(
        PlaybackSourceKind sourceKind,
        string sessionId,
        string title,
        string artist,
        IRandomAccessStreamReference? thumbnailRef,
        BitmapImage? internalCover)
    {
        // Compute stable thumbnail hash
        int thumbnailHash = 0;
        if (sourceKind == PlaybackSourceKind.Internal)
        {
            thumbnailHash = BitmapHelper.GetBitmapContentHash(internalCover);
        }
        else if (sourceKind == PlaybackSourceKind.External && thumbnailRef != null)
        {
            thumbnailHash = await BitmapHelper.GetStableThumbnailHashAsync(thumbnailRef).ConfigureAwait(false);
        }

        string requestKey = $"{sourceKind}_{sessionId}_{title}_{artist}_{thumbnailHash}";

        lock (_stateLock)
        {
            _currentRequestKey = requestKey;
        }

        Logger.Info($"Starting accent color processing for: {requestKey}");

        BitmapSource? imageToProcess = null;
        bool isReliable = false;

        try
        {
            if (sourceKind == PlaybackSourceKind.Internal)
            {
                imageToProcess = internalCover;
                isReliable = imageToProcess != null && BitmapHelper.IsReliableAlbumArt(imageToProcess);
            }
            else // External
            {
                if (thumbnailRef != null)
                {
                    var thumbImage = await BitmapHelper.GetThumbnailAsync(thumbnailRef).ConfigureAwait(false);
                    if (thumbImage != null)
                    {
                        imageToProcess = thumbImage;
                        isReliable = BitmapHelper.IsReliableAlbumArt(thumbImage);
                    }
                }
            }

            if (imageToProcess != null && isReliable)
            {
                bool isFlat = false;
                try
                {
                    await Task.Run(() =>
                    {
                        var formattedBitmap = new FormatConvertedBitmap();
                        formattedBitmap.BeginInit();
                        formattedBitmap.Source = imageToProcess;
                        formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
                        formattedBitmap.EndInit();
                        formattedBitmap.Freeze();

                        int width = formattedBitmap.PixelWidth;
                        int height = formattedBitmap.PixelHeight;
                        int stride = width * 4;
                        byte[] pixels = new byte[height * stride];
                        formattedBitmap.CopyPixels(pixels, stride, 0);

                        if (BitmapHelper.IsNearlyFlat(pixels, width, height))
                        {
                            isFlat = true;
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to check flatness for image");
                    isFlat = true;
                }

                if (isFlat)
                {
                    Logger.Warn($"Cover art image rejected because it is nearly flat: {requestKey}");
                    isReliable = false;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error loading or checking cover art for: {requestKey}");
            isReliable = false;
        }

        SolidColorBrush? accentBrush = null;

        if (isReliable && imageToProcess != null)
        {
            try
            {
                var colors = await Task.Run(() =>
                {
                    string cacheKey = thumbnailHash.ToString();
                    return BitmapHelper.GetDominantColors(imageToProcess, 1, cacheKey);
                }).ConfigureAwait(false);

                accentBrush = colors.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error extracting dominant colors for: {requestKey}");
                accentBrush = null;
            }
        }

        // Verify request is still active/current
        bool isCurrent = false;
        lock (_stateLock)
        {
            if (_currentRequestKey == requestKey)
            {
                isCurrent = true;
            }
        }

        if (!isCurrent)
        {
            Logger.Info($"Discarded obsolete accent color result for: {requestKey}");
            return new PlaybackAccentColorResult(null, false, requestKey, false);
        }

        // Update state in BitmapHelper
        if (accentBrush != null && isReliable)
        {
            BitmapHelper.SetSavedDominantColors([accentBrush]);
            BitmapHelper.SetHasAlbumArt(true);
        }
        else
        {
            BitmapHelper.SetSavedDominantColors([]);
            BitmapHelper.SetHasAlbumArt(false);
        }

        return new PlaybackAccentColorResult(accentBrush, isReliable && accentBrush != null, requestKey, true);
    }
}
