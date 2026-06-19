using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MicaWPF.Core.Enums;

namespace FluentFlyoutWPF.Classes.Utils;

public enum MediaBackdropSurface
{
    Flyout,
    Home,
    Taskbar
}

public enum MediaBackdropMaskKind
{
    None,
    CoverGlow,
    RisingGlow
}

public sealed class MediaBackdropStyleDefinition
{
    public int Id { get; }
    public string NameEs { get; }
    public string DescriptionEs { get; }
    public double BlurRadius { get; }
    public double FlyoutOpacity { get; }
    public double HomeOpacity { get; }
    public double Scale { get; }
    public MediaBackdropMaskKind MaskKind { get; }

    public MediaBackdropStyleDefinition(
        int id,
        string nameEs,
        string descriptionEs,
        double blurRadius,
        double flyoutOpacity,
        double homeOpacity,
        double scale,
        MediaBackdropMaskKind maskKind)
    {
        Id = id;
        NameEs = nameEs;
        DescriptionEs = descriptionEs;
        BlurRadius = blurRadius;
        FlyoutOpacity = flyoutOpacity;
        HomeOpacity = homeOpacity;
        Scale = scale;
        MaskKind = maskKind;
    }
}

public sealed class MediaBackdropSurfacePreset
{
    public double BlurRadius { get; }
    public double Opacity { get; }
    public double Scale { get; }
    public Brush? OpacityMask { get; }

    public MediaBackdropSurfacePreset(double blurRadius, double opacity, double scale, Brush? opacityMask)
    {
        BlurRadius = blurRadius;
        Opacity = opacity;
        Scale = scale;
        OpacityMask = opacityMask;
    }
}

public static class MediaBackdropStyleService
{
    private const int RenderWidth = 720;
    private const int RenderHeight = 420;
    private const int TaskbarRenderWidth = 360;
    private const int TaskbarRenderHeight = 120;

    private static readonly IReadOnlyDictionary<int, MediaBackdropStyleDefinition> Presets;
    private static readonly Dictionary<string, ImageSource?> ProcessedSourceCache = [];
    private static readonly Dictionary<Wpf.Ui.Appearance.ApplicationTheme, ImageSource> FallbackImageCache = [];

    static MediaBackdropStyleService()
    {
        Presets = new Dictionary<int, MediaBackdropStyleDefinition>
        {
            [0] = new(
                0,
                "Ninguno",
                "Sin fondo blur.",
                0,
                0.0,
                0.0,
                1.0,
                MediaBackdropMaskKind.None),
            [1] = new(
                1,
                "Resplandor de Portada",
                "Destello radial centrado/desplazado que emula la luz trasera de la caratula.",
                40,
                1.0,
                0.7,
                1.0,
                MediaBackdropMaskKind.None),
            [2] = new(
                2,
                "Resplandor Ascendente",
                "Degradado de luz suave ascendente desde la parte inferior.",
                40,
                0.8,
                0.5,
                1.0,
                MediaBackdropMaskKind.None),
            [3] = new(
                3,
                "Desenfoque Suave",
                "Desenfoque uniforme en toda la superficie de la ventana.",
                60,
                0.45,
                0.35,
                1.08,
                MediaBackdropMaskKind.None)
        };
    }

    private readonly record struct BackdropColors(Color Primary, Color Secondary, Color Accent);

    public static MediaBackdropStyleDefinition GetDefinition(int optionIndex)
    {
        return Presets.TryGetValue(optionIndex, out var preset)
            ? preset
            : Presets[0];
    }

