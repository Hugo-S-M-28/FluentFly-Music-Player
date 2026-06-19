using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;
using Xunit;

namespace FluentFlyout.Tests;

public class SettingsManagerPersistenceTests
{
    [Fact]
    public void SaveAndRestoreSettings_PreservesUserCustomizations()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "FluentFlyoutTests", Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(tempDirectory, "settings.xml");

        try
        {
            UserSettings settings = new()
            {
                AppTheme = 1,
                AppLanguage = "es",
                LibraryGridItemSize = 210,
                LibraryTrackIconSize = 64,
                LibraryItemCornerRadius = 20,
                EqualizerGains = [0f, 1f, 2f, 3f, 4f, -1f, -2f, -3f, -4f, -5f]
            };
            settings.MusicLibraryFolders.Clear();
            settings.ExcludedLibraryPaths.Clear();
            settings.MusicLibraryFolders.Add(@"C:\Users\Test\Music");
            settings.ExcludedLibraryPaths.Add(@"C:\Users\Test\Music\Ignored");
            SettingsManager.Current = settings;

            SettingsManager.SaveSettings(settingsPath);
            SettingsManager.Current = new UserSettings();
            UserSettings restored = SettingsManager.RestoreSettings(settingsPath);

            Assert.Contains(@"C:\Users\Test\Music", restored.MusicLibraryFolders);
            Assert.Contains(@"C:\Users\Test\Music\Ignored", restored.ExcludedLibraryPaths);
            Assert.Equal(settings.EqualizerGains, restored.EqualizerGains);
            Assert.Equal(1, restored.AppTheme);
            Assert.Equal("es", restored.AppLanguage);
            Assert.Equal(210, restored.LibraryGridItemSize);
            Assert.Equal(64, restored.LibraryTrackIconSize);
            Assert.Equal(20, restored.LibraryItemCornerRadius);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
