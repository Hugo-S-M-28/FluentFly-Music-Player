using System;
using System.Collections.Generic;
using System.ComponentModel;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// Defines a single equalizer frequency band with its center frequency, bandwidth, and gain.
/// </summary>
public class EqualizerBand : INotifyPropertyChanged
{
    private float _gain;

    public float Frequency { get; set; }
    public float Bandwidth { get; set; }
    public string Label { get; set; } = string.Empty;

    public float Gain
    {
        get => _gain;
        set
        {
            if (Math.Abs(_gain - value) > 0.01f)
            {
                _gain = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gain)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Defines an EQ preset with a name and gain values for each band.
/// </summary>
public class EqualizerPreset
{
    public string Name { get; set; } = string.Empty;
    public string IconSymbol { get; set; } = string.Empty;
    public float[] Gains { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Singleton service that manages the 10-band equalizer state and presets.
/// The actual audio processing is handled by EqualizerSampleProvider.
/// </summary>
public class EqualizerService : INotifyPropertyChanged
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static EqualizerService? _instance;
    private static readonly object _lock = new();

    public static EqualizerService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new EqualizerService();
                }
            }
            return _instance;
        }
    }

    private bool _isEnabled;
    private string _activePresetName = "Normal";
    private bool _isApplyingPreset;

    /// <summary>
    /// The 10 frequency bands of the equalizer (31Hz to 16kHz).
    /// </summary>
    public EqualizerBand[] Bands { get; }

    /// <summary>
    /// Available EQ presets.
    /// </summary>
    public List<EqualizerPreset> Presets { get; }

