// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FluentFlyoutWPF.Classes;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Windows;

public partial class EqualizerWindow : FluentWindow
{
    private static EqualizerWindow? _instance;

    public static void ShowInstance(Window owner)
    {
        if (_instance == null)
        {
            _instance = new EqualizerWindow
            {
                Owner = owner
            };
            _instance.Closed += (s, e) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
            {
                _instance.WindowState = WindowState.Normal;
            }

            _instance.Activate();
            _instance.Focus();
        }
    }

    private readonly EqualizerService _eqService;
    private readonly Slider[] _bandSliders;
    private readonly System.Windows.Controls.TextBlock[] _gainLabels;
    private bool _isUpdatingFromPreset = false;

    public EqualizerWindow()
    {
        _eqService = EqualizerService.Instance;
        InitializeComponent();

        // Collect slider and label references into arrays for easy indexed access
        _bandSliders = new Slider[]
        {
            BandSlider0, BandSlider1, BandSlider2, BandSlider3, BandSlider4,
            BandSlider5, BandSlider6, BandSlider7, BandSlider8, BandSlider9
        };
        _gainLabels = new System.Windows.Controls.TextBlock[]
        {
            GainLabel0, GainLabel1, GainLabel2, GainLabel3, GainLabel4,
            GainLabel5, GainLabel6, GainLabel7, GainLabel8, GainLabel9
        };

        // Initialize UI from current EQ service state
        LoadCurrentState();
        BuildPresetButtons();
    }

    /// <summary>
    /// Loads the current EQ band gains and enabled state into the UI controls.
    /// </summary>
    private void LoadCurrentState()
    {
        _isUpdatingFromPreset = true;

        EqEnabledToggle.IsChecked = _eqService.IsEnabled;

        for (int i = 0; i < _eqService.Bands.Length && i < _bandSliders.Length; i++)
        {
            _bandSliders[i].Value = _eqService.Bands[i].Gain;
            UpdateGainLabel(i, _eqService.Bands[i].Gain);
        }

        _isUpdatingFromPreset = false;
    }

    /// <summary>
    /// Dynamically creates preset buttons in the UniformGrid from the service's preset list.
    /// </summary>
    private void BuildPresetButtons()
    {
        PresetsGrid.Children.Clear();

        foreach (var preset in _eqService.Presets)
        {
            var btn = new Wpf.Ui.Controls.Button
            {
                Margin = new Thickness(4),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = preset.Name
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };

            // Try to resolve the icon symbol
            if (Enum.TryParse<SymbolRegular>(preset.IconSymbol, out var symbol))
            {
                stack.Children.Add(new SymbolIcon
                {
                    Symbol = symbol,
                    FontSize = 26,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = preset.Name,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });

            btn.Content = stack;
            btn.Click += PresetButton_Click;

            // Highlight the active preset
            if (preset.Name == _eqService.ActivePresetName)
            {
                btn.Appearance = ControlAppearance.Primary;
            }

            PresetsGrid.Children.Add(btn);
        }
    }

    /// <summary>
    /// Handles a band slider value change — updates the EqualizerService band gain in real-time.
    /// </summary>
    private void BandSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingFromPreset) return;
        if (sender is not Slider slider) return;
        if (slider.Tag is not string tagStr || !int.TryParse(tagStr, out int bandIndex)) return;
        if (bandIndex < 0 || bandIndex >= _eqService.Bands.Length) return;

        float gain = (float)Math.Round(e.NewValue, 1);
        _eqService.Bands[bandIndex].Gain = gain;
        UpdateGainLabel(bandIndex, gain);

        // When user manually adjusts, clear the active preset highlight
        _eqService.ActivePresetName = "Custom";
        UpdatePresetHighlights();
    }

    /// <summary>
    /// Updates the gain display label above a slider.
    /// </summary>
    private void UpdateGainLabel(int index, float gain)
    {
        if (index < 0 || index >= _gainLabels.Length) return;
        string sign = gain >= 0 ? "+" : "";
        _gainLabels[index].Text = $"{sign}{gain:F0}";
    }

    /// <summary>
    /// Applies a preset when its button is clicked.
    /// </summary>
    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button btn) return;
        if (btn.Tag is not string presetName) return;

        _eqService.ApplyPreset(presetName);

        // Update sliders to reflect the preset
        _isUpdatingFromPreset = true;
        for (int i = 0; i < _eqService.Bands.Length && i < _bandSliders.Length; i++)
        {
            _bandSliders[i].Value = _eqService.Bands[i].Gain;
            UpdateGainLabel(i, _eqService.Bands[i].Gain);
        }
        _isUpdatingFromPreset = false;

        UpdatePresetHighlights();
    }

    /// <summary>
    /// Highlights the currently active preset button and resets the others.
    /// </summary>
    private void UpdatePresetHighlights()
    {
        foreach (var child in PresetsGrid.Children)
        {
            if (child is Wpf.Ui.Controls.Button btn)
            {
                btn.Appearance = (btn.Tag as string) == _eqService.ActivePresetName
                    ? ControlAppearance.Primary
                    : ControlAppearance.Secondary;
            }
        }
    }

    /// <summary>
    /// Handles the EQ enabled/disabled toggle switch.
    /// </summary>
    private void EqEnabledToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_eqService != null && sender is Wpf.Ui.Controls.ToggleSwitch sw)
        {
            _eqService.IsEnabled = sw.IsChecked == true;
        }
    }
}
