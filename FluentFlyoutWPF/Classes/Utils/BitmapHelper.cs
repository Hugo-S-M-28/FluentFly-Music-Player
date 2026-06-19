// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Wpf.Ui.Appearance;

namespace FluentFlyout.Classes.Utils;

internal static class BitmapHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // LRU cache implementation for caching thumbnails and their dominant colors
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lruList = [];
        private readonly object _sync = new();

        private sealed class CacheEntry(TKey key, TValue value)
        {
            public TKey Key { get; } = key;
            public TValue Value { get; set; } = value;
        }

        public LruCache(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    existing.Value.Value = value;
                    _lruList.Remove(existing);
                    _lruList.AddFirst(existing);
                    return;
                }

                var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
                _lruList.AddFirst(node);
                _map[key] = node;

                if (_map.Count <= _capacity)
                    return;

                var leastRecent = _lruList.Last;
                if (leastRecent == null)
                    return;

                _lruList.RemoveLast();
                _map.Remove(leastRecent.Value.Key);
            }
        }
    }

    private const int _maxThumbnailSize = 256; // previously 512, reduced for application memory
    private const int _cacheEntryLimit = 5;

    // cached thumbnails to prevent reprocessing
    private static readonly LruCache<int, BitmapImage> _thumbnailCache = new(_cacheEntryLimit);

    // cached bitmapImage hashes and their dominant colors
    private static readonly LruCache<string, List<SolidColorBrush>> _dominantColorsCache = new(_cacheEntryLimit);

    private static int _currentHashCode = 0;
    private static readonly AsyncLocal<int> _currentHashCodeContext = new();
    private static volatile bool _hasAlbumArt;

    // current or latest dominant colors
    private static List<SolidColorBrush>? _currentDominantColors;
    private readonly record struct ColorSample(byte R, byte G, byte B, double Weight);

    public static List<SolidColorBrush> SavedDominantColors
    {
        get => _currentDominantColors ??= [];
    }

    public static void SetSavedDominantColors(List<SolidColorBrush> colors)
    {
        _currentDominantColors = colors;
    }

    public static bool HasAlbumArt
    {
        get => _hasAlbumArt;
    }

    public static void SetHasAlbumArt(bool hasAlbumArt)
    {
        _hasAlbumArt = hasAlbumArt;
    }

    public static int GetStableThumbnailHash(IRandomAccessStreamReference thumbnail)
        => GetStableThumbnailHashAsync(thumbnail).GetAwaiter().GetResult();

    public static async Task<int> GetStableThumbnailHashAsync(IRandomAccessStreamReference thumbnail)
    {
        if (thumbnail == null)
            return 0;

        try
        {
            await using Stream stream = await OpenThumbnailReadStreamAsync(thumbnail).ConfigureAwait(false);
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToInt32(hashBytes, 0);
        }
        catch (Exception ex)
        {
            Logger.Info(ex, "Failed to compute thumbnail hash; falling back to object hash");
            return thumbnail.GetHashCode();
        }
    }

    public static int GetBitmapContentHash(BitmapSource? image)
    {
        if (image == null)
            return 0;

        try
        {
            var formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = image;
            formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
            formattedBitmap.EndInit();
            formattedBitmap.Freeze();

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            using SHA256 sha256 = SHA256.Create();
            byte[] widthBytes = BitConverter.GetBytes(width);
            byte[] heightBytes = BitConverter.GetBytes(height);
            sha256.TransformBlock(widthBytes, 0, widthBytes.Length, null, 0);
            sha256.TransformBlock(heightBytes, 0, heightBytes.Length, null, 0);
            sha256.TransformFinalBlock(pixels, 0, pixels.Length);

            return BitConverter.ToInt32(sha256.Hash!, 0);
        }
        catch (Exception ex)
        {
            Logger.Info(ex, "Failed to compute bitmap content hash; falling back to object hash");
            return image.GetHashCode();
        }
    }

    internal static void SetCurrentBitmap(BitmapImage? image)
    {
        if (image == null)
        {
            _currentHashCode = 0;
            _currentHashCodeContext.Value = 0;
            _hasAlbumArt = false;
            return;
        }

        // Use a simple hash for the image if it doesn't have one
        int hashCode = image.GetHashCode();
        _thumbnailCache.Set(hashCode, image);
        _currentHashCode = hashCode;
        _currentHashCodeContext.Value = hashCode;
        _hasAlbumArt = true;
    }

    internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference? thumbnail, int maxThumbnailSize = _maxThumbnailSize)
        => GetThumbnailAsync(thumbnail, maxThumbnailSize).GetAwaiter().GetResult();

    internal static async Task<BitmapImage?> GetThumbnailAsync(IRandomAccessStreamReference? thumbnail, int maxThumbnailSize = _maxThumbnailSize)
    {
        if (thumbnail == null)
            return null;

        int hashCode = await GetStableThumbnailHashAsync(thumbnail).ConfigureAwait(false);

        if (hashCode == 0)
            return null;

        if (_thumbnailCache.TryGetValue(hashCode, out var cachedImage) && cachedImage != null)
        {
            _currentHashCode = hashCode;
            _currentHashCodeContext.Value = hashCode;
            _hasAlbumArt = true;
            return cachedImage;
        }
        await using (var imageStream = await OpenThumbnailReadStreamAsync(thumbnail).ConfigureAwait(false))
        {
            // initialize the BitmapImage
            imageStream.Position = 0;
            BitmapImage image = new();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = maxThumbnailSize;
            image.StreamSource = imageStream;
            image.EndInit();
            image.Freeze();

            // add bitmap to thumbnail cache
            _thumbnailCache.Set(hashCode, image);

            _currentHashCode = hashCode;
            _currentHashCodeContext.Value = hashCode;
            _hasAlbumArt = true;
            return image;
        }
    }

    private static Stream OpenThumbnailReadStream(IRandomAccessStreamReference thumbnail)
        => OpenThumbnailReadStreamAsync(thumbnail).GetAwaiter().GetResult();

    private static async Task<Stream> OpenThumbnailReadStreamAsync(IRandomAccessStreamReference thumbnail)
    {
        var stream = await thumbnail.OpenReadAsync().AsTask().ConfigureAwait(false);
        return stream.AsStreamForRead();
    }

    internal static CroppedBitmap? CropToSquare(BitmapImage? sourceImage)
    {
        if (sourceImage == null)
            return null;

        int size = (int)Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight);
        int x = (sourceImage.PixelWidth - size) / 2;
        int y = (sourceImage.PixelHeight - size) / 2;

        var rect = new Int32Rect(x, y, size, size);

        // create a CroppedBitmap (this is a lightweight object)
        var croppedBitmap = new CroppedBitmap(sourceImage, rect);

        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    /// <summary>
    /// Gets dominant colors from last cached Bitmap from GetThumbnail method.
    /// K-means clustering for multiple colors, histogram peak for single color.
    /// </summary>
    /// <param name="colorCount">Amount of colors needed</param>
    /// <param name="maxIterations">Amount of k-means iterations (more = higher accuracy)</param>
    /// <returns>List of dominant colors from cached Bitmap as SolidColorBrush</returns>
    public static List<SolidColorBrush> GetDominantColors(int colorCount, int maxIterations = 15)
    {
        int hashCode = _currentHashCodeContext.Value != 0 ? _currentHashCodeContext.Value : _currentHashCode;

        if (hashCode == 0)
        {
            return [];
        }

        if (_dominantColorsCache.TryGetValue(hashCode.ToString(), out var cachedColors) && cachedColors != null)
        {
            _currentDominantColors = cachedColors;
            return _currentDominantColors;
        }

        // convert BitmapImage to BGRA byte array
        if (!_thumbnailCache.TryGetValue(hashCode, out var sourceBitmap) || sourceBitmap == null)
        {
            Logger.Warn($"Thumbnail cache miss while extracting dominant colors");
            return _currentDominantColors ?? [];
        }

        var result = GetDominantColors(sourceBitmap, colorCount, hashCode.ToString(), maxIterations);
        _currentDominantColors = result;
        return result;
    }

    public static List<SolidColorBrush> GetDominantColors(BitmapSource image, int colorCount, string cacheKey, int maxIterations = 15)
    {
        if (image == null) return [];

        if (_dominantColorsCache.TryGetValue(cacheKey, out var cachedColors) && cachedColors != null)
        {
            return cachedColors;
        }

#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        try
        {
            var formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = image;
            formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
            formattedBitmap.EndInit();
            formattedBitmap.Freeze();

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;

            byte[] pixels = new byte[height * stride];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            bool darkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
            var samples = BuildDeterministicSamples(pixels, width, height, allowLowChroma: false);
            if (samples.Count == 0)
            {
                samples = BuildDeterministicSamples(pixels, width, height, allowLowChroma: true);
            }

            if (samples.Count == 0)
            {
                return [];
            }

            List<Color> result = colorCount == 1
                ? [ExtractRepresentativeColor(samples)]
                : ExtractClusterColors(samples, colorCount, maxIterations);

            result = [.. result.Select(c => NormalizeAccentColor(c, darkTheme))];

            // convert to brushes
            var brushes = result.Select(c =>
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze(); // makes it immutable & thread-safe
                return brush;
            }).ToList();

            _dominantColorsCache.Set(cacheKey, brushes);

#if DEBUG
            stopwatch.Stop();
            Logger.Debug($"Dominant color extraction for key {cacheKey} took {stopwatch.Elapsed.TotalMilliseconds} ms");
#endif
            return brushes;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error extracting dominant colors for key {cacheKey}");
            return [];
        }
    }

    private static double ToLinear(byte v)
        => Math.Pow(v / 255.0, 2.2);

    private static byte ToGamma(double v)
        => (byte)Math.Clamp(Math.Pow(v, 1.0 / 2.2) * 255.0, 0, 255);

    internal static Color ExtractRepresentativeColorForTests(params (byte r, byte g, byte b, double weight)[] samples)
    {
        var colorSamples = samples.Select(sample => new ColorSample(sample.r, sample.g, sample.b, sample.weight)).ToList();
        return ExtractRepresentativeColor(colorSamples);
    }

    private static List<ColorSample> BuildDeterministicSamples(byte[] pixels, int width, int height, bool allowLowChroma = false)
    {
        var samples = new List<ColorSample>(Math.Min(width * height, 4096));
        int stepX = Math.Max(1, width / 48);
        int stepY = Math.Max(1, height / 48);
        double centerX = (width - 1) / 2.0;
        double centerY = (height - 1) / 2.0;
        double maxDistance = Math.Sqrt((centerX * centerX) + (centerY * centerY));

        for (int y = 0; y < height; y += stepY)
        {
            for (int x = 0; x < width; x += stepX)
            {
                int pixelIndex = ((y * width) + x) * 4;
                byte b = pixels[pixelIndex];
                byte g = pixels[pixelIndex + 1];
                byte r = pixels[pixelIndex + 2];
                byte a = pixels[pixelIndex + 3];

                if (a < 160)
                    continue;

                var metrics = AnalyzeColor(r, g, b);
                if (!allowLowChroma && metrics.Chroma < 0.08f)
                    continue;

                double distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                double centerWeight = 1.0 - (distance / Math.Max(1.0, maxDistance));
                double lumaWeight = metrics.Lightness switch
                {
                    < 0.10f => 0.15,
                    > 0.92f => 0.20,
                    _ => 1.0
                };
                double weight = Math.Max(0.15, (0.35 + (metrics.Chroma * 1.8) + (centerWeight * 0.9)) * lumaWeight);

                samples.Add(new ColorSample(r, g, b, weight));
            }
        }

        return samples;
    }

    private static Color ExtractRepresentativeColor(List<ColorSample> samples)
    {
        const int quantBits = 4;
        const int bins = 1 << quantBits;
        var histogram = new double[bins * bins * bins];
        var totals = new (double R, double G, double B, double Weight)[histogram.Length];

        foreach (var sample in samples)
        {
            var metrics = AnalyzeColor(sample.R, sample.G, sample.B);
            double prominence = sample.Weight
                * Math.Max(0.18, metrics.Chroma)
                * (1.0 - Math.Abs(metrics.Lightness - 0.52f));

            int ri = sample.R >> (8 - quantBits);
            int gi = sample.G >> (8 - quantBits);
            int bi = sample.B >> (8 - quantBits);
            int index = ri * bins * bins + gi * bins + bi;

            histogram[index] += prominence;
            totals[index].R += sample.R * prominence;
            totals[index].G += sample.G * prominence;
            totals[index].B += sample.B * prominence;
            totals[index].Weight += prominence;
        }

        int peakIndex = Array.IndexOf(histogram, histogram.Max());
        if (peakIndex < 0 || totals[peakIndex].Weight <= 0.0001)
        {
            var fallback = samples
                .OrderByDescending(s => s.Weight)
                .First();
            return Color.FromArgb(255, fallback.R, fallback.G, fallback.B);
        }

        byte r = (byte)Math.Clamp(totals[peakIndex].R / totals[peakIndex].Weight, 0, 255);
        byte g = (byte)Math.Clamp(totals[peakIndex].G / totals[peakIndex].Weight, 0, 255);
        byte b = (byte)Math.Clamp(totals[peakIndex].B / totals[peakIndex].Weight, 0, 255);
        return Color.FromArgb(255, r, g, b);
    }

    private static List<Color> ExtractClusterColors(List<ColorSample> samples, int colorCount, int maxIterations)
    {
        var orderedSeeds = samples
            .OrderByDescending(sample => sample.Weight)
            .Take(Math.Max(colorCount * 3, colorCount))
            .ToList();

        var centroids = orderedSeeds
            .Take(colorCount)
            .Select(sample => new double[] { sample.R, sample.G, sample.B })
            .ToList();

        while (centroids.Count < colorCount)
        {
            var fallback = orderedSeeds[centroids.Count % orderedSeeds.Count];
            centroids.Add([fallback.R, fallback.G, fallback.B]);
        }

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var sums = Enumerable.Range(0, colorCount)
                .Select(_ => new double[4])
                .ToArray();
            bool converged = true;

            foreach (var sample in samples)
            {
                int best = 0;
                double bestDistance = double.MaxValue;

                for (int i = 0; i < colorCount; i++)
                {
                    double dr = sample.R - centroids[i][0];
                    double dg = sample.G - centroids[i][1];
                    double db = sample.B - centroids[i][2];
                    double distance = dr * dr + dg * dg + db * db;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                sums[best][0] += sample.R * sample.Weight;
                sums[best][1] += sample.G * sample.Weight;
                sums[best][2] += sample.B * sample.Weight;
                sums[best][3] += sample.Weight;
            }

            for (int i = 0; i < colorCount; i++)
            {
                if (sums[i][3] <= 0.0001)
                    continue;

                double newR = sums[i][0] / sums[i][3];
                double newG = sums[i][1] / sums[i][3];
                double newB = sums[i][2] / sums[i][3];

                double dr = newR - centroids[i][0];
                double dg = newG - centroids[i][1];
                double db = newB - centroids[i][2];
                if ((dr * dr) + (dg * dg) + (db * db) > 1.0)
                    converged = false;

                centroids[i][0] = newR;
                centroids[i][1] = newG;
                centroids[i][2] = newB;
            }

            if (converged)
                break;
        }

        return [.. centroids.Select(c => Color.FromArgb(255, (byte)c[0], (byte)c[1], (byte)c[2]))];
    }

    private static Color NormalizeAccentColor(Color color, bool darkTheme)
    {
        double r = ToLinear(color.R);
        double g = ToLinear(color.G);
        double b = ToLinear(color.B);
        double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

        if (darkTheme)
        {
            double targetLuminance = Math.Clamp(Math.Max(luminance, 0.42), 0.42, 0.74);
            double scale = targetLuminance / Math.Max(0.0001, luminance);
            r *= scale;
            g *= scale;
            b *= scale;

            const double desaturation = 0.22;
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            r += (l - r) * desaturation;
            g += (l - g) * desaturation;
            b += (l - b) * desaturation;
        }
        else
        {
            const double desaturation = 0.28;
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            r += (l - r) * desaturation;
            g += (l - g) * desaturation;
            b += (l - b) * desaturation;

            double adjustedLuminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            if (adjustedLuminance > 0.72)
            {
                double scale = 0.72 / adjustedLuminance;
                r *= scale;
                g *= scale;
                b *= scale;
            }
        }

        return Color.FromArgb(color.A, ToGamma(r), ToGamma(g), ToGamma(b));
    }

    private static (float Chroma, float Lightness) AnalyzeColor(byte rByte, byte gByte, byte bByte)
    {
        float r = rByte / 255f;
        float g = gByte / 255f;
        float b = bByte / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        return (max - min, (max + min) / 2f);
    }

    public static bool IsReliableAlbumArt(BitmapSource? image)
    {
        if (image == null)
            return false;

        if (image.PixelWidth < 48 || image.PixelHeight < 48)
            return false;

        if (image.PixelWidth == 0 || image.PixelHeight == 0)
            return false;

        return true;
    }

    public static bool IsNearlyFlat(byte[] pixels, int width, int height)
    {
        int stepX = Math.Max(1, width / 8);
        int stepY = Math.Max(1, height / 8);

        byte minR = 255, maxR = 0;
        byte minG = 255, maxG = 0;
        byte minB = 255, maxB = 0;
        bool hasPixels = false;

        for (int y = 0; y < height; y += stepY)
        {
            for (int x = 0; x < width; x += stepX)
            {
                int pixelIndex = ((y * width) + x) * 4;
                if (pixelIndex + 3 >= pixels.Length)
                    continue;

                byte b = pixels[pixelIndex];
                byte g = pixels[pixelIndex + 1];
                byte r = pixels[pixelIndex + 2];
                byte a = pixels[pixelIndex + 3];

                if (a < 160)
                    continue;

                hasPixels = true;
                if (r < minR) minR = r;
                if (r > maxR) maxR = r;
                if (g < minG) minG = g;
                if (g > maxG) maxG = g;
                if (b < minB) minB = b;
                if (b > maxB) maxB = b;
            }
        }

        if (!hasPixels)
            return true;

        int threshold = 8;
        return (maxR - minR <= threshold) && (maxG - minG <= threshold) && (maxB - minB <= threshold);
    }
}
