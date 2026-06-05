using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Utils;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FluentFlyoutWPF.Controls;

public partial class ColorPickerControl : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty SelectedColorHexProperty =
        DependencyProperty.Register(
            nameof(SelectedColorHex),
            typeof(string),
            typeof(ColorPickerControl),
            new FrameworkPropertyMetadata(
                "#808080",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorHexChanged));

    private bool _isUpdatingFromHex;
    private bool _isUpdatingFromInteraction;
    private bool _isDraggingSaturationValue;
    private bool _isDraggingHue;
    private bool _isDraggingAlpha;
    private double _hue;
    private double _saturation;
    private double _value = 1;
    private double _alpha = 255;
    private string _localColorHex = "#808080";

    public string LocalColorHex
    {
        get => _localColorHex;
        set
        {
            if (_localColorHex != value)
            {
                _localColorHex = value;
                OnPropertyChanged(nameof(LocalColorHex));
                ApplyHexToState(value);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ColorPickerControl()
    {
        InitializeComponent();
        Loaded += ColorPickerControl_Loaded;
        Unloaded += ColorPickerControl_Unloaded;
        IsVisibleChanged += ColorPickerControl_IsVisibleChanged;
    }

    public Brush PickerSurfaceSecondaryBrush => GetThemeBrush("SolidBackgroundFillColorBaseBrush", Color.FromRgb(32, 32, 32));

    public Brush PickerSurfaceBrush => GetThemeBrush("ControlFillColorDefaultBrush", Color.FromRgb(44, 44, 44));

    public Brush PickerSurfaceMutedBrush => GetThemeBrush("ControlFillColorTertiaryBrush", Color.FromRgb(72, 72, 72));

    public Brush PickerCardBorderBrush => GetThemeBrush("CardStrokeColorDefaultBrush", Color.FromArgb(64, 255, 255, 255));

    public Brush PickerBorderBrush => GetThemeBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(80, 255, 255, 255));

    public Brush PickerStrongBorderBrush => GetThemeBrush("ControlStrongStrokeColorDefaultBrush", Color.FromArgb(110, 255, 255, 255));

    public Brush PickerTextPrimaryBrush => GetThemeBrush("TextFillColorPrimaryBrush", Colors.White);

    public Brush PickerTextSecondaryBrush => GetThemeBrush("TextFillColorSecondaryBrush", Color.FromArgb(210, 255, 255, 255));

    public Brush CheckerPatternBrush => CreateCheckerPatternBrush();

    public string SelectedColorHex
    {
        get => (string)GetValue(SelectedColorHexProperty);
        set => SetValue(SelectedColorHexProperty, value);
    }

    public double Hue
    {
        get => _hue;
        set
        {
            if (SetField(ref _hue, Math.Clamp(value, 0, 360), nameof(Hue)))
            {
                NotifyBrushesChanged();
            }
        }
    }

    public double Saturation
    {
        get => _saturation;
        set
        {
            if (SetField(ref _saturation, Math.Clamp(value, 0, 1), nameof(Saturation)))
            {
                NotifyBrushesChanged();
            }
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, Math.Clamp(value, 0, 1), nameof(Value)))
            {
                NotifyBrushesChanged();
            }
        }
    }

    public double Alpha
    {
        get => _alpha;
        set
        {
            double clamped = Math.Clamp(value, 0, 255);
            if (Math.Abs(_alpha - clamped) > 0.001)
            {
                _alpha = clamped;
                OnPropertyChanged(nameof(Alpha));
                OnPropertyChanged(nameof(A));
                NotifyBrushesChanged();
                UpdateAlphaThumb();
                UpdateLocalColorHexFromState();
            }
        }
    }

    public byte A
    {
        get => (byte)Math.Round(Alpha);
        set
        {
            if (A != value)
            {
                Alpha = value;
            }
        }
    }

    public byte R
    {
        get => GetCurrentColor().R;
        set
        {
            var currentColor = GetCurrentColor();
            if (currentColor.R != value)
            {
                var newColor = Color.FromArgb(currentColor.A, value, currentColor.G, currentColor.B);
                UpdateStateFromColor(newColor);
            }
        }
    }

    public byte G
    {
        get => GetCurrentColor().G;
        set
        {
            var currentColor = GetCurrentColor();
            if (currentColor.G != value)
            {
                var newColor = Color.FromArgb(currentColor.A, currentColor.R, value, currentColor.B);
                UpdateStateFromColor(newColor);
            }
        }
    }

    public byte B
    {
        get => GetCurrentColor().B;
        set
        {
            var currentColor = GetCurrentColor();
            if (currentColor.B != value)
            {
                var newColor = Color.FromArgb(currentColor.A, currentColor.R, currentColor.G, value);
                UpdateStateFromColor(newColor);
            }
        }
    }

    public SolidColorBrush SelectedColorBrush => CreateBrush(GetCurrentColor());

    public SolidColorBrush HuePreviewBrush => CreateBrush(ColorFromHsv(Hue, 1, 1));

    public LinearGradientBrush AlphaGradientBrush
    {
        get
        {
            var opaqueColor = GetCurrentColor(includeAlpha: false);
            var transparentColor = Color.FromArgb(0, opaqueColor.R, opaqueColor.G, opaqueColor.B);
            return new LinearGradientBrush(
                [
                    new GradientStop(transparentColor, 0),
                    new GradientStop(Color.FromArgb(255, opaqueColor.R, opaqueColor.G, opaqueColor.B), 1)
                ],
                new Point(0.5, 1),
                new Point(0.5, 0));
        }
    }

    private void ColorPickerControl_Loaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        SyncLocalStateFromSelectedColor();
        RefreshThemeResources();
        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, static (recipient, message) =>
        {
            if (recipient is ColorPickerControl control)
            {
                _ = control.Dispatcher.InvokeAsync(control.RefreshThemeResources);
            }
        });
    }

    private void ColorPickerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void ColorPickerControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            SyncLocalStateFromSelectedColor();
        }
    }

    private static void OnSelectedColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ColorPickerControl control || control._isUpdatingFromInteraction)
            return;

        control.SyncLocalStateFromSelectedColor();
    }

    private void ApplyHexToState(string? hex)
    {
        if (_isUpdatingFromHex)
            return;

        if (!AccentColorResolver.TryParseCustomAccent(hex, out var brush) || brush == null)
            return;

        _isUpdatingFromHex = true;
        try
        {
            var color = brush.Color;
            Alpha = color.A;
            RgbToHsv(color, out var hue, out var saturation, out var value);
            _hue = hue;
            _saturation = saturation;
            _value = value;
            RaiseStateChanged();
            UpdateInteractionThumbs();
        }
        catch (Exception)
        {
            // Fail-safe catch
        }
        finally
        {
            _isUpdatingFromHex = false;
        }
    }

    private void SaturationValueArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSaturationValue = true;
        SaturationValueArea.CaptureMouse();
        UpdateSaturationValueFromPoint(e.GetPosition(SaturationValueArea));
    }

    private void SaturationValueArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSaturationValue)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDraggingInteractions();
            return;
        }

        UpdateSaturationValueFromPoint(e.GetPosition(SaturationValueArea));
    }

    private void HueArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        HueArea.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueArea));
    }

    private void HueArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingHue)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDraggingInteractions();
            return;
        }

        UpdateHueFromPoint(e.GetPosition(HueArea));
    }

    private void AlphaArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingAlpha = true;
        AlphaArea.CaptureMouse();
        UpdateAlphaFromPoint(e.GetPosition(AlphaArea));
    }

    private void AlphaArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingAlpha)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            StopDraggingInteractions();
            return;
        }

        UpdateAlphaFromPoint(e.GetPosition(AlphaArea));
    }

    private void InteractionSurface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        StopDraggingInteractions();
    }

    private void InteractionSurface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        StopDraggingInteractions();
    }

    private void SaturationValueArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSaturationValueThumb();
    }

    private void HueArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateHueThumb();
    }

    private void AlphaArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAlphaThumb();
    }

    private void UpdateSaturationValueFromPoint(Point point)
    {
        if (SaturationValueArea.ActualWidth <= 0 || SaturationValueArea.ActualHeight <= 0)
            return;

        Saturation = point.X / SaturationValueArea.ActualWidth;
        Value = 1 - (point.Y / SaturationValueArea.ActualHeight);
        UpdateSaturationValueThumb();
        UpdateLocalColorHexFromState();
    }

    private void UpdateHueFromPoint(Point point)
    {
        if (HueArea.ActualHeight <= 0)
            return;

        double normalized = 1 - (point.Y / HueArea.ActualHeight);
        Hue = Math.Clamp(normalized, 0, 1) * 360;
        UpdateHueThumb();
        UpdateLocalColorHexFromState();
    }

    private void UpdateAlphaFromPoint(Point point)
    {
        if (AlphaArea.ActualHeight <= 0)
            return;

        double normalized = 1 - (point.Y / AlphaArea.ActualHeight);
        Alpha = Math.Clamp(normalized, 0, 1) * 255;
        UpdateAlphaThumb();
        UpdateLocalColorHexFromState();
    }

    private void UpdateSaturationValueThumb()
    {
        if (SaturationValueThumb == null || SaturationValueArea == null || SaturationValueArea.ActualWidth <= 0 || SaturationValueArea.ActualHeight <= 0)
            return;

        double x = Saturation * SaturationValueArea.ActualWidth;
        double y = (1 - Value) * SaturationValueArea.ActualHeight;

        SaturationValueThumb.HorizontalAlignment = HorizontalAlignment.Left;
        SaturationValueThumb.VerticalAlignment = VerticalAlignment.Top;
        SaturationValueThumb.Margin = new Thickness(x - 8, y - 8, 0, 0);
    }

    private void UpdateHueThumb()
    {
        if (HueThumb == null || HueArea == null || HueArea.ActualHeight <= 0)
            return;

        double y = (1 - (Hue / 360d)) * HueArea.ActualHeight;
        HueThumb.Margin = new Thickness(0, y - 4, 0, 0);
    }

    private void UpdateAlphaThumb()
    {
        if (AlphaThumb == null || AlphaArea == null || AlphaArea.ActualHeight <= 0)
            return;

        double y = (1 - (Alpha / 255d)) * AlphaArea.ActualHeight;
        AlphaThumb.Margin = new Thickness(0, y - 4, 0, 0);
    }

    private void UpdateInteractionThumbs()
    {
        UpdateSaturationValueThumb();
        UpdateHueThumb();
        UpdateAlphaThumb();
    }

    private void UpdateLocalColorHexFromState()
    {
        if (_isUpdatingFromHex)
            return;

        _isUpdatingFromInteraction = true;
        try
        {
            var color = GetCurrentColor();
            _localColorHex = color.A >= 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            OnPropertyChanged(nameof(LocalColorHex));
            RaiseStateChanged();
            UpdateInteractionThumbs();
        }
        catch (Exception)
        {
            // Fail-safe catch
        }
        finally
        {
            _isUpdatingFromInteraction = false;
        }
    }

    private Color GetCurrentColor(bool includeAlpha = true)
    {
        var rgbColor = ColorFromHsv(Hue, Saturation, Value);
        return includeAlpha
            ? Color.FromArgb((byte)Math.Round(Alpha), rgbColor.R, rgbColor.G, rgbColor.B)
            : Color.FromArgb(255, rgbColor.R, rgbColor.G, rgbColor.B);
    }

    private void NotifyBrushesChanged()
    {
        OnPropertyChanged(nameof(SelectedColorBrush));
        OnPropertyChanged(nameof(HuePreviewBrush));
        OnPropertyChanged(nameof(AlphaGradientBrush));
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(Hue));
        OnPropertyChanged(nameof(Saturation));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Alpha));
        OnPropertyChanged(nameof(A));
        OnPropertyChanged(nameof(R));
        OnPropertyChanged(nameof(G));
        OnPropertyChanged(nameof(B));
        NotifyBrushesChanged();
    }

    private void UpdateStateFromColor(Color color)
    {
        if (_isUpdatingFromHex)
            return;

        _isUpdatingFromInteraction = true;
        try
        {
            _alpha = color.A;
            RgbToHsv(color, out var hue, out var saturation, out var value);
            _hue = hue;
            _saturation = saturation;
            _value = value;
            RaiseStateChanged();
            UpdateInteractionThumbs();

            // Also update local color hex
            _localColorHex = color.A >= 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            OnPropertyChanged(nameof(LocalColorHex));
        }
        catch (Exception)
        {
            // Fail-safe
        }
        finally
        {
            _isUpdatingFromInteraction = false;
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string hex)
        {
            LocalColorHex = hex;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedColorHex = LocalColorHex;
        TryCloseContainingPopup();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SyncLocalStateFromSelectedColor();
        TryCloseContainingPopup();
    }

    private void SyncLocalStateFromSelectedColor()
    {
        _localColorHex = SelectedColorHex ?? "#808080";
        OnPropertyChanged(nameof(LocalColorHex));
        ApplyHexToState(_localColorHex);
    }

    private void TryCloseContainingPopup()
    {
        if (Parent is Popup directPopup)
        {
            directPopup.IsOpen = false;
            return;
        }

        DependencyObject? current = this;
        while (current != null)
        {
            if (current is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void StopDraggingInteractions()
    {
        _isDraggingSaturationValue = false;
        _isDraggingHue = false;
        _isDraggingAlpha = false;

        if (SaturationValueArea.IsMouseCaptured)
        {
            SaturationValueArea.ReleaseMouseCapture();
        }

        if (HueArea.IsMouseCaptured)
        {
            HueArea.ReleaseMouseCapture();
        }

        if (AlphaArea.IsMouseCaptured)
        {
            AlphaArea.ReleaseMouseCapture();
        }
    }

    private bool SetField(ref double field, double value, string propertyName)
    {
        if (Math.Abs(field - value) < 0.001)
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void RefreshThemeResources()
    {
        OnPropertyChanged(nameof(PickerSurfaceSecondaryBrush));
        OnPropertyChanged(nameof(PickerSurfaceBrush));
        OnPropertyChanged(nameof(PickerSurfaceMutedBrush));
        OnPropertyChanged(nameof(PickerCardBorderBrush));
        OnPropertyChanged(nameof(PickerBorderBrush));
        OnPropertyChanged(nameof(PickerStrongBorderBrush));
        OnPropertyChanged(nameof(PickerTextPrimaryBrush));
        OnPropertyChanged(nameof(PickerTextSecondaryBrush));
        OnPropertyChanged(nameof(CheckerPatternBrush));
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Brush GetThemeBrush(string resourceKey, Color fallbackColor)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        return CreateBrush(fallbackColor);
    }

    private Brush CreateCheckerPatternBrush()
    {
        var baseBrush = PickerSurfaceBrush;
        var accentBrush = PickerSurfaceMutedBrush;

        var drawingGroup = new DrawingGroup();
        drawingGroup.Children.Add(new GeometryDrawing(baseBrush, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        drawingGroup.Children.Add(new GeometryDrawing(accentBrush, null, new RectangleGeometry(new Rect(0, 0, 6, 6))));
        drawingGroup.Children.Add(new GeometryDrawing(accentBrush, null, new RectangleGeometry(new Rect(6, 6, 6, 6))));

        var brush = new DrawingBrush(drawingGroup)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 12, 12),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 12, 12),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        hue = hue % 360;
        if (hue < 0)
            hue += 360;

        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs((hue / 60d % 2) - 1));
        double m = value - chroma;

        (double r, double g, double b) = hue switch
        {
            >= 0 and < 60 => (chroma, x, 0d),
            >= 60 and < 120 => (x, chroma, 0d),
            >= 120 and < 180 => (0d, chroma, x),
            >= 180 and < 240 => (0d, x, chroma),
            >= 240 and < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        hue = 0;
        if (delta > 1e-5)
        {
            if (r >= g && r >= b)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (g >= r && g >= b)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
            hue += 360;

        value = max;
        saturation = max <= 0 ? 0 : delta / max;
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
