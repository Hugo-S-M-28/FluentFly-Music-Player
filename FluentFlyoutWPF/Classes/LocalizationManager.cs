using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using System.Collections;
using System.Globalization;
using System.Windows;

namespace FluentFlyout.Classes;

public static class LocalizationManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static ResourceDictionary? _activeLocalizationDictionary;
#if DEBUG
    private static ResourceDictionary? _debugOverlayDictionary;
    private static readonly HashSet<string> DebugInvariantKeys =
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

    private static readonly Dictionary<string, HashSet<string>> DebugEquivalentKeysByCulture = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] =
        [
            "Color_Grey",
        ],
        ["ca"] =
        [
            "Color_Grey",
            "Color_Pink",
            "Edit_ArtistLabel",
            "General_Yes",
            "Home_Title",
            "Lib_Sort_Artist",
            "Lib_Title",
            "LockWindow_NumLock",
            "LockWindow_ScrollLock",
            "ManageLibrary_DeleteFolder",
            "SystemSettingsTitle",
        ],
        ["pt-BR"] =
        [
            "Color_Blue",
            "Color_Green",
            "Color_Pink",
            "ColorPicker_Cancel",
            "ColorPicker_Confirm",
            "Edit_AlbumLabel",
            "Edit_ArtistLabel",
            "Edit_AutoSyncSuccessTitle",
            "Edit_Cancel",
            "Edit_CollaboratorsLabel",
            "Edit_LyricsFormatInfo",
            "General_Open",
            "Home_Repeat",
            "Home_SyncButton",
            "Lib_Artists",
            "Lib_SizeLarge",
            "Lib_Sort_Album",
            "Lib_Sort_Artist",
            "Lib_Sort_Title",
            "Lib_SortBy",
            "Lib_Title",
            "Lib_ToggleList",
            "Playlist_Close",
            "PremiumPurchaseCancelled",
            "SystemConfigure",
            "SystemSettingsTitle",
            "TaskbarVisualizer_Max",
            "TaskbarVisualizerStereoModeTitle",
            "UnlockPremiumButton",
        ],
        ["pt-PT"] =
        [
            "Color_Blue",
            "Color_Green",
            "ColorPicker_Cancel",
            "ColorPicker_Confirm",
            "Edit_AlbumLabel",
            "Edit_ArtistLabel",
            "Edit_AutoSyncSuccessTitle",
            "Edit_Cancel",
            "Edit_CollaboratorsLabel",
            "Edit_GenreLabel",
            "Edit_LyricsFormatInfo",
            "Edit_ShiftLyricsTitle",
            "General_Open",
            "Home_Repeat",
            "Home_SyncButton",
            "Lib_Artists",
            "Lib_SizeLarge",
            "Lib_Sort_Album",
            "Lib_Sort_Artist",
            "Lib_Sort_Title",
            "Lib_SortBy",
            "Lib_Title",
            "Lib_ToggleList",
            "Playlist_Close",
            "PremiumPurchaseCancelled",
            "SystemConfigure",
            "SystemSettingsTitle",
            "TaskbarVisualizer_Max",
            "TaskbarVisualizerBarCountTitle",
            "TaskbarVisualizerCenteredBarsTitle",
            "TaskbarVisualizerStereoModeTitle",
            "UnlockPremiumButton",
        ],
        ["it"] =
        [
            "Color_Green",
            "Color_Pink",
            "Edit_ArtistLabel",
            "Edit_LyricsFormatInfo",
            "Lib_SizeLarge",
            "Lib_Sort_Artist",
            "SystemSettingsTitle",
        ],
    };