    public static MediaBackdropSurfacePreset ResolveSurfacePreset(int optionIndex, MediaBackdropSurface surface)
    {
        var definition = GetDefinition(optionIndex);
        if (definition.Id == 0)
        {
            return new MediaBackdropSurfacePreset(0, 0, 1, null);
        }

        double opacity = surface switch
        {
            MediaBackdropSurface.Home => definition.HomeOpacity,
            MediaBackdropSurface.Taskbar => Math.Min(definition.HomeOpacity, definition.FlyoutOpacity) * 0.75,
            _ => definition.FlyoutOpacity
        };

        double blurRadius = surface switch
        {
            MediaBackdropSurface.Taskbar => Math.Min(15, definition.BlurRadius * 0.25),
            MediaBackdropSurface.Flyout => Math.Min(35, definition.BlurRadius * 0.6),
            _ => definition.BlurRadius
        };

        double scale = surface switch
        {
            MediaBackdropSurface.Taskbar => 1.08 + ((definition.Scale - 1.0) * 0.35),
            _ => definition.Scale
        };

        return new MediaBackdropSurfacePreset(
            blurRadius,
            opacity,
            scale,
            GetOpacityMask(definition.MaskKind));
    }

    public static void ApplyPresetToImage(
        Image target,
        int optionIndex,
        MediaBackdropSurface surface,
        ImageSource? imageSource)
    {
        if (target != null && optionIndex != 0)
        {
            target.Source = CreateBackdropImageSource(
                optionIndex,
                surface,
                imageSource,
                target.ActualWidth,
                target.ActualHeight);
        }

        ApplyPresetToImageStyle(target, optionIndex, surface);
    }

    public static ImageSource? CreateBackdropImageSource(
        int optionIndex,
        MediaBackdropSurface surface,
        ImageSource? imageSource)
        => CreateBackdropImageSource(optionIndex, surface, imageSource, 0, 0);

    public static ImageSource? CreateBackdropImageSource(
        int optionIndex,
        MediaBackdropSurface surface,
        ImageSource? imageSource,
        double targetWidth,
        double targetHeight)
    {
        var definition = GetDefinition(optionIndex);
        if (definition.Id == 0)
        {
            return null;
        }

        if (imageSource == null)
        {
            return GetFallbackImageSource();
        }

        bool isDownloading = imageSource is BitmapImage bi && bi.IsDownloading;
        if (isDownloading)
        {
            return GetFallbackImageSource();
        }

        if (definition.Id == 3)
        {
            return imageSource;
        }

        var (width, height) = ResolveRenderSize(surface, targetWidth, targetHeight);
        string cacheKey = $"{definition.Id}:{surface}:{imageSource.GetHashCode()}:{Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme()}:{width}x{height}";
        if (ProcessedSourceCache.TryGetValue(cacheKey, out var cachedSource))
        {
            return cachedSource;
        }

        var source = definition.Id switch
        {
            1 => CreateCoverGlowImageSource(imageSource, surface, width, height),
            2 => CreateRisingGlowImageSource(imageSource, surface, width, height),
            _ => imageSource
        };

        if (ProcessedSourceCache.Count > 24)
        {
            ProcessedSourceCache.Clear();
        }

        ProcessedSourceCache[cacheKey] = source;
        return source;
    }

    public static void ApplyPresetToImageStyle(
        Image? target,
        int optionIndex,
        MediaBackdropSurface surface)
    {
        if (target == null)
        {
            return;
        }

        target.HorizontalAlignment = HorizontalAlignment.Stretch;
        target.VerticalAlignment = VerticalAlignment.Stretch;
        target.RenderTransformOrigin = new Point(0.5, 0.5);

        var preset = ResolveSurfacePreset(optionIndex, surface);
        if (optionIndex == 0)
        {
            target.Visibility = Visibility.Collapsed;
            target.Opacity = 0.0;
            target.OpacityMask = null;
            target.RenderTransform = Transform.Identity;
            SetBlurRadius(target, 0);
            return;
        }

        target.Visibility = Visibility.Visible;
        target.Opacity = preset.Opacity;
        target.RenderTransform = new ScaleTransform(preset.Scale, preset.Scale);
        target.OpacityMask = preset.OpacityMask;
        SetBlurRadius(target, preset.BlurRadius);
    }

