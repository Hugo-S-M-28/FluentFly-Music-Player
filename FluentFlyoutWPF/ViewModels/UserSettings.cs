using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Controls;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Classes.Utils;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Xml.Serialization;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using System.ComponentModel;
using PlaybackMode = FluentFlyoutWPF.Models.PlaybackSourceMode;

namespace FluentFlyoutWPF.ViewModels;

/**
 * User Settings data model.
 */
public partial class UserSettings : ObservableObject
{
    /// <summary>
    /// Serialized playback mode for new settings files.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [XmlElement(ElementName = "PlaybackSourceMode")]
    public string PlaybackSourceModeSerialized
    {
        get => PlaybackSourceMode.ToString();
        set
        {
            if (Enum.TryParse<PlaybackMode>(value, out var mode))
            {
                _playbackSourceMode = mode;
                _playbackSourceModeLoadedFromXml = true;
            }
        }
    }

    /// <summary>
    /// Legacy migration hook for old settings.xml files.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [XmlElement(ElementName = "InternalPlayerEnabled")]
    public bool LegacyInternalPlayerEnabled
    {
        get => InternalPlayerEnabled;
        set => _legacyInternalPlayerEnabled = value;
    }

    /// <summary>
    /// Legacy migration hook for old settings.xml files.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [XmlElement(ElementName = "SystemMediaControlEnabled")]
    public bool LegacySystemMediaControlEnabled
    {
        get => SystemMediaControlEnabled;
        set => _legacySystemMediaControlEnabled = value;
    }

    [XmlIgnore]
    public PlaybackMode PlaybackSourceMode
    {
        get => _playbackSourceMode;
        set
        {
            if (!SetProperty(ref _playbackSourceMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(InternalPlayerEnabled));
            OnPropertyChanged(nameof(SystemMediaControlEnabled));
            OnPropertyChanged(nameof(IsInternalPlayerMode));
            OnPropertyChanged(nameof(IsExternalMediaControlMode));

            if (_initializing)
            {
                return;
            }

            ApplyPlaybackSourceModeChange(value);
        }
    }

    /// <summary>
    /// Enable internal music player (playing local files instead of system media)
    /// </summary>
    [XmlIgnore]
    public bool InternalPlayerEnabled
    {
        get => PlaybackSourceMode == PlaybackMode.InternalPlayer;
        set
        {
            if (value)
            {
                PlaybackSourceMode = PlaybackMode.InternalPlayer;
            }
            else if (PlaybackSourceMode == PlaybackMode.InternalPlayer)
            {
                PlaybackSourceMode = PlaybackMode.ExternalMediaControl;
            }
        }
    }

    /// <summary>
    /// Enable controlling other media applications (Spotify, YouTube, etc.)
    /// </summary>
    [XmlIgnore]
    public bool SystemMediaControlEnabled
    {
        get => PlaybackSourceMode == PlaybackMode.ExternalMediaControl;
        set
        {
            if (value)
            {
                PlaybackSourceMode = PlaybackMode.ExternalMediaControl;
            }
            else if (PlaybackSourceMode == PlaybackMode.ExternalMediaControl)
            {
                PlaybackSourceMode = PlaybackMode.InternalPlayer;
            }
        }
    }

    [XmlIgnore]
    public bool IsInternalPlayerMode => PlaybackSourceMode == PlaybackMode.InternalPlayer;

    [XmlIgnore]
    public bool IsExternalMediaControlMode => PlaybackSourceMode == PlaybackMode.ExternalMediaControl;

    /// <summary>
    /// User selected music library folders
    /// </summary>
    public ObservableCollection<string> MusicLibraryFolders { get; set; } = new ObservableCollection<string>();

    /// <summary>
    /// File or folder paths excluded from the music library
    /// </summary>
    public ObservableCollection<string> ExcludedLibraryPaths { get; set; } = new ObservableCollection<string>();

    /// <summary>
    /// Player volume (0.0 to 1.0)
    /// </summary>
    [ObservableProperty]
    public partial float Volume { get; set; } = 1.0f;

    /// <summary>
    /// Last played track file path
    /// </summary>
    public string LastPlayedTrackPath { get; set; } = string.Empty;

    /// <summary>
    /// Last playback position in seconds
    /// </summary>
    public double LastPlaybackPosition { get; set; } = 0;

    /// <summary>
    /// Use a compact layout
    /// </summary>
    [ObservableProperty]
    public partial bool CompactLayout { get; set; }

    partial void OnCompactLayoutChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WeakReferenceMessenger.Default.Send(new UpdateUILayoutMessage());
    }