#endif

    private static readonly string[] SupportedLanguageCodes =
    [
        "system",
        "en-US",
        "ar",
        "ca",
        "zh-CN",
        "zh-TW",
        "hr",
        "cs",
        "nl",
        "fi",
        "fr",
        "de",
        "he",
        "hu",
        "hi",
        "id",
        "it",
        "ja",
        "ko",
        "pl",
        "pt-BR",
        "pt-PT",
        "ru",
        "si",
        "sk",
        "es",
        "ta",
        "th",
        "tr",
        "uk",
        "vi",
    ];

    private static readonly Dictionary<string, string> LanguageDisplayNameOverrides = new()
    {
        { "system", "System" },
        { "en-US", "English" },
    };

    private static readonly Dictionary<string, string> _supportedLanguages = CreateSupportedLanguages();

    public static double maxLength = 0;

    public static string LanguageCode { get; set; } = string.Empty;
    public static string CurrentCulture { get; private set; } = string.Empty;
    public static event EventHandler? LocalizationChanged;

    private static readonly Dictionary<string, string> _languageFontFamilies = new()
    {
        { "default", "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic" },
        { "zh-TW", "Segoe UI Variable, Microsoft JhengHei UI, Yu Gothic UI, Malgun Gothic" },
        { "ja", "Segoe UI Variable, Yu Gothic UI, Microsoft YaHei UI, Malgun Gothic" },
        { "ko", "Segoe UI Variable, Malgun Gothic, Microsoft YaHei UI, Yu Gothic UI" },
    };

    private static readonly HashSet<string> _rtlLanguages = ["ar", "he"];

    public static IReadOnlyDictionary<string, string> SupportedLanguages => _supportedLanguages;

    public static string GetString(string resourceKey, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return fallback ?? string.Empty;
        }

        if (Application.Current?.TryFindResource(resourceKey) is string localized
            && !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

#if DEBUG
        if (ShouldHighlightMissingTranslation(resourceKey, CurrentCulture, GetDictionaryEntries(_activeLocalizationDictionary), GetDictionaryEntries(FindDictionaryBySuffix("Dictionary-es.xaml"))))
        {
            return $"[{resourceKey}]";
        }
#endif

        return fallback ?? $"[{resourceKey}]";
    }

    public static void ApplyLocalization()
    {
        string culture = SettingsManager.Current.AppLanguage == "system"
            ? CultureInfo.CurrentUICulture.Name
            : SettingsManager.Current.AppLanguage;

        string languageCode = culture.Length >= 2 ? culture[..2] : culture;
        LanguageCode = languageCode;
        CurrentCulture = culture;

        var dictionaries = App.Current.Resources.MergedDictionaries;

        foreach (var dictionary in dictionaries.ToList())
        {
            if (dictionary.Source != null
                && dictionary.Source.OriginalString.StartsWith("Resources/Localization/")
                && !dictionary.Source.OriginalString.EndsWith("Dictionary-es.xaml")
                && !dictionary.Source.OriginalString.EndsWith("Dictionary-en-US.xaml"))
            {
                dictionaries.Remove(dictionary);
            }
        }

#if DEBUG
        if (_debugOverlayDictionary != null)
        {
            dictionaries.Remove(_debugOverlayDictionary);
            _debugOverlayDictionary = null;
        }
#endif

        Logger.Info("Applying localization for language: " + culture);

        ApplyFlowDirection(languageCode);
        ApplyFontFamily(culture);

        ResourceDictionary? activeDictionary = null;
        bool preferSpanishFallback = languageCode == "es";

        if (languageCode == "es")
        {
            activeDictionary = FindDictionaryBySuffix("Dictionary-es.xaml");
            goto PostLoad;
        }
        if (languageCode == "en")
        {
            activeDictionary = FindDictionaryBySuffix("Dictionary-en-US.xaml");
            goto PostLoad;
        }

        string localizationDictPath = $"Resources/Localization/Dictionary-{culture}.xaml";
        activeDictionary = TryLoadDictionary(dictionaries, localizationDictPath);
        if (activeDictionary != null)
        {
            Logger.Info("Successfully loaded localization for: " + culture);
            goto PostLoad;
        }

        localizationDictPath = $"Resources/Localization/Dictionary-{languageCode}.xaml";
        activeDictionary = TryLoadDictionary(dictionaries, localizationDictPath);
        if (activeDictionary != null)
        {
            Logger.Info("Successfully loaded localization for simplified code: " + languageCode);
            goto PostLoad;
        }

        if (languageCode == "zh")
        {
            activeDictionary = TryLoadDictionary(dictionaries, "Resources/Localization/Dictionary-zh-CN.xaml");
        }
        else if (languageCode == "pt")
        {
            activeDictionary = TryLoadDictionary(dictionaries, "Resources/Localization/Dictionary-pt-BR.xaml");
        }
        else
        {
            activeDictionary = FindDictionaryBySuffix("Dictionary-en-US.xaml");
        }

        Logger.Warn("Localization file not found for language: " + culture + ". Falling back to English/Spanish.");

    PostLoad:
        EnsureBaseDictionaryOrder(dictionaries, preferSpanishFallback, activeDictionary);
        _activeLocalizationDictionary = activeDictionary;
#if DEBUG
        ApplyDebugOverlay(dictionaries, culture, activeDictionary);
#endif
        List<double> lengths = new();
        lengths.Add(StringWidth.GetStringWidth(GetString("LockWindow_InsertPressed", "Insert Pressed")));

        var on = GetString("LockWindow_LockOn");
        var off = GetString("LockWindow_LockOff");
        var onOffMax = on.Length >= off.Length ? on + " " : off + " ";

        lengths.Add(StringWidth.GetStringWidth(onOffMax + GetString("LockWindow_CapsLock", "Caps Lock")));
        lengths.Add(StringWidth.GetStringWidth(onOffMax + GetString("LockWindow_NumLock", "Num Lock")));
        lengths.Add(StringWidth.GetStringWidth(onOffMax + GetString("LockWindow_ScrollLock", "Scroll Lock")));

        maxLength = lengths.Max();
        if (maxLength < 20)
        {
            maxLength = 115;
        }

        LocalizationChanged?.Invoke(null, EventArgs.Empty);
    }

    private static Dictionary<string, string> CreateSupportedLanguages()
    {
        Dictionary<string, string> languages = new();

        foreach (var cultureCode in SupportedLanguageCodes)
        {
            languages[GetDisplayNameForCulture(cultureCode)] = cultureCode;
        }

        return languages;
    }

    private static string GetDisplayNameForCulture(string cultureCode)
    {
        if (LanguageDisplayNameOverrides.TryGetValue(cultureCode, out var displayName))
        {
            return displayName;
        }

        return CultureInfo.GetCultureInfo(cultureCode).NativeName;
    }

    private static void ApplyFlowDirection(string languageCode)
    {
        SettingsManager.Current.FlowDirection = _rtlLanguages.Contains(languageCode)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Logger.Info("Applied flow direction: " + SettingsManager.Current.FlowDirection);
    }

    private static void ApplyFontFamily(string culture)
    {
        string fontFamily;
        if (_languageFontFamilies.TryGetValue(culture, out string? value))
        {
            fontFamily = value;
        }
        else if (_languageFontFamilies.TryGetValue(LanguageCode, out string? value1))
        {
            fontFamily = value1;
        }
        else
        {
            fontFamily = _languageFontFamilies["default"];
        }

        SettingsManager.Current.FontFamily = fontFamily;
        Logger.Debug("Applied font family: " + fontFamily);
    }

    private static ResourceDictionary? TryLoadDictionary(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, string path)
    {
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            var resourceDict = new ResourceDictionary { Source = uri };
            dictionaries.Add(resourceDict);
            return resourceDict;
        }
        catch
        {
            return null;
        }
    }

    private static ResourceDictionary? FindDictionaryBySuffix(string suffix)
    {
        return Application.Current?.Resources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source != null &&
            dictionary.Source.OriginalString.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureBaseDictionaryOrder(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, bool preferSpanishFallback, ResourceDictionary? activeDictionary)
    {
        var spanishDictionary = FindDictionaryBySuffix("Dictionary-es.xaml");
        var englishDictionary = FindDictionaryBySuffix("Dictionary-en-US.xaml");

        if (spanishDictionary == null || englishDictionary == null)
        {
            return;
        }

        dictionaries.Remove(spanishDictionary);
        dictionaries.Remove(englishDictionary);

        if (activeDictionary != null && activeDictionary != spanishDictionary && activeDictionary != englishDictionary)
        {
            dictionaries.Remove(activeDictionary);
        }

        if (preferSpanishFallback)
        {
            dictionaries.Add(englishDictionary);
            dictionaries.Add(spanishDictionary);
        }
        else
        {
            dictionaries.Add(spanishDictionary);
            dictionaries.Add(englishDictionary);
        }

        if (activeDictionary != null && activeDictionary != spanishDictionary && activeDictionary != englishDictionary)
        {
            dictionaries.Add(activeDictionary);
        }
    }

    internal static IReadOnlyDictionary<string, string> GetDictionaryEntries(ResourceDictionary? dictionary)
    {
        Dictionary<string, string> entries = [];
        if (dictionary == null)
        {
            return entries;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                entries[key] = value;
            }
        }

        return entries;
    }

