using System.Xml.Linq;
using FluentFlyout.Classes;
using Xunit;

namespace FluentFlyout.Tests;

public class LocalizationCoverageTests
{
    private static readonly string[] InvariantKeys =
    [
        "AnimationSpeed_05x",
        "AnimationSpeed_15x",
        "AnimationSpeed_1x",
        "AnimationSpeed_2x",
        "AnimationSpeed_3x",
        "AppLanguageDescriptionLink",
        "App_ProductName",
        "Edit_ErrorTitle",
        "Edit_GenrePlaceholder",
        "Edit_ShiftLyrics_Backward",
        "Edit_ShiftLyrics_Forward",
        "General_No",
        "General_Separator",
        "License_Error",
        "MillisecondsUnit",
        "Playlist_DurationHoursMinutesFormat",
        "Playlist_DurationMinutesSecondsFormat",
        "PixelsUnit",
    ];

    [Fact]
    public void BaseAndEnglishDictionaries_ContainCriticalLocalizationKeys()
    {
        var spanish = LoadDictionary("Dictionary-es.xaml");
        var english = LoadDictionary("Dictionary-en-US.xaml");

        string[] requiredKeys =
        [
            "App_ProductName",
            "License_Processing",
            "License_Success",
            "License_PurchaseFailed",
            "License_Error",
            "Queue_ChangesSaved",
            "Queue_PlaylistLoaded",
            "Queue_Cleared",
            "Queue_TrackRemoved",
            "Queue_TracksRemoved",
            "Track_UnknownAlbum",
            "Track_UnknownArtist",
            "NowPlaying_NoTrack",
            "NowPlaying_SelectTrack",
            "Playlist_SummarySingleFormat",
            "Playlist_SummaryPluralFormat",
            "Playlist_DurationHoursMinutesFormat",
            "Playlist_DurationMinutesSecondsFormat",
        ];

        foreach (var key in requiredKeys)
        {
            Assert.True(spanish.ContainsKey(key), $"Missing Spanish localization key: {key}");
            Assert.True(english.ContainsKey(key), $"Missing English localization key: {key}");
        }
    }

    [Fact]
    public void EnglishDictionary_DoesNotInheritSpanishValues_OutsideInvariantKeys()
    {
        var spanish = LoadDictionary("Dictionary-es.xaml");
        var english = LoadDictionary("Dictionary-en-US.xaml");

        var inherited = english
            .Where(entry => spanish.TryGetValue(entry.Key, out var spanishValue)
                && entry.Value == spanishValue
                && !InvariantKeys.Contains(entry.Key, StringComparer.Ordinal))
            .Select(entry => entry.Key)
            .ToList();

        Assert.Empty(inherited);
    }

    [Fact]
    public void DebugHighlighting_FlagsMissingOrInheritedSpanishTranslations()
    {
        var spanish = new Dictionary<string, string>
        {
            ["License_Processing"] = "Procesando...",
            ["Queue_ChangesSaved"] = "Cambios en la cola",
        };

        var inheritedCurrent = new Dictionary<string, string>
        {
            ["License_Processing"] = "Procesando..."
        };

        Assert.True(LocalizationManager.ShouldHighlightMissingTranslation("License_Processing", "en-US", inheritedCurrent, spanish));
        Assert.True(LocalizationManager.ShouldHighlightMissingTranslation("Queue_ChangesSaved", "en-US", inheritedCurrent, spanish));
        Assert.False(LocalizationManager.ShouldHighlightMissingTranslation("App_ProductName", "en-US", new Dictionary<string, string>(), new Dictionary<string, string> { ["App_ProductName"] = "FluentFlyout" }));
        Assert.False(LocalizationManager.ShouldHighlightMissingTranslation("Color_Grey", "fr", new Dictionary<string, string> { ["Color_Grey"] = "Gris" }, new Dictionary<string, string> { ["Color_Grey"] = "Gris" }));
        Assert.False(LocalizationManager.ShouldHighlightMissingTranslation("SystemSettingsTitle", "pt-BR", new Dictionary<string, string> { ["SystemSettingsTitle"] = "Sistema" }, new Dictionary<string, string> { ["SystemSettingsTitle"] = "Sistema" }));
        Assert.False(LocalizationManager.ShouldHighlightMissingTranslation("TaskbarVisualizerStereoModeTitle", "pt-PT", new Dictionary<string, string> { ["TaskbarVisualizerStereoModeTitle"] = "Modo estéreo" }, new Dictionary<string, string> { ["TaskbarVisualizerStereoModeTitle"] = "Modo estéreo" }));
        Assert.False(LocalizationManager.ShouldHighlightMissingTranslation("Edit_ArtistLabel", "it", new Dictionary<string, string> { ["Edit_ArtistLabel"] = "Artista" }, new Dictionary<string, string> { ["Edit_ArtistLabel"] = "Artista" }));
    }

    private static Dictionary<string, string> LoadDictionary(string fileName)
    {
        var document = XDocument.Load(Path.Combine(GetLocalizationDirectory(), fileName));
        return document.Root?
            .Elements()
            .Where(element => element.Name.LocalName.EndsWith("String", StringComparison.Ordinal))
            .Select(element => new
            {
                Key = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value,
                Value = element.Value
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
            .ToDictionary(entry => entry.Key!, entry => entry.Value)
            ?? [];
    }

    private static string GetLocalizationDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "FluentFlyout.sln")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            throw new DirectoryNotFoundException("Could not locate FluentFlyout.sln");
        }

        return Path.Combine(current.FullName, "FluentFlyoutWPF", "Resources", "Localization");
    }
}