    /// <summary>
    /// Flyout Target Display
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutSelectedMonitor { get; set; }

    /// <summary>
    /// Flyout position on screen
    /// </summary>
    [ObservableProperty]
    public partial int Position { get; set; }

    /// <summary>
    /// Scale for flyout animation speed
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutAnimationSpeed { get; set; }

    /// <summary>
    /// Show player information in the flyout
    /// </summary>
    [ObservableProperty]
    public partial bool PlayerInfoEnabled { get; set; }

    /// <summary>
    /// Enable repeat button
    /// </summary>
    [ObservableProperty]
    public partial bool RepeatEnabled { get; set; }

    /// <summary>
    /// Enable shuffle button
    /// </summary>
    [ObservableProperty]
    public partial bool ShuffleEnabled { get; set; }

    /// <summary>
    /// Start minimized to tray when Windows starts
    /// </summary>
    [ObservableProperty]
    public partial bool Startup { get; set; }

    /// <summary>
    /// MediaFlyout Always Display
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDurationEditable))]
    public partial bool MediaFlyoutAlwaysDisplay { get; set; }

    partial void OnMediaFlyoutAlwaysDisplayChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WeakReferenceMessenger.Default.Send(new UpdateUILayoutMessage());
    }

    [XmlIgnore] public bool IsDurationEditable => !MediaFlyoutAlwaysDisplay;

    /// <summary>
    /// Flyout display duration (milliseconds)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    public partial int Duration { get; set; }

    [XmlIgnore]
    public string DurationText
    {
        get => Duration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                Duration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                Duration = 3000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Enable the 'Next Up' flyout (experimental)
    /// </summary>
    [ObservableProperty]
    public partial bool NextUpEnabled { get; set; }

    /// <summary>
    /// 'Next Up' flyout display duration (milliseconds)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextUpDurationText))]
    public partial int NextUpDuration { get; set; }

    [XmlIgnore]
    public string NextUpDurationText
    {
        get => NextUpDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                NextUpDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                NextUpDuration = 2000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Tray icon left-click behavior
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "nIconLeftClick")]
    public partial int NIconLeftClick { get; set; }

    /// <summary>
    /// Center the title and artist text
    /// </summary>
    [ObservableProperty]
    public partial bool CenterTitleArtist { get; set; }

    partial void OnCenterTitleArtistChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WeakReferenceMessenger.Default.Send(new UpdateUILayoutMessage());
    }

    /// <summary>
    /// Animation easing style index
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutAnimationEasingStyle { get; set; }

    /// <summary>
    /// Enable lock keys flyout (shows Caps/Num/Scroll status)
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysEnabled { get; set; }

    /// <summary>
    /// Lock keys flyout display duration (milliseconds)
    /// </summary>

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockKeysDurationText))]
    public partial int LockKeysDuration { get; set; }

    [XmlIgnore]
    public string LockKeysDurationText
    {
        get => LockKeysDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                LockKeysDuration = result switch
                {
                    > 10000 => 10000,
                    < 250 => 250,
                    _ => result
                };
            }
            else
            {
                LockKeysDuration = 2000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// App theme. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    [ObservableProperty]
    public partial int AppTheme { get; set; }

    /// <summary>
    /// Enable media flyout
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutEnabled { get; set; }

    /// <summary>
    /// Whether the equalizer is enabled
    /// </summary>
    [ObservableProperty]
    public partial bool EqualizerEnabled { get; set; } = true;

    /// <summary>
    /// Name of the active EQ preset
    /// </summary>
    [ObservableProperty]
    public partial string ActiveEqPresetName { get; set; } = "Normal";

    /// <summary>
    /// Custom gains for the 10 EQ bands
    /// </summary>
    [ObservableProperty]
    public partial float[] EqualizerGains { get; set; } = new float[10];

    /// <summary>
    /// Exclude volume keys from triggering media flyout
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutVolumeKeysExcluded { get; set; }

    /// <summary>
    /// Use symbol-style tray icon
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "nIconSymbol")]
    public partial bool NIconSymbol { get; set; }

    /// <summary>
    /// Hide tray icon completely
    /// </summary>
    [ObservableProperty]
    public partial bool NIconHide { get; set; }

    /// <summary>
    /// Disable flyout when a DirectX exclusive fullscreen app is detected
    /// </summary>
    [ObservableProperty]
    public partial bool DisableIfFullscreen { get; set; }

    /// <summary>
    /// Use bold symbol and font in the lock keys flyout
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "LockKeysBoldUI")]
    public partial bool LockKeysBoldUi { get; set; }

    /// Selects which monitor to use for the lock keys flyout when multiple monitors are in use.
    /// 0 = Default behavior, 1 = Monitor containing the focused window, 2 = Monitor containing the cursor.
    [ObservableProperty]
    public partial int LockKeysMonitorPreference { get; set; }
    
    /// <summary>
    /// Determines if the user has updated to a new version
    /// </summary>
    [ObservableProperty]
    public partial string LastKnownVersion { get; set; }

    /// <summary>
    /// Show seekbar if the player supports it
    /// </summary>
    [ObservableProperty]
    public partial bool SeekbarEnabled { get; set; }

    partial void OnSeekbarEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WeakReferenceMessenger.Default.Send(new UpdateUILayoutMessage());
    }

    /// <summary>
    /// Pause other media sessions when focusing a new one
    /// </summary>
    [ObservableProperty]
    public partial bool PauseOtherSessionsEnabled { get; set; }

    /// <summary>
    /// Enable subtle animations for the lock keys flyout indicator
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysAnimated { get; set; }

    /// <summary>
    /// Show LockKeys flyout when the Insert key is pressed
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysInsertEnabled { get; set; }

    /// <summary>
    /// Preset for media flyout background blur styles
    /// </summary>
    [ObservableProperty]
    public partial int MediaFlyoutBackgroundBlur { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the flyout window
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the Next Up window
    /// </summary>
    [ObservableProperty]
    public partial bool NextUpAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the Lock Keys window
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// User's preferred app language (e.g., "system" for system default)
    /// </summary>
    [ObservableProperty]
    public partial string AppLanguage { get; set; }

    /// <summary>
    /// Language Options
    /// </summary>
    [XmlIgnore]
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = [];

    [XmlIgnore]
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial FlowDirection FlowDirection { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget is enabled
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetEnabled { get; set; }

    /// <summary>
    /// Widget Target Display
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetSelectedMonitor { get; set; }
    
    /// <summary>
    /// Autohide Widget after a few milliseconds after pause 
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetAutoHide { get; set; }

    /// <summary>
    /// Gets or sets the position of the taskbar widget, represented as an integer value.
    /// 0: Left, 1: Center, 2: Right
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetPosition { get; set; }

    /// <summary>
    /// Determines whether padding should be applied to the taskbar widget for the native Windows Widgets button
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetPadding { get; set; }

    /// <summary>
    /// Manual padding value in pixels applied to the taskbar widget
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaskbarWidgetManualPaddingText))]
    public partial int TaskbarWidgetManualPadding { get; set; }

    [XmlIgnore]
    public string TaskbarWidgetManualPaddingText
    {
        get => TaskbarWidgetManualPadding.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                TaskbarWidgetManualPadding = result switch
                {
                    > 9999 => 9999,
                    < -9999 => -9999,
                    _ => result
                };
            }
            else
            {
                TaskbarWidgetManualPadding = 0;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indication whether the taskbar widget background should have a blur effect
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetBackgroundBlur { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should be completely hidden from view when no media is playing.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetHideCompletely { get; set; }

    /// <summary>
    /// Whether taskbar widget controls (pause, previous, next) are enabled.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetControlsEnabled { get; set; }

    /// <summary>
    /// Position of the taskbar widget controls. 0: Left, 1: Right
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetControlsPosition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should play animations.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetAnimated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar visualizer is enabled.
    /// </summary>
    /// <remarks>For now, this requires Premium and Taskbar Widget to be enabled.</remarks>
    [ObservableProperty]
    public partial bool TaskbarVisualizerEnabled { get; set; }

    /// <summary>
    /// Position of the visualizer, where 0 and 1 are to the left or right of the widget.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerPosition { get; set; }

    /// <summary>
    /// Whether the visualizer is clickable to open the visualizer settings page.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerClickable { get; set; }

    /// <summary>
    /// Indicates whether the visualizer has content to display, and is not persisted since it's only relevant at runtime.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial bool TaskbarVisualizerHasContent { get; set; }

    /// <summary>
    /// The number of visualizer bars to display.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerBarCount { get; set; }

    /// <summary>
    /// Whether the visualizer should be symmetrical/mirrored.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerCenteredBars { get; set; }

    /// <summary>
    /// Gets or sets whether a bar baseline is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerBaseline { get; set; }

    /// <summary>
    /// Stereo processing mode for the visualizer. 0 = Mono (averaged L+R), 1 = Stereo Mirror (L|R).
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerStereoMode { get; set; }

    /// <summary>
    /// Gets or sets the audio sensitivity for the taskbar visualizer from 1 to 3, where 2 is the default.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerAudioSensitivity { get; set; }

    /// <summary>
    /// The audio peak level for the taskbar visualizer from 1 to 3.
    /// This is used to calibrate the visualizer bar height to the audio output.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerAudioPeakLevel { get; set; }

    /// <summary>
    /// Current audio peak level for UI visualization (0-100). Not persisted.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial double TaskbarVisualizerCurrentLevel { get; set; }

    /// <summary>
    /// Current calibrated level mapped within the user's sensitivity and peak limits (0-100). Not persisted.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial double TaskbarVisualizerCalibratedLevel { get; set; }

    /// <summary>
    /// Gets whether premium features are unlocked (runtime only, not persisted)
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial bool IsPremiumUnlocked { get; set; }

    /// <summary>
    /// Gets or sets the opacity level of the acrylic blur effect.
    /// </summary>
    [ObservableProperty]
    public partial uint AcrylicBlurOpacity { get; set; }

    [ObservableProperty]
    public partial bool UseAlbumArtAsAccentColor { get; set; }

    [ObservableProperty]
    public partial bool UseCustomAccentColor { get; set; }

    [ObservableProperty]
    public partial string CustomAccentColorHex { get; set; } = "#808080";

    [XmlIgnore]
    public bool IsCustomAccentColorHexValid
        => AccentColorResolver.TryParseCustomAccent(CustomAccentColorHex, out _);

    [XmlIgnore]
    public bool HasCustomAccentColorHexError
        => UseCustomAccentColor && !IsCustomAccentColorHexValid;

    [XmlIgnore]
    public string CustomAccentColorHexValidationMessage
        => HasCustomAccentColorHexError ? "Use #RRGGBB or #AARRGGBB." : string.Empty;

    /// <summary>
    /// Enable turntable mode in the home page
    /// </summary>
    [ObservableProperty]
    public partial bool TurntableModeEnabled { get; set; }

    /// <summary>
    /// Gets whether this is a Store version. Once false, always false (only if last known version was not null).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStoreVersion { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string PremiumPrice { get; set; }

    /// <summary>
    /// Last time the program has sent an update notification in Unix seconds.
    /// </summary>
    [ObservableProperty]
    public partial long LastUpdateNotificationUnixSeconds { get; set; }

    /// <summary>
    /// Determines whether user will get Windows notifications when a new update is available.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowUpdateNotifications { get; set; }

    /// <summary>
    /// Determines whether to use the legacy method for calculating taskbar width for widget positioning for compatibility with other taskbar mods
    /// </summary>
    [ObservableProperty]
    public partial bool LegacyTaskbarWidthEnabled { get; set; }

    /// <summary>
    /// Size of track icons in the library list view
    /// </summary>
    [ObservableProperty]
    public partial double LibraryTrackIconSize { get; set; } = 40.0;

    /// <summary>
    /// Size of album/artist items in the library grid view
    /// </summary>
    [ObservableProperty]
    public partial double LibraryGridItemSize { get; set; } = 160.0;
    
    /// <summary>
    /// Corner radius for library items
    /// </summary>
    [ObservableProperty]
    public partial double LibraryItemCornerRadius { get; set; } = 12.0;

    /// <summary>
    /// Preferred library sort property
    /// </summary>
    [ObservableProperty]
    public partial string LibrarySortProperty { get; set; } = "Title";

    /// <summary>
    /// Preferred library sort direction
    /// </summary>
    [ObservableProperty]
    public partial bool LibrarySortAscending { get; set; } = true;

    /// <summary>
    /// Last search text in library
    /// </summary>
    [ObservableProperty]
    public partial string LibrarySearchText { get; set; } = string.Empty;

    /// <summary>
    /// Last selected tab in library (0: Songs, 1: Albums, 2: Artists)
    /// </summary>
    [ObservableProperty]
    public partial int LibrarySelectedTab { get; set; } = 0;

    /// <summary>
    /// Whether the lyrics filter is active in the library
    /// </summary>
    [ObservableProperty]
    public partial bool LibraryLyricsFilterEnabled { get; set; } = false;

    /// <summary>
    /// Whether the playlist sidebar is visible in the library
    /// </summary>
    [ObservableProperty]
    public partial bool LibraryPlaylistVisible { get; set; } = false;

    [XmlIgnore]
    private bool _initializing = true;
    [XmlIgnore]
    internal bool SuppressAutoSave { get; set; }
    [XmlIgnore]
    private PlaybackMode _playbackSourceMode = PlaybackMode.InternalPlayer;
    [XmlIgnore]
    private bool _playbackSourceModeLoadedFromXml;
    [XmlIgnore]
    private bool? _legacyInternalPlayerEnabled;
    [XmlIgnore]
    private bool? _legacySystemMediaControlEnabled;

    public UserSettings()
    {
        foreach (var supportedLanguage in LocalizationManager.SupportedLanguages)
        {
            LanguageOptions.Add(new LanguageOption(supportedLanguage.Key, supportedLanguage.Value));
        }

        _playbackSourceMode = PlaybackMode.InternalPlayer;
        CompactLayout = false;
        FlyoutSelectedMonitor = 0;
        Position = 0;
        FlyoutAnimationSpeed = 2;
        PlayerInfoEnabled = true;
        RepeatEnabled = true;
        ShuffleEnabled = true;
        Startup = true;
        Duration = 3000;
        NextUpEnabled = true;
        NextUpDuration = 2000;
        NIconLeftClick = 0;
        CenterTitleArtist = true;
        FlyoutAnimationEasingStyle = 2;
        LockKeysEnabled = true;
        LockKeysDuration = 2000;
        AppTheme = 2;
        MediaFlyoutEnabled = true;
        EqualizerEnabled = true;
        ActiveEqPresetName = "Normal";
        EqualizerGains = new float[10];
        MediaFlyoutAlwaysDisplay = false;
        MediaFlyoutVolumeKeysExcluded = false;
        NIconSymbol = true;
        NIconHide = false;
        DisableIfFullscreen = true;
        LockKeysBoldUi = true;
        LockKeysMonitorPreference = 0;
        LastKnownVersion = "";
        SeekbarEnabled = true;
        PauseOtherSessionsEnabled = false;
        LockKeysAnimated = true;
        LockKeysInsertEnabled = true;
        MediaFlyoutBackgroundBlur = 3;
        MediaFlyoutAcrylicWindowEnabled = true;
        AppLanguage = "es";
        FlowDirection = FlowDirection.LeftToRight;
        FontFamily = "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI";
        NextUpAcrylicWindowEnabled = true;
        LockKeysAcrylicWindowEnabled = true;
        TaskbarWidgetEnabled = true;
        TaskbarWidgetSelectedMonitor = 0;
        TaskbarWidgetAutoHide = false;
        TaskbarWidgetPosition = 1;
        TaskbarWidgetPadding = true;
        TaskbarWidgetManualPadding = 0;
        TaskbarWidgetBackgroundBlur = true;
        TaskbarWidgetHideCompletely = false;
        TaskbarWidgetControlsEnabled = true;
        TaskbarWidgetControlsPosition = 1;
        TaskbarWidgetAnimated = true;
        TaskbarVisualizerEnabled = true;
        TaskbarVisualizerPosition = 1;
        TaskbarVisualizerClickable = true;
        TaskbarVisualizerBarCount = 12;
        TaskbarVisualizerCenteredBars = true;
        TaskbarVisualizerBaseline = true;
        TaskbarVisualizerAudioSensitivity = 6;
        TaskbarVisualizerAudioPeakLevel = 5;
        TaskbarVisualizerStereoMode = 1;
        AcrylicBlurOpacity = 175;
        UseAlbumArtAsAccentColor = true;
        UseCustomAccentColor = true;
        CustomAccentColorHex = "#00B7C3";
        LastUpdateNotificationUnixSeconds = 0;
        ShowUpdateNotifications = true;
        LegacyTaskbarWidthEnabled = false;
        LibraryTrackIconSize = 80.0;
        LibraryGridItemSize = 180.0;
        LibrarySortProperty = "Artist";
        LibrarySortAscending = true;
        LibrarySearchText = string.Empty;
        LibrarySelectedTab = 0;
        LibraryLyricsFilterEnabled = false;
        LibraryPlaylistVisible = true;
        LibraryItemCornerRadius = 12.0;
        TurntableModeEnabled = true;
    }


    /// <summary>
    /// Called after deserialization to finalize initialization
    /// </summary>
    internal void CompleteInitialization()
    {
        try
        {
            EnsureDefaultLibraryFolders();
            RemoveDuplicateLibraryEntries();
            InitializePlaybackSourceMode();
            NormalizeTrayIconSettings();
            SubscribeToCollectionChanges();
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Error during settings initialization, proceeding with partial init");
        }
        finally
        {
            _initializing = false;
        }
    }

    private void SubscribeToCollectionChanges()
    {
        MusicLibraryFolders.CollectionChanged -= MusicLibraryFolders_CollectionChanged;
        ExcludedLibraryPaths.CollectionChanged -= ExcludedLibraryPaths_CollectionChanged;

        MusicLibraryFolders.CollectionChanged += MusicLibraryFolders_CollectionChanged;
        ExcludedLibraryPaths.CollectionChanged += ExcludedLibraryPaths_CollectionChanged;
    }

    private void MusicLibraryFolders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PersistCollectionChange(nameof(MusicLibraryFolders));
    }

    private void ExcludedLibraryPaths_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PersistCollectionChange(nameof(ExcludedLibraryPaths));
    }

    private void PersistCollectionChange(string propertyName)
    {
        if (_initializing)
        {
            return;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }

    public bool ShouldSerializeLegacyInternalPlayerEnabled() => false;

    public bool ShouldSerializeLegacySystemMediaControlEnabled() => false;

    private void InitializePlaybackSourceMode()
    {
        _playbackSourceMode = _playbackSourceModeLoadedFromXml
            ? _playbackSourceMode
            : ResolvePlaybackSourceMode(_legacyInternalPlayerEnabled, _legacySystemMediaControlEnabled);
    }

    private static PlaybackMode ResolvePlaybackSourceMode(bool? legacyInternalPlayerEnabled, bool? legacySystemMediaControlEnabled)
    {
        return (legacyInternalPlayerEnabled, legacySystemMediaControlEnabled) switch
        {
            (true, false) => PlaybackMode.InternalPlayer,
            (false, true) => PlaybackMode.ExternalMediaControl,
            _ => PlaybackMode.InternalPlayer
        };
    }

    private void NormalizeTrayIconSettings()
    {
        if (NIconSymbol && NIconHide)
        {
            NIconHide = false;
        }
    }

    private void EnsureDefaultLibraryFolders()
    {
        if (MusicLibraryFolders.Count > 0)
        {
            return;
        }

        string defaultMusicFolder = global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrWhiteSpace(defaultMusicFolder))
        {
            MusicLibraryFolders.Add(defaultMusicFolder);
        }
    }

    private void RemoveDuplicateLibraryEntries()
    {
        RemoveDuplicateEntries(MusicLibraryFolders);
        RemoveDuplicateEntries(ExcludedLibraryPaths);
    }

    private static void RemoveDuplicateEntries(ObservableCollection<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = paths.Count - 1; i >= 0; i--)
        {
            string? path = paths[i]?.Trim();

            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                paths.RemoveAt(i);
                continue;
            }

            if (!string.Equals(paths[i], path, StringComparison.Ordinal))
            {
                paths[i] = path;
            }
        }
    }

    private void ApplyPlaybackSourceModeChange(PlaybackMode mode)
    {
        if (mode == PlaybackMode.ExternalMediaControl)
        {
            MusicPlayerService.Instance.Stop();
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            ExternalMediaService.Instance.UpdateStateFromSettings();
        });
    }

    partial void OnAppLanguageChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        SelectedLanguage = LanguageOptions.First(l => l.Tag == newValue);
    }

    partial void OnSelectedLanguageChanged(LanguageOption oldValue, LanguageOption newValue)
    {
        if (oldValue == newValue || _initializing) return;
        AppLanguage = newValue.Tag;
        LocalizationManager.ApplyLocalization();
    }

    /// <summary>
    /// Changes the application theme when the selection is changed. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    partial void OnAppThemeChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ThemeManager.ApplyAndSaveTheme(newValue);
    }

    partial void OnNIconSymbolChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        if (newValue && NIconHide)
        {
            NIconHide = false;
        }

        ThemeManager.UpdateTrayIcon();
    }

    partial void OnAcrylicBlurOpacityChanged(uint oldValue, uint newValue)
    {
        if (_initializing) return;
        uint clamped = Math.Min(newValue, 255u);
        if (clamped != newValue)
        {
            AcrylicBlurOpacity = clamped;
            return;
        }
        if (oldValue == clamped) return;

        if (IsPremiumUnlocked)
        {
            WindowBlurHelper.AdjustBlurOpacityForAllWindows(clamped);
        }
    }

    partial void OnMediaFlyoutAcrylicWindowEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WindowBlurHelper.RefreshAllWindowBackdrops();
    }

    partial void OnNextUpAcrylicWindowEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WindowBlurHelper.RefreshAllWindowBackdrops();
    }

    partial void OnLockKeysAcrylicWindowEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WindowBlurHelper.RefreshAllWindowBackdrops();
    }

    partial void OnMediaFlyoutBackgroundBlurChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WindowBlurHelper.RefreshAllWindowBackdrops();
    }

    partial void OnTaskbarWidgetEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // Check premium status before allowing widget to be enabled
        if (newValue && !SettingsManager.Current.IsPremiumUnlocked)
        {
            // Revert the change if premium is not unlocked
            TaskbarWidgetEnabled = false;
            return;
        }

        ExternalMediaService.Instance.UpdateStateFromSettings();
        UpdateTaskbar();
    }

    // Update taskbar when relevant settings change
    partial void OnTaskbarWidgetPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetPaddingChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetManualPaddingChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetBackgroundBlurChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetHideCompletelyChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetControlsEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetControlsPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        
        WeakReferenceMessenger.Default.Send(new ReorderTaskbarWidgetControlsMessage());
    }

    partial void OnTaskbarVisualizerPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnLegacyTaskbarWidthEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    private void UpdateTaskbar()
    {
        WeakReferenceMessenger.Default.Send(new UpdateTaskbarMessage());
    }

    partial void OnTaskbarVisualizerEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        TaskbarVisualizerControl.OnTaskbarVisualizerEnabledChanged(newValue);
        UpdateTaskbar();
    }

    partial void OnTaskbarVisualizerBarCountChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        Visualizer.ResizeBarList(newValue);
    }

    partial void OnTaskbarVisualizerBaselineChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing || newValue == false) return;
        TaskbarVisualizerHasContent = true;
    }

    partial void OnTaskbarVisualizerAudioSensitivityChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // Sensitivity controls minDb = -20 - (val * 8)
        // Peak controls maxDb = -45 + (val * 5)
        // Ensure minDb < maxDb with at least 10dB range
        float minDb = -20f - (newValue * 8f);
        float maxDb = -45f + (TaskbarVisualizerAudioPeakLevel * 5f);

        if (minDb >= maxDb - 10f)
        {
            // Adjust peak to maintain 10dB minimum range
            int requiredPeak = (int)Math.Ceiling((minDb + 10f + 45f) / 5f);
            TaskbarVisualizerAudioPeakLevel = Math.Clamp(requiredPeak, 1, 10);
        }
    }

    partial void OnTaskbarVisualizerAudioPeakLevelChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;

        float maxDb = -45f + (newValue * 5f);
        float minDb = -20f - (TaskbarVisualizerAudioSensitivity * 8f);

        if (maxDb <= minDb + 10f)
        {
            // Adjust sensitivity to maintain 10dB minimum range
            int requiredSens = (int)Math.Floor((-20f - (maxDb - 10f)) / 8f);
            TaskbarVisualizerAudioSensitivity = Math.Clamp(requiredSens, 1, 10);
        }
    }


    partial void OnUseAlbumArtAsAccentColorChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        var albumArtBrush = BitmapHelper.GetDominantColors(1).FirstOrDefault();
        var oldSource = AccentColorResolver.ResolveAccentSource(
            oldValue,
            UseCustomAccentColor,
            CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            albumArtBrush);
        var newSource = AccentColorResolver.ResolveAccentSource(
            newValue,
            UseCustomAccentColor,
            CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            albumArtBrush);

        if (oldSource != newSource)
        {
            WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
        }
    }

    partial void OnUseCustomAccentColorChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        OnPropertyChanged(nameof(HasCustomAccentColorHexError));
        OnPropertyChanged(nameof(CustomAccentColorHexValidationMessage));

        var albumArtBrush = BitmapHelper.SavedDominantColors.FirstOrDefault();
        var oldSource = AccentColorResolver.ResolveAccentSource(
            UseAlbumArtAsAccentColor,
            oldValue,
            CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            albumArtBrush);
        var newSource = AccentColorResolver.ResolveAccentSource(
            UseAlbumArtAsAccentColor,
            newValue,
            CustomAccentColorHex,
            BitmapHelper.HasAlbumArt,
            albumArtBrush);

        if (oldSource != newSource)
        {
            WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
        }
    }

    partial void OnCustomAccentColorHexChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue || _initializing) return;
        OnPropertyChanged(nameof(IsCustomAccentColorHexValid));
        OnPropertyChanged(nameof(HasCustomAccentColorHexError));
        OnPropertyChanged(nameof(CustomAccentColorHexValidationMessage));

        var albumArtBrush = BitmapHelper.SavedDominantColors.FirstOrDefault();
        var oldSource = AccentColorResolver.ResolveAccentSource(
            UseAlbumArtAsAccentColor,
            UseCustomAccentColor,
            oldValue,
            BitmapHelper.HasAlbumArt,
            albumArtBrush);
        var newSource = AccentColorResolver.ResolveAccentSource(
            UseAlbumArtAsAccentColor,
            UseCustomAccentColor,
            newValue,
            BitmapHelper.HasAlbumArt,
            albumArtBrush);

        if (oldSource != newSource)
        {
            WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
            return;
        }

        if (newSource != AccentColorSource.Custom)
            return;

        bool hadOldBrush = AccentColorResolver.TryParseCustomAccent(oldValue, out var oldBrush);
        bool hasNewBrush = AccentColorResolver.TryParseCustomAccent(newValue, out var newBrush);

        if (hadOldBrush != hasNewBrush)
        {
            WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
            return;
        }

        if (hadOldBrush && hasNewBrush && oldBrush!.Color != newBrush!.Color)
        {
            WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
        }
    }

    partial void OnMediaFlyoutEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // If the flyout is being disabled and is currently visible, close it immediately
        if (!newValue)
        {
            WeakReferenceMessenger.Default.Send(new ShowMediaFlyoutMessage(toggleMode: true));
        }
    }

    /// <summary>
    /// Automatically save settings whenever a property changes.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Don't save during initialization or for ignored properties
        if (!_initializing && 
            !SuppressAutoSave &&
            e.PropertyName != nameof(IsPremiumUnlocked) && 
            e.PropertyName != nameof(IsDurationEditable) &&
            e.PropertyName != nameof(Volume) && // Saved separately with debounce by MusicPlayerService
            e.PropertyName != nameof(LibrarySearchText) && // Avoid persisting every search keystroke
            e.PropertyName != nameof(LastPlaybackPosition) && // Avoid excessive writes during playback
            !string.IsNullOrEmpty(e.PropertyName))
        {
            SettingsManager.SaveSettings();
        }
    }

    /// <summary>
    /// Public wrapper to trigger OnPropertyChanged from external services
    /// </summary>
    public void NotifyPropertyChanged(string propertyName)
    {
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
    }
}