#if DEBUG
    internal static bool ShouldHighlightMissingTranslation(string resourceKey, string culture, IReadOnlyDictionary<string, string> currentEntries, IReadOnlyDictionary<string, string> spanishEntries)
    {
        if (string.IsNullOrWhiteSpace(resourceKey)
            || string.IsNullOrWhiteSpace(culture)
            || culture.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            || DebugInvariantKeys.Contains(resourceKey)
            || IsEquivalentLocalizedKey(culture, resourceKey)
            || !spanishEntries.TryGetValue(resourceKey, out var spanishValue))
        {
            return false;
        }

        return !currentEntries.TryGetValue(resourceKey, out var currentValue)
            || string.Equals(currentValue, spanishValue, StringComparison.Ordinal);
    }

    private static bool IsEquivalentLocalizedKey(string culture, string resourceKey)
    {
        if (DebugEquivalentKeysByCulture.TryGetValue(culture, out var exactMatches) && exactMatches.Contains(resourceKey))
        {
            return true;
        }

        if (culture.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
            && DebugEquivalentKeysByCulture.TryGetValue(culture, out var portugueseMatches)
            && portugueseMatches.Contains(resourceKey))
        {
            return true;
        }

        return false;
    }

    private static void ApplyDebugOverlay(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, string culture, ResourceDictionary? currentDictionary)
    {
        if (culture.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var spanishEntries = GetDictionaryEntries(FindDictionaryBySuffix("Dictionary-es.xaml"));
        var currentEntries = GetDictionaryEntries(currentDictionary);

        ResourceDictionary overlay = new();
        foreach (var entry in spanishEntries)
        {
            if (ShouldHighlightMissingTranslation(entry.Key, culture, currentEntries, spanishEntries))
            {
                overlay[entry.Key] = $"[{entry.Key}]";
            }
        }

        if (overlay.Count == 0)
        {
            return;
        }

        _debugOverlayDictionary = overlay;
        dictionaries.Add(overlay);
    }
#endif
}
