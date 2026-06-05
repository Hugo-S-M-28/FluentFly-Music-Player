using System;
using System.Collections.Generic;
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
    private static readonly IReadOnlyDictionary<int, MediaBackdropStyleDefinition> Presets;
    private static readonly RadialGradientBrush CoverGlowMask;
    private static readonly RadialGradientBrush RisingGlowMask;

    static MediaBackdropStyleService()
    {
        CoverGlowMask = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.2, 0.5),
            Center = new Point(0.12, 0.5),
            RadiusX = 0.72,
            RadiusY = 0.38
        };
        CoverGlowMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.0));
        CoverGlowMask.GradientStops.Add(new GradientStop(Color.FromArgb(156, 0, 0, 0), 0.62));
        CoverGlowMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
        CoverGlowMask.Freeze();

        RisingGlowMask = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 1.0),
            Center = new Point(0.5, 1.0),
            RadiusX = 0.62,
            RadiusY = 0.95
        };
        RisingGlowMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.0));
        RisingGlowMask.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0, 0, 0), 0.55));
        RisingGlowMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
        RisingGlowMask.Freeze();

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
                50,
                1.0,
                0.7,
                1.2,
                MediaBackdropMaskKind.CoverGlow),
            [2] = new(
                2,
                "Resplandor Ascendente",
                "Degradado de luz suave ascendente desde la parte inferior.",
                200,
                0.8,
                0.5,
                1.8,
                MediaBackdropMaskKind.RisingGlow),
            [3] = new(
                3,
                "Desenfoque Suave",
                "Desenfoque uniforme en toda la superficie de la ventana.",
                60,
                0.45,
                0.35,
                1.25,
                MediaBackdropMaskKind.None)
        };
    }

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
            MediaBackdropSurface.Taskbar => Math.Max(18, definition.BlurRadius * 0.45),
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

        target.Source = imageSource ?? GetFallbackImageSource();
        target.Visibility = Visibility.Visible;
        target.Opacity = preset.Opacity;
        target.RenderTransform = new ScaleTransform(preset.Scale, preset.Scale);
        target.OpacityMask = preset.OpacityMask;
        SetBlurRadius(target, preset.BlurRadius);
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
        return maskKind switch
        {
            MediaBackdropMaskKind.CoverGlow => CoverGlowMask,
            MediaBackdropMaskKind.RisingGlow => RisingGlowMask,
            _ => null
        };
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
            return renderTargetBitmap;
        }
        catch
        {
            return null;
        }
    }
}
