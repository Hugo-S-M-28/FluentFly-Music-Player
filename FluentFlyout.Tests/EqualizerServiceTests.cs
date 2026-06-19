using System.Reflection;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.ViewModels;
using Xunit;

namespace FluentFlyout.Tests;

public class EqualizerServiceTests
{
    [Fact]
    public void BandGainChange_PersistsCompleteGainsArray()
    {
        UserSettings settings = CreateIsolatedSettings();
        EqualizerService service = CreateFreshService(settings);

        service.Bands[0].Gain = 3.5f;
        service.PersistCurrentGains();

        Assert.Equal(10, settings.EqualizerGains.Length);
        Assert.Equal(3.5f, settings.EqualizerGains[0]);
        Assert.Equal("Custom", settings.ActiveEqPresetName);
    }

    [Fact]
    public void ApplyPreset_PersistsPresetNameAndGains()
    {
        UserSettings settings = CreateIsolatedSettings();
        EqualizerService service = CreateFreshService(settings);

        service.ApplyPreset("Rock");

        Assert.Equal("Rock", settings.ActiveEqPresetName);
        Assert.Equal(new float[] { 5, 4, 3, 1, -1, -1, 1, 3, 4, 5 }, settings.EqualizerGains);
    }

    [Fact]
    public void Reset_PersistsNormalPresetAndFlatGains()
    {
        UserSettings settings = CreateIsolatedSettings();
        EqualizerService service = CreateFreshService(settings);

        service.ApplyPreset("Rock");
        service.Reset();

        Assert.Equal("Normal", settings.ActiveEqPresetName);
        Assert.All(settings.EqualizerGains, gain => Assert.Equal(0f, gain));
    }

    private static UserSettings CreateIsolatedSettings()
    {
        var settings = new UserSettings
        {
            EqualizerEnabled = true,
            ActiveEqPresetName = "Custom",
            EqualizerGains = new float[10]
        };
        settings.CompleteInitialization();
        SettingsManager.Current = settings;
        return settings;
    }

    private static EqualizerService CreateFreshService(UserSettings settings)
    {
        SettingsManager.Current = settings;
        typeof(EqualizerService)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        return EqualizerService.Instance;
    }
}
