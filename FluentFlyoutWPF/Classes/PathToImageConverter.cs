using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// Converts a file path to a BitmapImage for display.
/// Features:
///   - Thread-safe in-memory cache to avoid reloading the same image
///   - Configurable decode size via ConverterParameter (default: 80px for thumbnails)
///   - All images are Frozen for safe cross-thread access
/// </summary>
public class PathToImageConverter : IValueConverter
{
    // Thread-safe cache: path+size -> frozen BitmapImage
    private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new();

    // Maximum cache size to prevent unbounded memory growth
    private const int MaxCacheSize = 500;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
            return null;

        // Determine decode pixel width from ConverterParameter (default 80 for small thumbnails)
        int decodeWidth = 80;
        if (parameter is string paramStr && int.TryParse(paramStr, out int pw) && pw > 0)
        {
            decodeWidth = pw;
        }
        else if (parameter is int pi && pi > 0)
        {
            decodeWidth = pi;
        }

        string cacheKey = $"{path}|{decodeWidth}";

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();

            // Evict oldest entries if cache is too large
            if (_cache.Count >= MaxCacheSize)
            {
                // Simple eviction: clear half the cache
                int toRemove = MaxCacheSize / 2;
                foreach (var key in _cache.Keys)
                {
                    if (toRemove-- <= 0) break;
                    _cache.TryRemove(key, out _);
                }
            }

            _cache.TryAdd(cacheKey, image);
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Clear the entire image cache (useful when library is re-scanned).
    /// </summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }

    public static void InvalidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string prefix = $"{path}|";
        foreach (var key in _cache.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            _cache.TryRemove(key, out _);
        }
    }
}