    private static void DrawImageUniformToFill(DrawingContext dc, ImageSource source, Rect targetRect, double opacity)
    {
        var brush = new ImageBrush(source)
        {
            Stretch = Stretch.UniformToFill
        };
        brush.Freeze();
        dc.PushOpacity(opacity);
        dc.DrawRectangle(brush, null, targetRect);
        dc.Pop();
    }

    private static ImageSource CreateCoverGlowImageSource(ImageSource? imageSource, MediaBackdropSurface surface, int width, int height)
    {
        var colors = ExtractBackdropColors(imageSource);
        var center = surface switch
        {
            MediaBackdropSurface.Taskbar => new Point(width * 0.50, height * 0.52),
            MediaBackdropSurface.Home => new Point(width * 0.28, height * 0.54),
            _ => new Point(width * 0.18, height * 0.52)
        };

        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            DrawBackdropBase(dc, imageSource, colors, width, height);
            DrawRadialGlow(dc, center, width * 0.72, height * 0.62, colors.Primary, 185, 92);
            DrawRadialGlow(dc, new Point(center.X + width * 0.20, center.Y - height * 0.08), width * 0.46, height * 0.38, colors.Secondary, 120, 52);
            DrawRadialGlow(dc, new Point(center.X + width * 0.05, center.Y + height * 0.14), width * 0.34, height * 0.28, colors.Accent, 90, 38);
        }

