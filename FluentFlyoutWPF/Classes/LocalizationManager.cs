using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using System.Globalization;
using System.Windows;

namespace FluentFlyout.Classes;

public static class LocalizationManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static double maxLength = 0;

    // current language code (first two letters) for easy access
    public static string LanguageCode { get; set; } = string.Empty;

    // dictionary of supported languages where key is the local language name and value is the language/culture code
    // check https://simplelocalize.io/data/locales/ for additional language info
    private static readonly Dictionary<string, string> _supportedLanguages = new()
    {
        { "System", "system" },
        { "English", "en-US" },
        { "العربية", "ar" },
        { "català", "ca" },
        { "中文（简体）", "zh-CN" },
        { "中文（繁體）", "zh-TW" },
        { "hrvatski jezik", "hr" },
        { "čeština", "cs" },
        { "Nederlands", "nl" },
        { "suomi", "fi" },
        { "français", "fr" },
        { "Deutsch", "de" },
        { "עברית", "he" },
        { "हिन्दी", "hi" },
        { "Bahasa Indonesia", "id" },
        { "Italiano", "it" },
        { "日本語", "ja" },
        { "한국어", "ko" },
        { "polski", "pl" },
        { "Português (Brasil)", "pt-BR" },
        { "Русский", "ru" },
        { "slovenčina", "sk" },
        { "Español", "es" },
        { "Türkçe", "tr" },
        { "Українська", "uk" },
        { "Tiếng Việt", "vi" },
    };

    // dictionary of font families for specific languages, priorities are switched around
    private static readonly Dictionary<string, string> _languageFontFamilies = new()
    {
        { "default", "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic" }, // default support for multiple languages
        //{ "zh-CN", "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic" }, // same as default
        { "zh-TW", "Segoe UI Variable, Microsoft JhengHei UI, Yu Gothic UI, Malgun Gothic" },
        { "ja", "Segoe UI Variable, Yu Gothic UI, Microsoft YaHei UI, Malgun Gothic" },
        { "ko", "Segoe UI Variable, Malgun Gothic, Microsoft YaHei UI, Yu Gothic UI" },
    };

    // right-to-left languages
    private static readonly HashSet<string> _rtlLanguages = ["ar", "he"];

    // readonly property to access supported languages
    public static Dictionary<string, string> SupportedLanguages => _supportedLanguages;

    public static void ApplyLocalization()
    {
        string culture;
        if (SettingsManager.Current.AppLanguage == "system")
        {
            culture = CultureInfo.CurrentUICulture.Name;
        }
        else
        {
            culture = SettingsManager.Current.AppLanguage;
        }

        // extract only the language code (first two letters) from the culture
        string languageCode = culture.Length >= 2 ? culture[..2] : culture;
        LanguageCode = languageCode;

        // get current localization
        var dictionaries = App.Current.Resources.MergedDictionaries;

        // remove all localization dictionaries except the default one (en-US)
        foreach (var dictionary in dictionaries.ToList())
        {
            if (dictionary.Source != null
                && dictionary.Source.OriginalString.StartsWith("Resources/Localization/")
                && !dictionary.Source.OriginalString.EndsWith("Dictionary-en-US.xaml"))
            {
                dictionaries.Remove(dictionary);
            }
        }

        Logger.Info("Applying localization for language: " + culture);

        // change flow direction of all windows
        ApplyFlowDirection(languageCode);

        ApplyFontFamily(culture);

        // if English, the default (en-US) is already loaded, so no need to add another dictionary
        if (culture == "en-US" || (culture == "en" && languageCode == "en")) return;

        // Try loading the full culture dictionary first (e.g., Dictionary-zh-TW.xaml)
        string localizationDictPath = $"Resources/Localization/Dictionary-{culture}.xaml";
        if (TryLoadDictionary(dictionaries, localizationDictPath))
        {
            Logger.Info("Successfully loaded localization for: " + culture);
            goto PostLoad;
        }

        // If not found, try the simplified language code (e.g., Dictionary-zh.xaml)
        localizationDictPath = $"Resources/Localization/Dictionary-{languageCode}.xaml";
        if (TryLoadDictionary(dictionaries, localizationDictPath))
        {
            Logger.Info("Successfully loaded localization for simplified code: " + languageCode);
            goto PostLoad;
        }

        // special cases for regional variants if base not found
        if (languageCode == "zh")
        {
            // Default to CN if zh-TW not found and zh not found
            TryLoadDictionary(dictionaries, "Resources/Localization/Dictionary-zh-CN.xaml");
        }
        else if (languageCode == "pt")
        {
            // Default to BR if pt-PT not found
            TryLoadDictionary(dictionaries, "Resources/Localization/Dictionary-pt-BR.xaml");
        }

        Logger.Warn("Localization file not found for language: " + culture + ". Falling back to English.");

    PostLoad:

        //Calculate the Lock Key Flyout text's Max Lenght
        List<double> Lengths = new List<double>();

        Lengths.Add(StringWidth.GetStringWidth(Application.Current.TryFindResource("LockWindow_InsertPressed")?.ToString() ?? "Insert Pressed"));

        var On = Application.Current.TryFindResource("LockWindow_LockOn")?.ToString() ?? string.Empty;
        var Off = Application.Current.TryFindResource("LockWindow_LockOff")?.ToString() ?? string.Empty;
        var OnOffMax = On.Length >= Off.Length ? On + " " : Off + " ";

        Lengths.Add(StringWidth.GetStringWidth(OnOffMax + (Application.Current.TryFindResource("LockWindow_CapsLock")?.ToString() ?? "Caps Lock")));
        Lengths.Add(StringWidth.GetStringWidth(OnOffMax + (Application.Current.TryFindResource("LockWindow_NumLock")?.ToString() ?? "Num Lock")));
        Lengths.Add(StringWidth.GetStringWidth(OnOffMax + (Application.Current.TryFindResource("LockWindow_ScrollLock")?.ToString() ?? "Scroll Lock")));

        maxLength = Lengths.Max();

        // set minimum just in case if resources weren't loaded
        if (maxLength < 20)
            maxLength = 115; // 160 (default width) - 45 (estimated padding)
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

    private static bool TryLoadDictionary(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, string path)
    {
        try
        {
            var uri = new Uri(path, UriKind.Relative);
            var resourceDict = new ResourceDictionary() { Source = uri };
            dictionaries.Add(resourceDict);
            return true;
        }
        catch
        {
            return false;
        }
    }
}