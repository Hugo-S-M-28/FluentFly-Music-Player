using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Controls;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Windows;
using MicaWPF.Controls;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Extensions;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Windows.ApplicationModel;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;
using static WindowsMediaController.MediaManager;
using FluentFlyoutWPF.Models;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Coordinators;


namespace FluentFlyoutWPF;

public partial class MainWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly SystemHookService _systemHookService = new();
    private readonly IExternalMediaService _externalMediaService = App.GetRequiredService<IExternalMediaService>();

    internal CancellationTokenSource cts; // to close the flyout after a certain time
    internal long _lastFlyoutTime = 0;

    // Fix: Add cancellation token for the settings event listener thread to prevent memory leak
    private readonly CancellationTokenSource _settingsListenerCts = new();

    // for detecting changes in settings (lazy way)
    private int _position = SettingsManager.Current.Position;
    private bool _layout = SettingsManager.Current.CompactLayout;
    private bool _repeatEnabled = SettingsManager.Current.RepeatEnabled;
    private bool _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
    private bool _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
    private bool _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
    private bool _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
    private bool _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    private bool _mediaSessionSupportsSeekbar = false; // default off to handle initialization
    private int _themeOption = SettingsManager.Current.AppTheme;

    internal bool IsInternalPlayerActive => PlaybackSourceResolver.Resolve().Kind == PlaybackSourceKind.Internal;

    static Mutex singleton = new Mutex(true, "FluentFlyout"); // to prevent multiple instances of the app
    private readonly int _seekbarUpdateInterval = 300;
    private readonly Timer _positionTimer;
    private bool _isActive;
    private bool _isDragging;
    internal bool _isHiding = true;

    private DateTime _lastSelfUpdateTimestamp = DateTime.MinValue;

    private readonly TaskbarIntegrationCoordinator _taskbarCoordinator;
    private readonly NextUpCoordinator _nextUpCoordinator;
    private readonly LockKeyFlyoutCoordinator _lockKeyFlyoutCoordinator;
    private readonly PlaybackSessionCoordinator _playbackSessionCoordinator;
    private readonly MainWindowAccentColorApplier _accentColorApplier;
    internal TaskbarWindow? taskbarWindow { get => _taskbarCoordinator?.TaskbarWindow; set { } }
    private string _lastTrackId = string.Empty;
    private ImageSource? _lastAlbumArt = null;

    internal static volatile bool ExplorerRestarting = false;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow(MainWindowViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        WindowHelper.SetNoActivate(this); // prevents some fullscreen apps from minimizing
        InitializeComponent();
        var windowManager = App.GetRequiredService<IWindowManager>();
        var playbackService = App.GetRequiredService<IPlaybackService>();
        var playbackSourceResolver = App.GetRequiredService<IPlaybackSourceResolver>();
        var libraryService = App.GetRequiredService<ILibraryService>();
        var settingsService = App.GetRequiredService<ISettingsService>();

        _taskbarCoordinator = new TaskbarIntegrationCoordinator(
            this,
            windowManager,
            playbackService,
            playbackSourceResolver,
            libraryService);
        _nextUpCoordinator = new NextUpCoordinator(
            windowManager,
            libraryService,
            settingsService,
            Dispatcher,
            () => IsVisible);
        _lockKeyFlyoutCoordinator = new LockKeyFlyoutCoordinator(windowManager);
        _playbackSessionCoordinator = new PlaybackSessionCoordinator(
            playbackService,
            _externalMediaService,
            HandleInternalTrackChanged,
            _nextUpCoordinator.ShowInternalPreview,
            HandleInternalPlaybackStateChanged,
            CurrentSession_OnPlaybackStateChanged,
            MediaManager_OnAnyMediaPropertyChanged,
            MediaManager_OnAnyTimelinePropertyChanged,
            MediaManager_OnAnySessionClosed);
        _accentColorApplier = new MainWindowAccentColorApplier(this);
        WindowHelper.SetTopmost(this); // more prevention of fullscreen apps minimizing

        if (!singleton.WaitOne(TimeSpan.Zero, true)) // if another instance is already running, close this one
        {
            // Signal the existing instance to open settings
            Task.Run(() =>
            {
                try
                {
                    using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                    {
                        settingsEvent.Set();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to signal existing instance");
                }
            });

            Environment.Exit(0);
        }

        Logger.Info("Starting FluentFlyout MainWindow");

        // in the existing instance, listen for the signal to open settings
        Task.Run(() =>
        {
            try
            {
                using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                {
                    // Fix: Check cancellation token to allow graceful shutdown
                    WaitHandle[] waitHandles = [settingsEvent, _settingsListenerCts.Token.WaitHandle];
                    while (!_settingsListenerCts.IsCancellationRequested)
                    {
                        int index = WaitHandle.WaitAny(waitHandles, 100);
                        if (index == 0) // settingsEvent
                        {
                            Application.Current.Dispatcher.Invoke(() => { App.GetRequiredService<IWindowManager>().ShowSettings(); });
                        }
                        // If index == 1, it's the cancellation token, loop will exit
                        // If index == WaitHandle.WaitTimeout, loop continues
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, ignore
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Settings event listener error");
            }
        }, _settingsListenerCts.Token);

        if (SettingsManager.Current.Startup == true) // add to startup programs if enabled, needs improvement
        {
            RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            string? executablePath = Environment.ProcessPath;
            if (executablePath != null) key?.SetValue("FluentFlyout", executablePath);
        }

        // Tray icon initialization is deferred to MicaWindow_Loaded
        // where the window has a valid HWND and Register() can succeed

        cts = new CancellationTokenSource();

        _playbackSessionCoordinator.Initialize();

        HotKeyService.Instance.HotKeyPressed += OnHotKeyPressed;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = int.MinValue / 2; // move window off-screen to prevent flash before the animation starts (DPI-safe)
        CustomWindowChrome.CaptionHeight = 0; // hide the title bar

        // System hooks
        _systemHookService.MediaOrVolumeCommandReceived += (s, isVolume) =>
        {
            if (!isVolume || !SettingsManager.Current.MediaFlyoutVolumeKeysExcluded)
                TryShowMediaFlyoutDebounced();
        };
        _systemHookService.ExplorerRestarted += OnExplorerRestarted;
        _systemHookService.ThemeChanged += (s, e) => ThemeManager.UpdateTaskbarWidget();
        _systemHookService.Initialize(this);

        _positionTimer = new Timer(SeekbarUpdateUi, null, Timeout.Infinite, Timeout.Infinite);

        if (_seekBarEnabled)
        {
            var resolved = PlaybackSourceResolver.Resolve();
            if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
                UpdateSeekbarCurrentDuration(resolved.ExternalSession.ControlSession.GetTimelineProperties().Position);
            else if (resolved.Kind == PlaybackSourceKind.Internal)
                UpdateSeekbarCurrentDuration(MusicPlayerService.Instance.CurrentPosition);
        }

        // apply other things on new thread
        Dispatcher.Invoke(() =>
        {
            LocalizationManager.ApplyLocalization();
            // show settings to new users
            string previousVersion = SettingsManager.Current.LastKnownVersion;
            if (previousVersion == string.Empty)
                App.GetRequiredService<IWindowManager>().ShowSettings();

            try // update last known version. gets the version of the app, works only in release mode
            {
                var version = Package.Current.Id.Version;
                SettingsManager.Current.LastKnownVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
            catch (InvalidOperationException ex)
            {
                // Only catch specific exception when running outside MSIX container
                // This is expected in debug mode or when running as standalone exe
                Logger.Warn(ex, "Failed to detect package version (running outside MSIX container)");
                SettingsManager.Current.LastKnownVersion = "debug";
            }
            catch (Exception ex)
            {
                // Catch any other unexpected exceptions but still log them
                Logger.Error(ex, "Unexpected error detecting package version");
                SettingsManager.Current.LastKnownVersion = "debug";
            }

            Logger.Info($"Current version: {SettingsManager.Current.LastKnownVersion}");

            Notifications.ShowFirstOrUpdateNotification(previousVersion, SettingsManager.Current.LastKnownVersion);
            FlowDirection = SettingsManager.Current.FlowDirection;

            // check for updates on startup
            _ = CheckForUpdatesOnStartupAsync();
        });

        RegisterMessages();
        ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault());
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<ShowMediaFlyoutMessage>(this, (r, m) =>
        {
            _ = Dispatcher.InvokeAsync(() => ShowMediaFlyoutAsync(m.ToggleMode, m.ForceShow));
        });

        WeakReferenceMessenger.Default.Register<RecreateTaskbarWindowMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(RecreateTaskbarWindow);
        });

        WeakReferenceMessenger.Default.Register<RecreateTrayIconMessage>(this, (r, m) =>
        {
            _ = Dispatcher.InvokeAsync(async () => await RecreateTrayIconAsync());
        });

        WeakReferenceMessenger.Default.Register<RequestApplicationShutdownMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(ShutdownApplication);
        });



        WeakReferenceMessenger.Default.Register<UpdateTaskbarMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(UpdateTaskbar);
        });

        WeakReferenceMessenger.Default.Register<UpdateUILayoutMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(UpdateUILayout);
        });

        WeakReferenceMessenger.Default.Register<ReorderTaskbarWidgetControlsMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() => _taskbarCoordinator.TaskbarWindow?.Widget?.ReorderControls());
        });

        WeakReferenceMessenger.Default.Register<TrayIconStateMessage>(this, (r, m) =>
        {
            _ = Dispatcher.InvokeAsync(async () => await RecreateTrayIconAsync());
        });

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, (r, m) =>
        {
            Dispatcher.InvokeAsync(() => ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault()));
        });
    }

    private void HandleInternalPlaybackStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateTaskbar();
            if (IsVisible)
            {
                var status = MusicPlayerService.Instance.IsPlaying ?
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing :
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                HandlePlayBackState(status);
                UpdateUI();
            }
        });
    }

    private void HandleInternalTrackChanged()
    {
        if (!SettingsManager.Current.InternalPlayerEnabled) return;

        Dispatcher.Invoke(() =>
        {
            UpdateTaskbar();
            var track = MusicPlayerService.Instance.CurrentTrack;
            if (track == null) return;

            var thumbnail = track.AlbumArt;
            if (thumbnail == null && !string.IsNullOrEmpty(track.AlbumArtPath))
            {
                thumbnail = LibraryManager.Instance.GetAlbumArt(track);
                track.AlbumArt = thumbnail;
            }

            _nextUpCoordinator.CloseIfOpen();

            if (IsVisible)
            {
                UpdateUI();
                var status = MusicPlayerService.Instance.IsPlaying ?
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing :
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                HandlePlayBackState(status);
            }
        });
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await UpdateChecker.CheckForUpdatesAsync(SettingsManager.Current.LastKnownVersion);

            if (result.Success)
            {
                UpdateState.Current.IsUpdateAvailable = result.IsUpdateAvailable;
                UpdateState.Current.NewestVersion = result.NewestVersion;
                UpdateState.Current.UpdateUrl = result.UpdateUrl;
                UpdateState.Current.LastUpdateCheck = result.CheckedAt;

                if (result.IsUpdateAvailable)
                {
                    Notifications.ShowUpdateAvailableNotification(result.NewestVersion, result.UpdateUrl);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to check for updates on startup");
        }
    }

    private static async Task<GlobalSystemMediaTransportControlsSessionMediaProperties?> TryGetMediaPropertiesAsync(GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return await controlSession.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
        }
        catch (COMException ex)
        {
            Logger.Error(ex, "Failed to retrieve data from the player");
            return null;
        }
    }



    public void UpdateTaskbar() { _taskbarCoordinator?.UpdateTaskbar(); }
    private async void UpdateTaskbar_Deprecated()
    {
        if (IsInternalPlayerActive)
        {
            var track = MusicPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                var playbackStatus = MusicPlayerService.Instance.IsPlaying ?
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing :
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;

                if (track.AlbumArt == null && !string.IsNullOrEmpty(track.AlbumArtPath))
                    track.AlbumArt = LibraryManager.Instance.GetAlbumArt(track);

                BitmapHelper.SetCurrentBitmap(track.AlbumArt);
                ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);

                taskbarWindow?.UpdateUi(track.Title, track.Artist, track.AlbumArt, playbackStatus, null);
                return;
            }
        }

        var focusedSession = _externalMediaService.GetPreferredSession();

        // If we have an external session
        if (_externalMediaService.MediaManager != null && _externalMediaService.MediaManager.IsStarted && focusedSession != null)
        {
            var songInfo = await TryGetMediaPropertiesAsync(focusedSession.ControlSession);
            if (songInfo != null)
            {
                var playbackInfo = focusedSession.ControlSession.GetPlaybackInfo();
                var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
                taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);
                return;
            }
        }

        // Fallback: If internal player is enabled and has a track, use its data directly
        // This is necessary if GSMTC loopback doesn't work for the current process
        if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            var track = MusicPlayerService.Instance.CurrentTrack;
            var playbackStatus = MusicPlayerService.Instance.IsPlaying ?
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing :
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;

            // Ensure image is loaded for dominant color extraction
            if (track.AlbumArt == null && !string.IsNullOrEmpty(track.AlbumArtPath))
                track.AlbumArt = LibraryManager.Instance.GetAlbumArt(track);

            BitmapHelper.SetCurrentBitmap(track.AlbumArt);
            ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);

            taskbarWindow?.UpdateUi(track.Title, track.Artist, track.AlbumArt, playbackStatus, null);
            return;
        }

        // If no media is found, clear the taskbar widget
        taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
    }

    public void reportBug(object? sender, EventArgs e)
    {
        TrayIconService.Instance.ReportBug();
    }

    private void openRepository(object? sender, EventArgs e)
    {
        TrayIconService.Instance.OpenRepository();
    }

    public void openLogsFolder(object? sender, EventArgs e)
    {
        TrayIconService.Instance.OpenLogsFolder();
    }

    private void pauseOtherMediaSessionsIfNeeded(MediaSession mediaSession)
    {
        if (
            SettingsManager.Current.PauseOtherSessionsEnabled
            && mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            )
        {
            _ = _externalMediaService.PauseOtherSessions(mediaSession);
        }
    }

    private async void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
