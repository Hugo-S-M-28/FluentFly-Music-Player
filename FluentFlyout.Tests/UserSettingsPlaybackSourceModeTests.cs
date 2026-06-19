using System.Xml.Serialization;
using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
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

    [Fact]
    public void DeserializedSettings_RestoreExcludedLibraryPaths()
    {
        UserSettings settings = Deserialize(
            """
            <UserSettings>
              <ExcludedLibraryPaths>
                <string>C:\Users\Test\Music\Ignore</string>
                <string>C:\Users\Test\Music\Ignore</string>
                <string>C:\Users\Test\Music\skip.mp3</string>
              </ExcludedLibraryPaths>
            </UserSettings>
            """);

        settings.CompleteInitialization();

        Assert.Equal(2, settings.ExcludedLibraryPaths.Count);
        Assert.Equal(@"C:\Users\Test\Music\Ignore", settings.ExcludedLibraryPaths[0]);
        Assert.Equal(@"C:\Users\Test\Music\skip.mp3", settings.ExcludedLibraryPaths[1]);
    }

    [Fact]
    public void LibraryCollectionChanges_RaisePropertyChangedAfterInitialization()
    {
        UserSettings settings = new();
        settings.CompleteInitialization();
        var changedProperties = new List<string?>();
        settings.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

        settings.MusicLibraryFolders.Add(@"C:\Users\Test\Music");
        settings.ExcludedLibraryPaths.Add(@"C:\Users\Test\Music\Ignore");

        Assert.Contains(nameof(UserSettings.MusicLibraryFolders), changedProperties);
        Assert.Contains(nameof(UserSettings.ExcludedLibraryPaths), changedProperties);
    }

    [Fact]
    public void CompleteInitialization_ClearsInitializingFlagWhenInitializationFails()
    {
        UserSettings settings = new();
        typeof(UserSettings)
            .GetProperty(nameof(UserSettings.MusicLibraryFolders))!
            .SetValue(settings, null);

        settings.CompleteInitialization();

        bool initializing = (bool)typeof(UserSettings)
            .GetField("_initializing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(settings)!;

        Assert.False(initializing);
    }

    [Fact]
    public void SerializedSettings_RestoreMutableAndScalarCustomizations()
    {
        UserSettings settings = new()
        {
            EqualizerGains = [1f, 0.5f, 0f, -0.5f, -1f, 1.5f, 2f, -2f, 3f, -3f],
            AppTheme = 2,
            AppLanguage = "es",
            LibraryGridItemSize = 220,
            LibraryTrackIconSize = 72,
            LibraryItemCornerRadius = 18
        };
        settings.MusicLibraryFolders.Add(@"C:\Users\Test\Music");
        settings.ExcludedLibraryPaths.Add(@"C:\Users\Test\Music\Ignore");

        UserSettings restored = Deserialize(Serialize(settings));
        restored.CompleteInitialization();

        Assert.Contains(@"C:\Users\Test\Music", restored.MusicLibraryFolders);
        Assert.Contains(@"C:\Users\Test\Music\Ignore", restored.ExcludedLibraryPaths);
        Assert.Equal(settings.EqualizerGains, restored.EqualizerGains);
        Assert.Equal(2, restored.AppTheme);
        Assert.Equal("es", restored.AppLanguage);
        Assert.Equal(220, restored.LibraryGridItemSize);
        Assert.Equal(72, restored.LibraryTrackIconSize);
        Assert.Equal(18, restored.LibraryItemCornerRadius);
    }

    [Fact]
    public void TaskbarWidgetPaddingChanged_SendsUpdateTaskbarMessage()
    {
        var recipient = new TaskbarMessageRecipient();
        WeakReferenceMessenger.Default.Register<UpdateTaskbarMessage>(recipient, static (r, _) => ((TaskbarMessageRecipient)r).Count++);

        try
        {
            UserSettings settings = new();
            settings.CompleteInitialization();

            settings.TaskbarWidgetPadding = !settings.TaskbarWidgetPadding;

            Assert.Equal(1, recipient.Count);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    [Theory]
    [InlineData("0", 250)]
    [InlineData("-1", 250)]
    [InlineData("249", 250)]
    [InlineData("250", 250)]
    [InlineData("10001", 10000)]
    [InlineData("bad", 2000)]
    public void LockKeysDurationText_ClampsToVisibleRange(string value, int expected)
    {
        UserSettings settings = new();

        settings.LockKeysDurationText = value;

        Assert.Equal(expected, settings.LockKeysDuration);
    }

    [Fact]
    public void TaskbarWidgetPositionChanged_SendsUpdateTaskbarMessage()
    {
        var recipient = new TaskbarMessageRecipient();
        WeakReferenceMessenger.Default.Register<UpdateTaskbarMessage>(recipient, static (r, _) => ((TaskbarMessageRecipient)r).Count++);

        try
        {
            UserSettings settings = new();
            settings.CompleteInitialization();

            settings.TaskbarWidgetPosition = (settings.TaskbarWidgetPosition == 0) ? 1 : 0;

            Assert.Equal(1, recipient.Count);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }

    [Fact]
    public void TaskbarWidgetControlsPositionChanged_SendsReorderTaskbarWidgetControlsMessage()
    {
        var recipient = new TaskbarMessageRecipient();
        WeakReferenceMessenger.Default.Register<ReorderTaskbarWidgetControlsMessage>(recipient, static (r, _) => ((TaskbarMessageRecipient)r).Count++);

        try
        {
            UserSettings settings = new();
            settings.CompleteInitialization();

            settings.TaskbarWidgetControlsPosition = (settings.TaskbarWidgetControlsPosition == 0) ? 1 : 0;

            Assert.Equal(1, recipient.Count);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
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

    private sealed class TaskbarMessageRecipient
    {
        public int Count { get; set; }
    }
}
