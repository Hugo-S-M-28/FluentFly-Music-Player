using System.Xml.Serialization;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using Xunit;

namespace FluentFlyout.Tests;

public class UserSettingsPlaybackSourceModeTests
{
    [Fact]
    public void LegacyInternalOnlySettings_MigrateToInternalPlaybackSource()
    {
        UserSettings settings = Deserialize(
            """
            <UserSettings>
              <InternalPlayerEnabled>true</InternalPlayerEnabled>
              <SystemMediaControlEnabled>false</SystemMediaControlEnabled>
            </UserSettings>
            """);

        settings.CompleteInitialization();

        Assert.Equal(PlaybackSourceMode.InternalPlayer, settings.PlaybackSourceMode);
        Assert.True(settings.InternalPlayerEnabled);
        Assert.False(settings.SystemMediaControlEnabled);
    }

    [Fact]
    public void LegacyExternalOnlySettings_MigrateToExternalPlaybackSource()
    {
        UserSettings settings = Deserialize(
            """
            <UserSettings>
              <InternalPlayerEnabled>false</InternalPlayerEnabled>
              <SystemMediaControlEnabled>true</SystemMediaControlEnabled>
            </UserSettings>
            """);

        settings.CompleteInitialization();

        Assert.Equal(PlaybackSourceMode.ExternalMediaControl, settings.PlaybackSourceMode);
        Assert.False(settings.InternalPlayerEnabled);
        Assert.True(settings.SystemMediaControlEnabled);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void AmbiguousLegacySettings_DefaultToInternalPlaybackSource(bool internalEnabled, bool externalEnabled)
    {
        UserSettings settings = Deserialize(
            $"""
             <UserSettings>
               <InternalPlayerEnabled>{internalEnabled.ToString().ToLowerInvariant()}</InternalPlayerEnabled>
               <SystemMediaControlEnabled>{externalEnabled.ToString().ToLowerInvariant()}</SystemMediaControlEnabled>
             </UserSettings>
             """);

        settings.CompleteInitialization();

        Assert.Equal(PlaybackSourceMode.InternalPlayer, settings.PlaybackSourceMode);
        Assert.True(settings.InternalPlayerEnabled);
        Assert.False(settings.SystemMediaControlEnabled);
    }

    [Fact]
    public void NewSerialization_StoresPlaybackSourceModeWithoutLegacyFlags()
    {
        UserSettings settings = new()
        {
            PlaybackSourceMode = PlaybackSourceMode.ExternalMediaControl
        };

        string xml = Serialize(settings);

        Assert.Contains("<PlaybackSourceMode>ExternalMediaControl</PlaybackSourceMode>", xml);
        Assert.DoesNotContain("<InternalPlayerEnabled>", xml);
        Assert.DoesNotContain("<SystemMediaControlEnabled>", xml);
    }

    [Fact]
    public void DeserializedSettings_DoNotDuplicateDefaultMusicFolder()
    {
        UserSettings settings = Deserialize(
            """
            <UserSettings>
              <MusicLibraryFolders>
                <string>C:\Users\Test\Music</string>
                <string>C:\Users\Test\Music</string>
                <string>C:\Users\Test\Music\MP3</string>
              </MusicLibraryFolders>
            </UserSettings>
            """);

        settings.CompleteInitialization();

        Assert.Equal(2, settings.MusicLibraryFolders.Count);
        Assert.Equal(@"C:\Users\Test\Music", settings.MusicLibraryFolders[0]);
        Assert.Equal(@"C:\Users\Test\Music\MP3", settings.MusicLibraryFolders[1]);
    }

    private static UserSettings Deserialize(string xml)
    {
        XmlSerializer serializer = new(typeof(UserSettings));
        using StringReader reader = new(xml);
        return (UserSettings)serializer.Deserialize(reader)!;
    }

    private static string Serialize(UserSettings settings)
    {
        XmlSerializer serializer = new(typeof(UserSettings));
        using StringWriter writer = new();
        serializer.Serialize(writer, settings);
        return writer.ToString();
    }
}