#if DEBUG
        Logger.Debug("Playback state changed: " + mediaSession.Id + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif     
        if (_externalMediaService.IsInternalSession(mediaSession)) return;

        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        pauseOtherMediaSessionsIfNeeded(mediaSession);

        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            UpdateTaskbar();
            if (IsVisible)
            {
                var status = MusicPlayerService.Instance.IsPlaying
                    ? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    : GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                UpdateUI();
                HandlePlayBackState(status);
            }
            return;
        }

        if (resolved.Kind != PlaybackSourceKind.External || resolved.ExternalSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var focusedSession = resolved.ExternalSession;
        var songInfo = await TryGetMediaPropertiesAsync(focusedSession.ControlSession);
        if (songInfo == null)
            return;

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
        var resolvedPlaybackInfo = focusedSession.ControlSession.GetPlaybackInfo();
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, resolvedPlaybackInfo?.PlaybackStatus, resolvedPlaybackInfo?.Controls);
        _nextUpCoordinator.RetryPendingExternal(
            focusedSession.Id,
            songInfo.Title,
            songInfo.Artist,
            thumbnail,
            resolvedPlaybackInfo?.PlaybackStatus ?? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);

        if (IsVisible)
        {
            UpdateUI();
            HandlePlayBackState(resolvedPlaybackInfo?.PlaybackStatus);
        }
    }

    // for determining whether MediaPropertyChanged has no changes
    private string previousMediaProperty = "";
    private int previousMediaPropertyThumbnail = 0;
    private async void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
    {
        // sometimes mediaSession.ControlSession can be null
        if (mediaSession.ControlSession == null)
            return;

        if (_externalMediaService.IsInternalSession(mediaSession)) return;

#if DEBUG
        Logger.Debug("Media property changed: " + mediaProperties.Title + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif
        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        _nextUpCoordinator.ClearPendingForSession(mediaSession.Id);

        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            UpdateTaskbar();
            if (IsVisible)
            {
                HandlePlayBackState(MusicPlayerService.Instance.IsPlaying
                    ? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    : GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                UpdateUI();
            }
            return;
        }

        if (resolved.Kind != PlaybackSourceKind.External || resolved.ExternalSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var selectedSession = resolved.ExternalSession;
        var songInfo = await TryGetMediaPropertiesAsync(selectedSession.ControlSession);
        if (songInfo == null)
            return;

        var playbackInfo = selectedSession.ControlSession.GetPlaybackInfo();

        string check = $"External_{selectedSession.Id}_{songInfo.Title}_{songInfo.Artist}_{playbackInfo.PlaybackStatus}";
        int checkThumbnail = BitmapHelper.GetStableThumbnailHash(songInfo.Thumbnail);
        if (previousMediaProperty == check)
        {
            if (previousMediaPropertyThumbnail == checkThumbnail)
                return; // prevent multiple calls for the same song info
        }

        previousMediaProperty = check;
        previousMediaPropertyThumbnail = checkThumbnail;

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);

        pauseOtherMediaSessionsIfNeeded(selectedSession);

        _nextUpCoordinator.ShowExternalNowPlaying(
            selectedSession.Id,
            songInfo.Title,
            songInfo.Artist,
            thumbnail,
            playbackInfo.PlaybackStatus,
            allowPending: true);

        if (IsVisible)
        {
            HandlePlayBackState(playbackInfo.PlaybackStatus);
            UpdateUI();
        }
    }

    private void MediaManager_OnAnyTimelinePropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (_externalMediaService.IsInternalSession(mediaSession)) return;

        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        _lastSelfUpdateTimestamp = DateTime.Now;

        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            if (_seekBarEnabled)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!IsActive || _isDragging) return;
                    UpdateSeekbarCurrentDuration(MusicPlayerService.Instance.CurrentPosition);
                    var status = MusicPlayerService.Instance.IsPlaying ?
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing :
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                    HandlePlayBackState(status);
                });
            }
            return;
        }

        if (resolved.Kind != PlaybackSourceKind.External || resolved.ExternalSession == null)
            return;

        var session = resolved.ExternalSession;
        if (_seekBarEnabled)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsActive || _isDragging) return;

                UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
                HandlePlayBackState(session.ControlSession.GetPlaybackInfo().PlaybackStatus);
            });
        }
    }

    private async void MediaManager_OnAnySessionClosed(MediaSession mediaSession)
    {
#if DEBUG
        Logger.Debug("Session closed: " + (mediaSession.Id).ToString());
#endif
        if (_externalMediaService.IsInternalSession(mediaSession)) return;

        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            UpdateTaskbar();
            if (IsVisible)
            {
                UpdateUI();
                HandlePlayBackState(MusicPlayerService.Instance.IsPlaying
                    ? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    : GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
            }
            return;
        }

        if (resolved.Kind != PlaybackSourceKind.External || resolved.ExternalSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
        }
        else
        {
            var focusedSession = resolved.ExternalSession;
            var songInfo = await TryGetMediaPropertiesAsync(focusedSession.ControlSession);
            if (songInfo == null)
                return;

            var playbackInfo = focusedSession.ControlSession.GetPlaybackInfo();
            var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
            ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
            taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);
        }
    }

    private async void OnHotKeyPressed(object? sender, HotKeyEventArgs e)
    {
        int vkCode = e.VkCode;
        bool isKeyDown = e.IsKeyDown;

        bool mediaKeysPressed = vkCode == 0xB3 || vkCode == 0xB0 || vkCode == 0xB1 || vkCode == 0xB2; // Play/Pause, next, previous, stop
        bool volumeKeysPressed = vkCode == 0xAD || vkCode == 0xAE || vkCode == 0xAF; // Mute, Volume Down, Volume Up

        if (isKeyDown && (mediaKeysPressed || (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeysPressed)))
        {
            TryShowMediaFlyoutDebounced();
        }

        await _lockKeyFlyoutCoordinator.HandleHotKeyAsync(vkCode, isKeyDown);
    }

    // show the media flyout with debounce
    private bool TryShowMediaFlyoutDebounced()
    {
        long currentTime = Environment.TickCount64;
        // debounce to prevent hangs with rapid key presses
        if ((currentTime - _lastFlyoutTime) < 200) // Reduced from 500ms to 200ms
        {
            return false;
        }
        _lastFlyoutTime = currentTime;
        _ = ShowMediaFlyoutAsync();
        return true;
    }

    public async Task ShowMediaFlyoutAsync(bool toggleMode = false, bool forceShow = false)
    {
        // If in toggle mode and flyout is visible, close it
        if (toggleMode && Visibility == Visibility.Visible && !_isHiding)
        {
            _isHiding = true;
            FlyoutAnimationService.CloseAnimation(this);
            cts.Cancel();
            await Task.Delay(FlyoutAnimationService.GetDuration());
            if (_isHiding)
            {
                Hide();
                if (_seekBarEnabled)
                    HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                _isHiding = false;
            }
            return;
        }

        var resolved = PlaybackSourceResolver.Resolve();
        var focusedSession = resolved.Kind == PlaybackSourceKind.External ? resolved.ExternalSession : null;
        bool hasInternalTrack = resolved.Kind == PlaybackSourceKind.Internal;

        if (resolved.Kind == PlaybackSourceKind.None)
            return;

        if (!forceShow && !SettingsManager.Current.MediaFlyoutEnabled)
        {
            Logger.Debug("ShowMediaFlyout: Suppressed because MediaFlyoutEnabled is false.");
            return;
        }

        if (FullscreenDetector.IsFullscreenApplicationRunning())
        {
            Logger.Debug("ShowMediaFlyout: Suppressed because a fullscreen application is running.");
            return;
        }

        UpdateUI();

        if (_seekBarEnabled)
        {
            if (focusedSession != null)
                HandlePlayBackState(focusedSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
            else if (hasInternalTrack)
                HandlePlayBackState(MusicPlayerService.Instance.IsPlaying ?
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing :
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
        }

        _nextUpCoordinator.CloseIfOpen(ignoreSafeguard: true);

        bool needsAnimation = Visibility != Visibility.Visible || _isHiding;
        Visibility = Visibility.Visible;
        if (needsAnimation)
        {
            _isHiding = false;
            FlyoutAnimationService.OpenAnimation(this);
        }
        cts?.Cancel();
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        WindowHelper.SetTopmost(this);
        this.EnableBackdrop();

        // Start the auto-close loop so the flyout hides after the configured duration
        _ = RunFlyoutLoop(token);
    }

    private void openSettings(object? sender, EventArgs e)
    {
        TrayIconService.Instance.OpenSettings();
    }

    private async Task RunFlyoutLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token); // check if mouse is over every 100ms
                if (!IsMouseOver && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    await Task.Delay(SettingsManager.Current.Duration, token);
                    if (!IsMouseOver)
                    {
                        _isHiding = true;
                        FlyoutAnimationService.CloseAnimation(this);
                        await Task.Delay(FlyoutAnimationService.GetDuration());
                        if (_isHiding == false) return;
                        Hide();
                        if (_seekBarEnabled)
                            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // task was canceled, do nothing
        }
    }

    private void UpdateMediaFlyoutCloseButtonVisibility()
    {
        ViewModel.UpdateCloseButtonVisibility(SettingsManager.Current.MediaFlyoutAlwaysDisplay, SettingsManager.Current.CompactLayout);
    }

    internal void UpdatePlaybackStateTimer(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        HandlePlayBackState(status);
    }

    internal void CloseNextUpWindowIfOpen()
    {
        _nextUpCoordinator.CloseIfOpen(ignoreSafeguard: true);
    }

    internal async void UpdateUI()
    {
        if (_layout != SettingsManager.Current.CompactLayout ||
            _shuffleEnabled != SettingsManager.Current.ShuffleEnabled ||
            _repeatEnabled != SettingsManager.Current.RepeatEnabled ||
            _playerInfoEnabled != SettingsManager.Current.PlayerInfoEnabled ||
            _centerTitleArtist != SettingsManager.Current.CenterTitleArtist ||
            _seekBarEnabled != SettingsManager.Current.SeekbarEnabled ||
            _alwaysDisplay != SettingsManager.Current.MediaFlyoutAlwaysDisplay)
            UpdateUILayout();

        await Dispatcher.InvokeAsync(async () =>
        {
            ViewModel.UpdateCloseButtonVisibility(SettingsManager.Current.MediaFlyoutAlwaysDisplay, SettingsManager.Current.CompactLayout);

            var resolved = PlaybackSourceResolver.Resolve();
            bool isInternal = resolved.Kind == PlaybackSourceKind.Internal;
            var focusedSession = resolved.ExternalSession;

            if (isInternal)
            {
                var track = MusicPlayerService.Instance.CurrentTrack;
                if (track != null)
                {
                    string trackId = track.FilePath + track.Title;
                    bool trackChanged = _lastTrackId != trackId;

                    if (trackChanged)
                    {
                        if (track.AlbumArt == null && !string.IsNullOrEmpty(track.AlbumArtPath))
                            track.AlbumArt = LibraryManager.Instance.GetAlbumArt(track);

                        var image = track.AlbumArt;

                        BitmapHelper.SetCurrentBitmap(image);
                        BitmapHelper.GetDominantColors(1);
                        ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault());

                        _lastTrackId = trackId;
                        _lastAlbumArt = image;
                    }

                    if (_seekBarEnabled)
                    {
                        if (!_mediaSessionSupportsSeekbar)
                        {
                            _mediaSessionSupportsSeekbar = true;
                            UpdateUILayout();
                        }
                    }

                    ViewModel.ClearMediaIdentity();
                    return;
                }
            }

            if (resolved.Kind != PlaybackSourceKind.External || focusedSession == null)
            {
                _lastTrackId = string.Empty;
                _lastAlbumArt = null;
                ViewModel.ClearMediaIdentity();
                return;
            }

            var controlSession = focusedSession.ControlSession;
            if (controlSession == null) return;

            var playbackInfo = controlSession.GetPlaybackInfo();
            var songInfo = await TryGetMediaPropertiesAsync(controlSession);

            string extTrackId = (songInfo?.Title ?? "") + (songInfo?.Artist ?? "");
            bool extTrackChanged = _lastTrackId != extTrackId;

            if (extTrackChanged && songInfo != null)
            {
                var image = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
                _lastTrackId = extTrackId;
                _lastAlbumArt = image;
            }

            if (_seekBarEnabled && playbackInfo != null)
            {
                var timeline = controlSession.GetTimelineProperties();
                bool sessionSupportsSeekbar = timeline.MaxSeekTime.TotalSeconds >= 1.0;

                if (_mediaSessionSupportsSeekbar != sessionSupportsSeekbar)
                {
                    _mediaSessionSupportsSeekbar = sessionSupportsSeekbar;
                    UpdateUILayout();
                    _isHiding = true;
                    _ = ShowMediaFlyoutAsync(forceShow: true);
                }
            }

            if (SettingsManager.Current.PlayerInfoEnabled && !SettingsManager.Current.CompactLayout)
            {
                (string title, ImageSource? Icon) = MediaPlayerData.getMediaPlayerData(focusedSession.Id);
                ViewModel.SetMediaIdentity(title, Icon);
            }
            else
            {
                ViewModel.ClearMediaIdentity();
            }
        });
    }

    private void UpdateUILayout() // update the layout based on the settings
    {
        Dispatcher.Invoke(() =>
        {
            bool showRepeat = SettingsManager.Current.RepeatEnabled && !SettingsManager.Current.CompactLayout;
            bool showShuffle = SettingsManager.Current.ShuffleEnabled && !SettingsManager.Current.CompactLayout;
            bool showPlayerInfo = SettingsManager.Current.PlayerInfoEnabled && !SettingsManager.Current.CompactLayout;
            int compactWindowWidth = CalculateCompactWindowWidth();
            int compactBodyWidth = compactWindowWidth - 96;
            int normalBodyWidth = CalculateNormalBodyWidth(showRepeat, showShuffle, showPlayerInfo);
            int normalWindowWidth = normalBodyWidth + 126;
            int extraHeight = (SettingsManager.Current.SeekbarEnabled && _mediaSessionSupportsSeekbar) || IsInternalPlayerActive ? 36 : 0;

            if (SettingsManager.Current.CompactLayout) // compact layout
            {
                Height = 64 + extraHeight;
                Width = compactWindowWidth;
                MainStackPanel.Margin = new Thickness(12, 4, 12, 4);
                SongTitle.TextWrapping = TextWrapping.Wrap;
                SongTitle.MaxHeight = 36;
                SongArtist.TextWrapping = TextWrapping.NoWrap;
                SongInfoStackPanel.MaxWidth = 152;

                SongImageBorder.Height = 48;
                SongImageBorder.Width = 48;
                SongImageBorder.Margin = new Thickness(0);

                // In compact mode: Title/Artist and Controls side-by-side
                Grid.SetRow(SongInfoStackPanel, 0);
                Grid.SetColumn(SongInfoStackPanel, 0);
                Grid.SetColumnSpan(SongInfoStackPanel, 1);

                Grid.SetRow(ControlsStackPanel, 0);
                Grid.SetColumn(ControlsStackPanel, 1);
                Grid.SetColumnSpan(ControlsStackPanel, 1);
                ControlsStackPanel.Margin = new Thickness(12, 0, 0, 0);
                ControlsStackPanel.VerticalAlignment = VerticalAlignment.Center;

                BodyGrid.Width = compactBodyWidth;
                BodyGrid.Margin = new Thickness(12, 0, 0, 0);

                BodyContentColumn.Width = new GridLength(1, GridUnitType.Star);
                BodyControlsColumn.Width = GridLength.Auto;
            }
            else // normal layout
            {
                Height = 112 + extraHeight;
                Width = normalWindowWidth;
                MainStackPanel.Margin = new Thickness(12);
                SongTitle.TextWrapping = TextWrapping.Wrap;
                SongTitle.MaxHeight = 54;
                SongArtist.TextWrapping = TextWrapping.NoWrap;
                SongInfoStackPanel.MaxWidth = Math.Max(160, normalBodyWidth - 12);

                SongImageBorder.Height = 78;
                SongImageBorder.Width = 78;
                SongImageBorder.Margin = new Thickness(0);

                // In normal mode: Title/Artist on top, Controls below
                Grid.SetRow(SongInfoStackPanel, 0);
                Grid.SetColumn(SongInfoStackPanel, 0);
                Grid.SetColumnSpan(SongInfoStackPanel, 2);

                Grid.SetRow(ControlsStackPanel, 1);
                Grid.SetColumn(ControlsStackPanel, 0);
                Grid.SetColumnSpan(ControlsStackPanel, 2);
                ControlsStackPanel.Margin = new Thickness(0, 8, 0, 0);
                ControlsStackPanel.VerticalAlignment = VerticalAlignment.Top;

                BodyGrid.Width = normalBodyWidth;
                BodyGrid.Margin = new Thickness(12, 0, 0, 0);
                BodyContentColumn.Width = new GridLength(1, GridUnitType.Star);
                BodyControlsColumn.Width = GridLength.Auto;
            }

            if (SettingsManager.Current.CenterTitleArtist)
            {
                SongInfoStackPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                SongTitle.HorizontalAlignment = HorizontalAlignment.Center;
                SongArtist.HorizontalAlignment = HorizontalAlignment.Center;
                SongTitle.TextAlignment = TextAlignment.Center;
                SongArtist.TextAlignment = TextAlignment.Center;
            }
            else
            {
                SongInfoStackPanel.HorizontalAlignment = HorizontalAlignment.Left;
                SongTitle.HorizontalAlignment = HorizontalAlignment.Left;
                SongArtist.HorizontalAlignment = HorizontalAlignment.Left;
                SongTitle.TextAlignment = TextAlignment.Left;
                SongArtist.TextAlignment = TextAlignment.Left;
            }

            ViewModel.IsSeekbarVisible = (SettingsManager.Current.SeekbarEnabled && _mediaSessionSupportsSeekbar) || IsInternalPlayerActive;
            ViewModel.UpdateCloseButtonVisibility(SettingsManager.Current.MediaFlyoutAlwaysDisplay, SettingsManager.Current.CompactLayout);

            this.EnableBackdrop();

            if (SettingsManager.Current.CompactLayout)
                AdjustCompactLayoutHeight(extraHeight);
            else
                AdjustNormalLayoutHeight(extraHeight);
        });

        _layout = SettingsManager.Current.CompactLayout;
        _repeatEnabled = SettingsManager.Current.RepeatEnabled;
        _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
        _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
        _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
        _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
        _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    }

    private static int CalculateCompactWindowWidth()
    {
        const int compactImageWidth = 48;
        const int compactBodyGap = 12;
        const int compactOuterPadding = 36;
        const int compactControlsWidth = 156;
        const int compactTextWidth = 140;

        return compactImageWidth + compactBodyGap + compactControlsWidth + compactTextWidth + compactOuterPadding;
    }

    private static int CalculateNormalBodyWidth(bool showRepeat, bool showShuffle, bool showPlayerInfo)
    {
        const int bodyLeftMargin = 12;
        const int textBlockMinWidth = 168;
        const int seekbarContentWidth = 248;

        int controlsWidth = 126; // back + play/pause + next with margins
        if (showRepeat)
            controlsWidth += 40;
        if (showShuffle)
            controlsWidth += 40;
        if (showPlayerInfo)
            controlsWidth += 72;

        return Math.Max(seekbarContentWidth, Math.Max(textBlockMinWidth + bodyLeftMargin, controlsWidth));
    }

    private void AdjustCompactLayoutHeight(int extraHeight)
    {
        Dispatcher.BeginInvoke(() =>
        {
            const double singleLineTitleHeight = 18;
            const double compactBaseHeight = 64;
            const double maxExtraTitleHeight = 18;

            double additionalTitleHeight = Math.Max(0, SongTitle.ActualHeight - singleLineTitleHeight);
            double desiredHeight = compactBaseHeight + extraHeight + Math.Min(maxExtraTitleHeight, Math.Ceiling(additionalTitleHeight));

            if (Math.Abs(Height - desiredHeight) > 0.5)
                Height = desiredHeight;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AdjustNormalLayoutHeight(int extraHeight)
    {
        Dispatcher.BeginInvoke(() =>
        {
            const double singleLineTitleHeight = 18;
            const double maxExtraTitleHeight = 36;
            const double normalBaseHeight = 112;

            double additionalTitleHeight = Math.Max(0, SongTitle.ActualHeight - singleLineTitleHeight);
            double desiredHeight = normalBaseHeight + extraHeight + Math.Min(maxExtraTitleHeight, Math.Ceiling(additionalTitleHeight));

            if (Math.Abs(Height - desiredHeight) > 0.5)
                Height = desiredHeight;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }



    private void Seekbar_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging) return;
        _isDragging = true;

        Slider slider = (Slider)sender;
        System.Windows.Point clickPosition = e.GetPosition(slider);
        double thumbWidth = slider.Template.FindName("Thumb", slider) is Thumb thumb ? thumb.ActualWidth : 0;
        double ratio = (clickPosition.X - thumbWidth / 2) / (slider.ActualWidth - thumbWidth);
        ratio = Math.Max(0, Math.Min(1, ratio));
        double targetSeconds = ratio * slider.Maximum;

        ViewModel.BeginSeekFromShell();
        ViewModel.PreviewSeekFromShell(targetSeconds);
    }

    private async void Seekbar_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await ViewModel.CommitSeekFromShellAsync(Seekbar.Value);
        _isDragging = false;
    }

    private void Seekbar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isDragging) return;
        ViewModel.PreviewSeekFromShell(e.NewValue);
    }

    private void SeekbarUpdateUi(object? sender)
    {
        // Skip if the media player recently pushed its own timeline update
        // to avoid fighting between self-updates and timer-interpolated updates
        if (DateTime.Now.Subtract(_lastSelfUpdateTimestamp).TotalSeconds < 1.5) return;

        if (!_seekBarEnabled || Visibility != Visibility.Visible || _isDragging) return;
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            if (MusicPlayerService.Instance.IsPlaying && _seekBarEnabled && Visibility == Visibility.Visible && !_isDragging)
            {
                var currentPos = MusicPlayerService.Instance.CurrentPosition;
                UpdateSeekbarCurrentDuration(currentPos);

                if (currentPos >= MusicPlayerService.Instance.TotalDuration)
                {
                    HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
                }
            }
            return;
        }

        if (resolved.Kind != PlaybackSourceKind.External || resolved.ExternalSession == null)
            return;

        var session = resolved.ExternalSession;
        var timeline = session.ControlSession.GetTimelineProperties();
        var pos = timeline.Position + (DateTime.Now - timeline.LastUpdatedTime.DateTime);
        if (pos > timeline.EndTime)
        {
            HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        UpdateSeekbarCurrentDuration(pos);
    }

    private void UpdateSeekbarCurrentDuration(TimeSpan pos)
    {
        Dispatcher.Invoke(() =>
        {
            var max = TimeSpan.FromSeconds(ViewModel.NowPlaying.SeekMaximum);
            ViewModel.NowPlaying.UpdateTimeline(pos, max);

            if (_taskbarCoordinator.TaskbarWindow?.Widget != null)
            {
                _taskbarCoordinator.TaskbarWindow.Widget.UpdateProgress(pos.TotalSeconds, ViewModel.NowPlaying.SeekMaximum);
            }
        });
    }

    private void HandlePlayBackState(GlobalSystemMediaTransportControlsSessionPlaybackStatus? status)
    {
        if (status == null) return;
        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            if (_isActive) return;
            _isActive = true;
            _positionTimer.Change(0, _seekbarUpdateInterval);
        }
        else
        {
            if (!_isActive) return;
            _isActive = false;
            _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnExplorerRestarted(object? sender, EventArgs e)
    {
        Logger.Warn("Explorer restart detected (TaskbarCreated)");

        ExplorerRestarting = true;

        // Defer recovery, do NOT touch tray/taskbar immediately
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                // Wait for Explorer to actually stabilize
                if (await WaitForExplorerReadyAsync())
                {
                    ExplorerRestarting = false;
                    Logger.Info("Explorer stabilized, resuming taskbar integration");

                    // Now it is safe to recreate tray icon
                    await RecreateTrayIconAsync();
                }
                else
                {
                    Logger.Warn("Explorer did not stabilize within timeout; keeping integration disabled");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Explorer recovery failed");
            }
        }, DispatcherPriority.Background);
    }

    private async Task<bool> WaitForExplorerReadyAsync(int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero &&
                GetWindowRect(taskbar, out NativeMethods.RECT rect) &&
                rect.Right > rect.Left &&
                rect.Bottom > rect.Top)
            {
                return true; // taskbar exists and has geometry
            }

            await Task.Delay(200);
        }

        return false;
    }

    private static readonly TrayIconCoordinator _trayIconCoordinator = new();

    /// <summary>
    /// Recreates the tray icon with retry logic for Explorer restart scenarios
    /// where the Shell may not be ready immediately.
    /// </summary>
    private Task RecreateTrayIconAsync(int maxAttempts = 3, int delayMs = 500)
    {
        return _trayIconCoordinator.RecreateTrayIconAsync(nIcon, maxAttempts, delayMs);
    }

    private void CleanupResources()
    {
        try
        {
            // dispose managed resources
            _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _positionTimer?.Dispose();
            cts?.Cancel();
            cts?.Dispose();

            // Fix: Cancel settings listener to prevent memory leak
            _settingsListenerCts?.Cancel();
            _settingsListenerCts?.Dispose();

            TaskbarVisualizerControl.DisposeVisualizer();

            HotKeyService.Instance.HotKeyPressed -= OnHotKeyPressed;
            HotKeyService.Instance.Dispose();

            _playbackSessionCoordinator?.Dispose();
            _systemHookService.Dispose();
            _externalMediaService.Stop();

            MusicPlayerService.Instance.Dispose();
            AppVolumeService.Instance.Dispose();
            SystemVolumeService.Instance.Dispose();
            LibraryManager.Instance.Dispose();

            WeakReferenceMessenger.Default.UnregisterAll(this);

            _lockKeyFlyoutCoordinator?.Dispose();
            _nextUpCoordinator?.Dispose();

            _taskbarCoordinator?.Dispose();
            // Handled by taskbar coordinator dispose

            // dispose mutex
            singleton?.Dispose();

            // flush and close NLog
            NLog.LogManager.Shutdown();
        }
        catch (ObjectDisposedException)
        {
            // harmless shutdown exceptions
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            CleanupResources();
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private void MicaWindow_MouseEnter(object sender, MouseEventArgs e) // keep the flyout open when mouse is over
    {
        _ = ShowMediaFlyoutAsync();
    }

    private void NotifyIconQuit_Click(object sender, RoutedEventArgs e)
    {
        ShutdownApplication();
    }

    private void ShutdownApplication()
    {
        try
        {
            CleanupResources();
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }


    private async void MicaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateUILayout();
        ThemeManager.ApplySavedTheme();
        Hide();

        try
        {
            await LicenseManager.Instance.InitializeAsync();

            // Sync license status from LicenseManager to SettingsManager
            // IsPremiumUnlocked is [XmlIgnore] so it doesn't trigger save
            SettingsManager.Current.IsPremiumUnlocked = LicenseManager.Instance.IsPremiumUnlocked;
            // IsStoreVersion setter triggers OnPropertyChanged -> auto-save, no explicit SaveSettings needed
            SettingsManager.Current.IsStoreVersion = LicenseManager.Instance.IsStoreVersion;

            if (!SettingsManager.Current.IsPremiumUnlocked && SettingsManager.Current.NIconHide)
            {
                SettingsManager.Current.NIconHide = false;
                Logger.Info("Tray icon hide setting was disabled because Premium is not unlocked");
            }

            Logger.Info($"License synced on startup - Store: {SettingsManager.Current.IsStoreVersion}, Premium: {SettingsManager.Current.IsPremiumUnlocked}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize license");
        }

        ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
        _taskbarCoordinator.Initialize();

        // Initialize SMTC now that the window has a valid HWND. The window handle is
        // required by Windows.Media.SystemMediaTransportControls; calling
        // GetForCurrentView() before this point raises 0x80070578 (Invalid window handle).
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            SmtcService.Initialize(hwnd);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to obtain HWND for SMTC initialization");
        }

        // Initialize tray icon after window has a valid HWND (deferred from constructor)
        await RecreateTrayIconAsync();
    }

    public void RecreateTaskbarWindow() { _taskbarCoordinator?.RecreateTaskbarWindow(); }
    private void RecreateTaskbarWindow_Deprecated()
    {
        try
        {
            Logger.Info("Recreating Taskbar Widget window");

            if (taskbarWindow != null)
            {
                try
                {
                    taskbarWindow.Close();
                }
                catch { }

                taskbarWindow = null;
            }

            taskbarWindow = App.GetRequiredService<TaskbarWindow>();
            UpdateTaskbar();

            Logger.Info("Taskbar Widget window recreated successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate Taskbar Widget window");
        }
    }

    private void nIcon_LeftClick(Wpf.Ui.Tray.Controls.NotifyIcon sender, RoutedEventArgs e) // change the behavior of the tray icon
    {
        if (SettingsManager.Current.NIconLeftClick == 0)
        {
            openSettings(sender, e);
            //Wpf.Ui.Appearance.ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica); // to change the theme
            //ThemeService themeService = new ThemeService();
            //themeService.ChangeTheme(MicaWPF.Core.Enums.WindowsTheme.Light);
        }
        else if (SettingsManager.Current.NIconLeftClick == 1) _ = ShowMediaFlyoutAsync();
    }

    private Task PauseOtherSessions(MediaSession currentMediaSession)
    {
        return _externalMediaService.PauseOtherSessions(currentMediaSession);
    }

    internal void ToggleBlur()
    {
        if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }
    }

    private void MediaFlyoutCloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Use the updated ShowMediaFlyout method with toggle mode to close the flyout
        _ = ShowMediaFlyoutAsync(toggleMode: true);
    }
    internal void ApplyAccentColor(SolidColorBrush? brush, bool notifyOthers = false) { _accentColorApplier.ApplyAccentColor(brush, notifyOthers); }
}
