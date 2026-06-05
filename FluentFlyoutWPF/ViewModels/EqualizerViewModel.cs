using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyoutWPF.Classes;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.ViewModels;

public partial class EqualizerViewModel : ObservableObject, IDisposable
{
    private readonly EqualizerService _equalizerService = EqualizerService.Instance;

    public ObservableCollection<EqualizerBandViewModel> Bands { get; } = [];
    public ObservableCollection<EqualizerPresetViewModel> Presets { get; } = [];

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private string activePresetName = "Normal";

    public EqualizerViewModel()
    {
        foreach (var band in _equalizerService.Bands)
        {
            Bands.Add(new EqualizerBandViewModel(band, OnBandGainChanged));
        }

        foreach (var preset in _equalizerService.Presets)
        {
            Presets.Add(new EqualizerPresetViewModel(preset));
        }

        SyncFromService();
        _equalizerService.PropertyChanged += EqualizerService_PropertyChanged;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_equalizerService.IsEnabled != value)
        {
            _equalizerService.IsEnabled = value;
        }
    }

    [RelayCommand]
    private void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        _equalizerService.ApplyPreset(presetName);
        SyncFromService();
    }

    private void OnBandGainChanged()
    {
        if (_equalizerService.ActivePresetName != "Custom")
        {
            _equalizerService.ActivePresetName = "Custom";
        }

        SyncPresetSelection();
    }

    private void EqualizerService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EqualizerService.IsEnabled) ||
            e.PropertyName == nameof(EqualizerService.ActivePresetName))
        {
            SyncFromService();
        }
    }

    private void SyncFromService()
    {
        IsEnabled = _equalizerService.IsEnabled;
        ActivePresetName = _equalizerService.ActivePresetName;

        foreach (var band in Bands)
        {
            band.SyncFromModel();
        }

        SyncPresetSelection();
    }

    private void SyncPresetSelection()
    {
        foreach (var preset in Presets)
        {
            preset.Appearance = preset.Name == ActivePresetName
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
        }
    }

    public void Dispose()
    {
        _equalizerService.PropertyChanged -= EqualizerService_PropertyChanged;
        foreach (var band in Bands.OfType<IDisposable>())
        {
            band.Dispose();
        }
    }
}

public partial class EqualizerBandViewModel : ObservableObject, IDisposable
{
    private readonly EqualizerBand _band;
    private readonly Action _onGainChanged;
    private bool _isSynchronizing;

    [ObservableProperty]
    private double gain;

    [ObservableProperty]
    private string gainLabel = "0";

    public string Label => _band.Label;

    public EqualizerBandViewModel(EqualizerBand band, Action onGainChanged)
    {
        _band = band;
        _onGainChanged = onGainChanged;
        _band.PropertyChanged += Band_PropertyChanged;
        SyncFromModel();
    }

    partial void OnGainChanged(double value)
    {
        GainLabel = FormatGain(value);

        if (_isSynchronizing)
        {
            return;
        }

        var rounded = (float)Math.Round(value, 1);
        if (Math.Abs(_band.Gain - rounded) > 0.01f)
        {
            _band.Gain = rounded;
            _onGainChanged();
        }
    }

    public void SyncFromModel()
    {
        _isSynchronizing = true;
        Gain = _band.Gain;
        GainLabel = FormatGain(_band.Gain);
        _isSynchronizing = false;
    }

    private void Band_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EqualizerBand.Gain))
        {
            SyncFromModel();
        }
    }

    private static string FormatGain(double value)
    {
        string sign = value >= 0 ? "+" : string.Empty;
        return $"{sign}{value:F0}";
    }

    public void Dispose()
    {
        _band.PropertyChanged -= Band_PropertyChanged;
    }
}

public partial class EqualizerPresetViewModel : ObservableObject
{
    public string Name { get; }
    public SymbolRegular IconSymbol { get; }

    [ObservableProperty]
    private ControlAppearance appearance = ControlAppearance.Secondary;

    public EqualizerPresetViewModel(EqualizerPreset preset)
    {
        Name = preset.Name;
        IconSymbol = Enum.TryParse<SymbolRegular>(preset.IconSymbol, out var symbol)
            ? symbol
            : SymbolRegular.Emoji24;
    }
}