    /// <summary>
    /// Whether the equalizer processing is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                SettingsManager.Current.EqualizerEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                Logger.Info($"Equalizer {(value ? "enabled" : "disabled")}");
            }
        }
    }

    /// <summary>
    /// Name of the currently active preset.
    /// </summary>
    public string ActivePresetName
    {
        get => _activePresetName;
        set
        {
            if (_activePresetName != value)
            {
                _activePresetName = value;
                SettingsManager.Current.ActiveEqPresetName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActivePresetName)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void LoadFromSettings()
    {
        _isEnabled = SettingsManager.Current.EqualizerEnabled;
        _activePresetName = SettingsManager.Current.ActiveEqPresetName;
    }

    private EqualizerService()
    {
        LoadFromSettings();
        // Initialize 10 standard EQ bands
        Bands = new EqualizerBand[]
        {
            new() { Frequency = 31,    Bandwidth = 0.8f, Gain = 0, Label = "31"  },
            new() { Frequency = 63,    Bandwidth = 0.8f, Gain = 0, Label = "63"  },
            new() { Frequency = 125,   Bandwidth = 0.8f, Gain = 0, Label = "125" },
            new() { Frequency = 250,   Bandwidth = 0.8f, Gain = 0, Label = "250" },
            new() { Frequency = 500,   Bandwidth = 0.8f, Gain = 0, Label = "500" },
            new() { Frequency = 1000,  Bandwidth = 0.8f, Gain = 0, Label = "1K"  },
            new() { Frequency = 2000,  Bandwidth = 0.8f, Gain = 0, Label = "2K"  },
            new() { Frequency = 4000,  Bandwidth = 0.8f, Gain = 0, Label = "4K"  },
            new() { Frequency = 8000,  Bandwidth = 0.8f, Gain = 0, Label = "8K"  },
            new() { Frequency = 16000, Bandwidth = 0.8f, Gain = 0, Label = "16K" },
        };

        foreach (var band in Bands)
        {
            band.PropertyChanged += OnBandPropertyChanged;
        }

        // Initialize presets
        Presets = new List<EqualizerPreset>
        {
            new() { Name = "Normal",        IconSymbol = "Emoji24",        Gains = new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } },
            new() { Name = "Pop",           IconSymbol = "MusicNote224",   Gains = new float[] { -1, 1, 4, 5, 4, 1, -1, -2, -2, -1 } },
            new() { Name = "Rock",          IconSymbol = "MusicNote124",   Gains = new float[] { 5, 4, 3, 1, -1, -1, 1, 3, 4, 5 } },
            new() { Name = "Heavy metal",   IconSymbol = "TopSpeed24",     Gains = new float[] { 4, 3, 0, -2, -3, -2, 1, 3, 4, 5 } },
            new() { Name = "Jazz",          IconSymbol = "HeadphonesSoundWave24", Gains = new float[] { 0, 0, 0, 2, 4, 4, 2, 1, 2, 3 } },
            new() { Name = "Folk",          IconSymbol = "Leaf24",         Gains = new float[] { 0, 0, 0, 0, 1, 2, 3, 4, 2, 0 } },
            new() { Name = "R&B",           IconSymbol = "Heart24",        Gains = new float[] { 3, 5, 4, 1, -1, -1, 2, 3, 4, 5 } },
            new() { Name = "Electrónica",   IconSymbol = "Flash24",        Gains = new float[] { 4, 3, 1, 0, -2, -1, 0, 2, 4, 5 } },
            new() { Name = "HipHop",        IconSymbol = "Speaker224",     Gains = new float[] { 5, 4, 1, 3, -1, -1, 1, -1, 2, 3 } },
            new() { Name = "Reguetón",      IconSymbol = "Fire24",         Gains = new float[] { 5, 4, 3, 0, -1, -1, 0, 2, 3, 4 } },
            new() { Name = "Sonido latino", IconSymbol = "DrinkMargarita24", Gains = new float[] { 4, 3, 0, 0, -1, -1, 2, 3, 4, 5 } }
        };

        Logger.Info("EqualizerService initialized with 10 bands and 8 presets");

        if (_activePresetName == "Custom")
        {
            ApplyCustomGains();
        }
        else if (_activePresetName != "Normal")
        {
            ApplyPreset(_activePresetName);
        }
    }

    private void ApplyCustomGains()
    {
        _isApplyingPreset = true;
        var savedGains = SettingsManager.Current.EqualizerGains;
        if (savedGains != null && savedGains.Length == Bands.Length)
        {
            for (int i = 0; i < Bands.Length; i++)
            {
                Bands[i].Gain = savedGains[i];
            }
        }
        _isApplyingPreset = false;
        ActivePresetName = "Custom";
    }

    private void OnBandPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EqualizerBand.Gain) && !_isApplyingPreset)
        {
            var savedGains = SettingsManager.Current.EqualizerGains;
            if (savedGains == null || savedGains.Length != Bands.Length)
            {
                SettingsManager.Current.EqualizerGains = new float[Bands.Length];
                savedGains = SettingsManager.Current.EqualizerGains;
            }

            for (int i = 0; i < Bands.Length; i++)
            {
                savedGains[i] = Bands[i].Gain;
            }

            // Force update to trigger auto-save in UserSettings
            SettingsManager.Current.NotifyPropertyChanged(nameof(UserSettings.EqualizerGains));
            
            if (ActivePresetName != "Custom")
            {
                ActivePresetName = "Custom";
            }
        }
    }

    /// <summary>
    /// Apply a preset by name — sets all band gains to the preset values.
    /// </summary>
    public void ApplyPreset(string presetName)
    {
        var preset = Presets.Find(p => p.Name == presetName);
        if (preset == null) return;

        _isApplyingPreset = true;
        for (int i = 0; i < Math.Min(Bands.Length, preset.Gains.Length); i++)
        {
            Bands[i].Gain = preset.Gains[i];
        }
        _isApplyingPreset = false;

        ActivePresetName = presetName;
        Logger.Info($"Applied EQ preset: {presetName}");
    }

    /// <summary>
    /// Reset all bands to flat (0 dB gain).
    /// </summary>
    public void Reset()
    {
        foreach (var band in Bands)
        {
            band.Gain = 0;
        }
        ActivePresetName = "Normal";
    }
}