        return RenderVisual(drawingVisual, width, height);
    }

    private static ImageSource CreateRisingGlowImageSource(ImageSource? imageSource, MediaBackdropSurface surface, int width, int height)
    {
        var colors = ExtractBackdropColors(imageSource);
        var drawingVisual = new DrawingVisual();

        using (var dc = drawingVisual.RenderOpen())
        {
            DrawBackdropBase(dc, imageSource, colors, width, height);

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 1.0),
                EndPoint = new Point(0.5, 0.0)
            };
            brush.GradientStops.Add(new GradientStop(WithAlpha(colors.Primary, 195), 0.0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(colors.Secondary, 138), 0.26));
            brush.GradientStops.Add(new GradientStop(WithAlpha(colors.Accent, 74), 0.56));
            brush.GradientStops.Add(new GradientStop(WithAlpha(colors.Accent, 0), 1.0));
            brush.Freeze();

            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            DrawRadialGlow(dc, new Point(width * 0.5, height * 0.98), width * 0.65, height * 0.46, colors.Primary, 120, 0);
        }

        return RenderVisual(drawingVisual, width, height);
    }

    private static void DrawBackdropBase(DrawingContext dc, ImageSource? imageSource, BackdropColors colors, int width, int height)
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        var baseColor = theme == Wpf.Ui.Appearance.ApplicationTheme.Dark
            ? Color.FromRgb(21, 21, 25)
            : Color.FromRgb(238, 238, 244);

        var baseBrush = new SolidColorBrush(baseColor);
        baseBrush.Freeze();
        dc.DrawRectangle(baseBrush, null, new Rect(0, 0, width, height));

        if (imageSource != null)
        {
            DrawImageUniformToFill(dc, imageSource, new Rect(0, 0, width, height), 0.18);
        }

        var tint = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        tint.GradientStops.Add(new GradientStop(WithAlpha(colors.Primary, theme == Wpf.Ui.Appearance.ApplicationTheme.Dark ? (byte)80 : (byte)54), 0.0));
        tint.GradientStops.Add(new GradientStop(WithAlpha(colors.Secondary, theme == Wpf.Ui.Appearance.ApplicationTheme.Dark ? (byte)62 : (byte)42), 0.52));
        tint.GradientStops.Add(new GradientStop(WithAlpha(colors.Accent, theme == Wpf.Ui.Appearance.ApplicationTheme.Dark ? (byte)48 : (byte)34), 1.0));
        tint.Freeze();

        dc.DrawRectangle(tint, null, new Rect(0, 0, width, height));
    }

    private static void DrawRadialGlow(DrawingContext dc, Point center, double radiusX, double radiusY, Color color, byte centerAlpha, byte midAlpha)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, centerAlpha), 0.0));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, midAlpha), 0.58));
        brush.GradientStops.Add(new GradientStop(WithAlpha(color, 0), 1.0));
        brush.Freeze();

        dc.DrawEllipse(brush, null, center, radiusX, radiusY);
    }

    private static ImageSource RenderVisual(DrawingVisual drawingVisual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawingVisual);
        bitmap.Freeze();
        return bitmap;
    }

    private static (int Width, int Height) ResolveRenderSize(MediaBackdropSurface surface, double targetWidth, double targetHeight)
    {
        int fallbackWidth = surface == MediaBackdropSurface.Taskbar ? TaskbarRenderWidth : RenderWidth;
        int fallbackHeight = surface == MediaBackdropSurface.Taskbar ? TaskbarRenderHeight : RenderHeight;

        if (targetWidth <= 0 || targetHeight <= 0 || double.IsNaN(targetWidth) || double.IsNaN(targetHeight))
        {
            return (fallbackWidth, fallbackHeight);
        }

        int width = Math.Clamp((int)Math.Ceiling(targetWidth), 16, 4096);
        int height = Math.Clamp((int)Math.Ceiling(targetHeight), 16, 4096);

        return (QuantizeSize(width), QuantizeSize(height));
    }

    private static int QuantizeSize(int value)
        => Math.Max(16, (int)Math.Ceiling(value / 16.0) * 16);

    private static Dictionary<int, (double Weight, double R, double G, double B)> CollectSwatches(
        byte[] pixels,
        int width,
        int height,
        double centerX,
        double centerY,
        double maxDistance,
        int stepX,
        int stepY,
        bool flexible)
    {
        var swatches = new Dictionary<int, (double Weight, double R, double G, double B)>();

        for (int y = 0; y < height; y += stepY)
        {
            for (int x = 0; x < width; x += stepX)
            {
                int pixelIndex = ((y * width) + x) * 4;
                if (pixelIndex + 3 >= pixels.Length)
                {
                    continue;
                }
                byte b = pixels[pixelIndex];
                byte g = pixels[pixelIndex + 1];
                byte r = pixels[pixelIndex + 2];
                byte a = pixels[pixelIndex + 3];
                if (a < 150)
                {
                    continue;
                }

                var metrics = AnalyzeColor(r, g, b);
                if (flexible)
                {
                    if (metrics.Lightness < 0.015 || metrics.Lightness > 0.99)
                    {
                        continue;
                    }
                }
                else
                {
                    if (metrics.Chroma < 0.06 || metrics.Lightness < 0.06 || metrics.Lightness > 0.96)
                    {
                        continue;
                    }
                }

                int key = Quantize(r, 48) << 16 | Quantize(g, 48) << 8 | Quantize(b, 48);
                double distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                double centerWeight = 1.0 - (distance / Math.Max(1.0, maxDistance));
                double weight = 0.55 + (metrics.Chroma * 2.2) + (centerWeight * 0.65);

                if (!swatches.TryGetValue(key, out var existing))
                {
                    existing = default;
                }

                swatches[key] = (
                    existing.Weight + weight,
                    existing.R + (r * weight),
                    existing.G + (g * weight),
                    existing.B + (b * weight));
            }
        }

        return swatches;
    }

    private static BackdropColors ExtractBackdropColors(ImageSource? imageSource)
    {
        if (imageSource is not BitmapSource bitmapSource)
        {
            return GetFallbackColors();
        }

        try
        {
            BitmapSource finalSource = bitmapSource;
            if (bitmapSource.PixelWidth > 64 || bitmapSource.PixelHeight > 64)
            {
                double scaleX = 64.0 / bitmapSource.PixelWidth;
                double scaleY = 64.0 / bitmapSource.PixelHeight;
                var scaleTransform = new ScaleTransform(scaleX, scaleY);
                scaleTransform.Freeze();
                var transformed = new TransformedBitmap(bitmapSource, scaleTransform);
                transformed.Freeze();
                finalSource = transformed;
            }

            var formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = finalSource;
            formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
            formattedBitmap.EndInit();

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[Math.Max(0, height * stride)];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            int stepX = Math.Max(1, width / 56);
            int stepY = Math.Max(1, height / 56);
            double centerX = Math.Max(1, (width - 1) / 2.0);
            double centerY = Math.Max(1, (height - 1) / 2.0);
            double maxDistance = Math.Sqrt((centerX * centerX) + (centerY * centerY));

            // Primera pasada: Estricta
            var swatches = CollectSwatches(pixels, width, height, centerX, centerY, maxDistance, stepX, stepY, flexible: false);

            var colors = swatches.Values
                .Where(s => s.Weight > 0)
                .OrderByDescending(s => s.Weight)
                .Take(5)
                .Select(s => NormalizeBackdropColor(Color.FromRgb(
                    (byte)Math.Clamp(s.R / s.Weight, 0, 255),
                    (byte)Math.Clamp(s.G / s.Weight, 0, 255),
                    (byte)Math.Clamp(s.B / s.Weight, 0, 255))))
                .ToList();

            // Segunda pasada: Flexible (si no hay colores válidos)
            if (colors.Count == 0)
            {
                swatches = CollectSwatches(pixels, width, height, centerX, centerY, maxDistance, stepX, stepY, flexible: true);
                colors = swatches.Values
                    .Where(s => s.Weight > 0)
                    .OrderByDescending(s => s.Weight)
                    .Take(5)
                    .Select(s => NormalizeBackdropColor(Color.FromRgb(
                        (byte)Math.Clamp(s.R / s.Weight, 0, 255),
                        (byte)Math.Clamp(s.G / s.Weight, 0, 255),
                        (byte)Math.Clamp(s.B / s.Weight, 0, 255))))
                    .ToList();
            }

            if (colors.Count == 0)
            {
                return GetFallbackColors();
            }

            var primary = colors[0];
            var primaryMetrics = AnalyzeColor(primary.R, primary.G, primary.B);
            bool isMonochromatic = primaryMetrics.Chroma < 0.06;

            var secondary = colors.Skip(1).OrderByDescending(c => ColorDistance(primary, c)).FirstOrDefault();
            if (secondary == default)
            {
                secondary = isMonochromatic
                    ? ScaleColor(primary, primaryMetrics.Lightness > 0.5 ? 0.75 : 1.3, (byte)(primaryMetrics.Lightness > 0.5 ? 0 : 10))
                    : ShiftHue(primary, 42);
            }

            var accent = colors.Skip(1).OrderByDescending(c => ColorDistance(secondary, c)).FirstOrDefault();
            if (accent == default)
            {
                accent = isMonochromatic
                    ? ScaleColor(primary, primaryMetrics.Lightness > 0.5 ? 0.5 : 1.6, (byte)(primaryMetrics.Lightness > 0.5 ? 0 : 20))
                    : ShiftHue(primary, -34);
            }

            return new BackdropColors(primary, secondary, accent);
        }
        catch
        {
            return GetFallbackColors();
        }
    }

    private static (double Chroma, double Lightness) AnalyzeColor(byte r, byte g, byte b)
    {
        double rn = r / 255.0;
        double gn = g / 255.0;
        double bn = b / 255.0;
        double max = Math.Max(rn, Math.Max(gn, bn));
        double min = Math.Min(rn, Math.Min(gn, bn));
        return (max - min, (max + min) / 2.0);
    }

    private static int Quantize(byte value, int bucketSize)
        => Math.Min(255, (value / bucketSize) * bucketSize);

    private static Color NormalizeBackdropColor(Color color)
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        return theme == Wpf.Ui.Appearance.ApplicationTheme.Dark
            ? ScaleColor(color, 1.16, 18)
            : ScaleColor(color, 0.82, 0);
    }

    private static Color ScaleColor(Color color, double multiplier, byte lift)
        => Color.FromRgb(
            (byte)Math.Clamp((color.R * multiplier) + lift, 0, 255),
            (byte)Math.Clamp((color.G * multiplier) + lift, 0, 255),
            (byte)Math.Clamp((color.B * multiplier) + lift, 0, 255));

    private static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static double ColorDistance(Color a, Color b)
        => Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    private static Color ShiftHue(Color color, double degrees)
    {
        double hue = (GetHue(color) + degrees + 360) % 360;
        double saturation = Math.Max(0.45, GetHslSaturation(color));
        double lightness = Math.Clamp(GetHslLightness(color), 0.34, 0.74);
        return FromHsl(hue, saturation, lightness);
    }

    private static double GetHue(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        if (delta == 0)
        {
            return 0;
        }

        double hue = max switch
        {
            var v when v == r => 60 * (((g - b) / delta) % 6),
            var v when v == g => 60 * (((b - r) / delta) + 2),
            _ => 60 * (((r - g) / delta) + 4)
        };

        return hue < 0 ? hue + 360 : hue;
    }

    private static double GetHslSaturation(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double lightness = (max + min) / 2.0;
        double delta = max - min;

        return delta == 0
            ? 0
            : delta / (1 - Math.Abs((2 * lightness) - 1));
    }

    private static double GetHslLightness(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        return (Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b))) / 2.0;
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        double h = hue / 60.0;
        double x = chroma * (1 - Math.Abs((h % 2) - 1));
        (double r1, double g1, double b1) = h switch
        {
            >= 0 and < 1 => (chroma, x, 0d),
            >= 1 and < 2 => (x, chroma, 0d),
            >= 2 and < 3 => (0d, chroma, x),
            >= 3 and < 4 => (0d, x, chroma),
            >= 4 and < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        double m = lightness - (chroma / 2);
        return Color.FromRgb(
            (byte)Math.Clamp((r1 + m) * 255, 0, 255),
            (byte)Math.Clamp((g1 + m) * 255, 0, 255),
            (byte)Math.Clamp((b1 + m) * 255, 0, 255));
    }

    private static BackdropColors GetFallbackColors()
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        return theme == Wpf.Ui.Appearance.ApplicationTheme.Dark
            ? new BackdropColors(Color.FromRgb(70, 122, 190), Color.FromRgb(154, 76, 190), Color.FromRgb(48, 170, 158))
            : new BackdropColors(Color.FromRgb(70, 116, 176), Color.FromRgb(132, 80, 172), Color.FromRgb(50, 145, 140));
    }

    private static void SetBlurRadius(Image target, double blurRadius)
    {
        if (target.Effect is BlurEffect blurEffect)
        {
            blurEffect.Radius = blurRadius;
        }
        else
        {
            target.Effect = new BlurEffect { Radius = blurRadius };
        }
    }

    private static Brush? GetOpacityMask(MediaBackdropMaskKind maskKind)
    {
        return null;
    }

    public static Brush GetFallbackBrush()
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };

        if (theme == Wpf.Ui.Appearance.ApplicationTheme.Dark)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 35), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(15, 15, 18), 1.0));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(240, 240, 245), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(220, 220, 225), 1.0));
        }

        brush.Freeze();
        return brush;
    }

    public static ImageSource? GetFallbackImageSource()
    {
        try
        {
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            if (FallbackImageCache.TryGetValue(theme, out var cached))
            {
                return cached;
            }

            var brush = GetFallbackBrush();
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                drawingContext.DrawRectangle(brush, null, new Rect(0, 0, 400, 400));
            }

            var renderTargetBitmap = new RenderTargetBitmap(
                400, 400, 96, 96, PixelFormats.Pbgra32);
            renderTargetBitmap.Render(drawingVisual);
            renderTargetBitmap.Freeze();

            FallbackImageCache[theme] = renderTargetBitmap;
            return renderTargetBitmap;
        }
        catch
        {
            return null;
        }
    }
}
