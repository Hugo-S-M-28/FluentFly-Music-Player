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


namespace FluentFlyoutWPF;

public partial class MainWindow : MicaWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly SystemHookService _systemHookService = new();
    private readonly ExternalMediaService _externalMediaService = ExternalMediaService.Instance;

    private CancellationTokenSource cts; // to close the flyout after a certain time
    private long _lastFlyoutTime = 0;

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
    
    private bool IsInternalPlayerActive => SettingsManager.Current.InternalPlayerEnabled && 
                                          (MusicPlayerService.Instance.IsPlaying || MusicPlayerService.Instance.CurrentTrack != null);

    static Mutex singleton = new Mutex(true, "FluentFlyout"); // to prevent multiple instances of the app
    private NextUpWindow? nextUpWindow = null; // to prevent multiple instances of NextUpWindow
    private string currentTitle = ""; // to prevent NextUpWindow from showing the same song

    private readonly int _seekbarUpdateInterval = 300;
    private readonly Timer _positionTimer;
    private bool _isActive;
    private bool _isDragging;
    private bool _isHiding = true;

    private LockWindow? lockWindow;
    private DateTime _lastSelfUpdateTimestamp = DateTime.MinValue;

    internal TaskbarWindow? taskbarWindow;
    private string _lastTrackId = string.Empty;
    private ImageSource? _lastAlbumArt = null;
    private bool? _lastIsPlaying = null;

    internal static volatile bool ExplorerRestarting = false;
    
    public NowPlayingViewModel NowPlaying { get; } = new();

    public MainWindow()
    {
        DataContext = SettingsManager.Current;
        WindowHelper.SetNoActivate(this); // prevents some fullscreen apps from minimizing
        InitializeComponent();
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
                            Application.Current.Dispatcher.Invoke(() => { SettingsWindow.ShowInstance(); });
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

        // display tray icon if enabled
        if (!SettingsManager.Current.NIconHide && nIcon != null)
        {
            nIcon.Visibility = Visibility.Visible;
        }

        cts = new CancellationTokenSource();

        _externalMediaService.MediaPropertyChanged += MediaManager_OnAnyMediaPropertyChanged;
        _externalMediaService.PlaybackStateChanged += CurrentSession_OnPlaybackStateChanged;
        _externalMediaService.TimelinePropertyChanged += MediaManager_OnAnyTimelinePropertyChanged;
        _externalMediaService.SessionClosed += MediaManager_OnAnySessionClosed;
        _externalMediaService.Initialize();

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
        
        // Subscribe to internal player events for reliable updates even when external SMTC is disabled
        MusicPlayerService.Instance.TrackChanged += (s, e) => HandleInternalTrackChanged();
        MusicPlayerService.Instance.PropertyChanged += (s, e) => {
            if (e.PropertyName == "IsPlaying" || e.PropertyName == "CurrentTrack")
                HandleInternalPlaybackStateChanged();
        };

        if (_seekBarEnabled)
        {
            var session = _externalMediaService.GetPreferredSession();
            if (session != null)
                UpdateSeekbarCurrentDuration(session.ControlSession.GetTimelineProperties().Position);
            else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
                UpdateSeekbarCurrentDuration(MusicPlayerService.Instance.CurrentPosition);
        }

        // apply other things on new thread
        Dispatcher.Invoke(() =>
        {
            LocalizationManager.ApplyLocalization();
            // show settings to new users
            string previousVersion = SettingsManager.Current.LastKnownVersion;
            if (previousVersion == string.Empty)
                SettingsWindow.ShowInstance();

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
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<ShowMediaFlyoutMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() => ShowMediaFlyout(m.ToggleMode, m.ForceShow));
        });

        WeakReferenceMessenger.Default.Register<RecreateTaskbarWindowMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(RecreateTaskbarWindow);
        });

        WeakReferenceMessenger.Default.Register<RecreateTrayIconMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(RecreateTrayIconSafely);
        });

        WeakReferenceMessenger.Default.Register<ToggleBlurMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(ToggleBlur);
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
            Dispatcher.Invoke(() => taskbarWindow?.Widget?.ReorderControls());
        });

        WeakReferenceMessenger.Default.Register<TrayIconStateMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (m.Register) nIcon.Register();
                else nIcon.Unregister();
            });
        });

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() => ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault()));
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

            if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.IsFullscreenApplicationRunning())
            {
                void createNewNextUpWindow()
                {
                    if (nextUpWindow == null)
                    {
                        nextUpWindow = new NextUpWindow(track.Title, track.Artist, thumbnail!);
                        currentTitle = track.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null;
                    }
                }

                if (nextUpWindow == null && !IsVisible && currentTitle != track.Title)
                {
                    createNewNextUpWindow();
                }
                else if (nextUpWindow != null && currentTitle != track.Title)
                {
                    WindowHelper.SetVisibility(nextUpWindow, false);
                    nextUpWindow.Close();
                    createNewNextUpWindow();
                }
                else if (nextUpWindow != null)
                {
                    if (thumbnail != null) nextUpWindow.UpdateThumbnail(thumbnail);
                }
            }

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

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (COMException ex)
        {
            Logger.Error(ex, "Failed to retrieve data from the player");
            return null;
        }
    }



    public void UpdateTaskbar()
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
            var songInfo = TryGetMediaProperties(focusedSession.ControlSession);
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

    private void CurrentSession_OnPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
    {
#if DEBUG
        Logger.Debug("Playback state changed: " + mediaSession.Id + " " + mediaSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
#endif     
        if (_externalMediaService.IsInternalSession(mediaSession)) return;

        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        pauseOtherMediaSessionsIfNeeded(mediaSession);

        var focusedSession = _externalMediaService.GetPreferredSession();
        if (focusedSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = TryGetMediaProperties(focusedSession.ControlSession);
        if (songInfo == null)
            return;

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo?.PlaybackStatus, playbackInfo?.Controls);

        if (IsVisible)
        {
            UpdateUI();
            HandlePlayBackState(playbackInfo?.PlaybackStatus);
        }
    }

    // for determining whether MediaPropertyChanged has no changes
    private string previousMediaProperty = "";
    private int previousMediaPropertyThumbnail = 0;
    private void MediaManager_OnAnyMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
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

        if (_externalMediaService.GetPreferredSession() == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
            return;
        }

        var songInfo = TryGetMediaProperties(mediaSession.ControlSession);
        if (songInfo == null)
            return;

        var playbackInfo = mediaSession.ControlSession.GetPlaybackInfo();

        string check = songInfo.Title + songInfo.Artist + playbackInfo.PlaybackStatus;
        int checkThumbnail = BitmapHelper.GetStableThumbnailHash(songInfo.Thumbnail);
        bool onlyThumbnailChanged = false;
        if (previousMediaProperty == check)
        {
            onlyThumbnailChanged = true;
            if (previousMediaPropertyThumbnail == checkThumbnail)
                return; // prevent multiple calls for the same song info
        }

        previousMediaProperty = check;
        previousMediaPropertyThumbnail = checkThumbnail;

        var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
        ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
        taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);

        pauseOtherMediaSessionsIfNeeded(mediaSession);

        if (SettingsManager.Current.NextUpEnabled && !FullscreenDetector.IsFullscreenApplicationRunning()) // show NextUpWindow if enabled in settings
        {
            // Fix: Move all NextUpWindow logic inside Dispatcher.Invoke to prevent race conditions
            Dispatcher.Invoke(() =>
            {
                void createNewNextUpWindow()
                {
                    if (nextUpWindow == null && playbackInfo.Controls.IsPauseEnabled)
                    {
                        nextUpWindow = new NextUpWindow(songInfo.Title, songInfo.Artist, thumbnail!);
                        currentTitle = songInfo.Title;
                        nextUpWindow.Closed += (s, e) => nextUpWindow = null; // set nextUpWindow to null when closed
                    }
                }

                if (nextUpWindow == null && IsVisible == false && songInfo.Thumbnail != null && currentTitle != songInfo.Title)
                {
                    createNewNextUpWindow();
                }
                else if (nextUpWindow != null && !onlyThumbnailChanged)
                {
                    if (nextUpWindow != null)
                    {
                        WindowHelper.SetVisibility(nextUpWindow, false); // prevents rare flickering during rapid closing
                        nextUpWindow.Close(); // must be cleared by the Closed event
                    }
                    createNewNextUpWindow();
                }
                else if (nextUpWindow != null && songInfo.Thumbnail != null)
                {
                    if (thumbnail != null) nextUpWindow?.UpdateThumbnail(thumbnail);
                }
            });
        }

        if (IsVisible)
        {
            var focusedSession = _externalMediaService.GetPreferredSession();
            // Fix: Check if focusedSession is null before accessing ControlSession
            if (focusedSession?.ControlSession != null)
            {
                HandlePlayBackState(focusedSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
                UpdateUI();
            }
        }
    }

    private void MediaManager_OnAnyTimelinePropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
    {
        if (_externalMediaService.IsInternalSession(mediaSession)) return;

        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        _lastSelfUpdateTimestamp = DateTime.Now;

        if (_externalMediaService.GetPreferredSession() is not { } session)
        {
            if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null && _seekBarEnabled)
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

    private void MediaManager_OnAnySessionClosed(MediaSession mediaSession)
    {
#if DEBUG
        Logger.Debug("Session closed: " + (mediaSession.Id).ToString());
#endif
        if (_externalMediaService.IsInternalSession(mediaSession)) return;

        if (!SettingsManager.Current.SystemMediaControlEnabled)
            return;

        var focusedSession = _externalMediaService.GetPreferredSession();

        if (focusedSession == null)
        {
            taskbarWindow?.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
        }
        else
        {
            var songInfo = TryGetMediaProperties(focusedSession.ControlSession);
            if (songInfo == null)
                return;

            var playbackInfo = focusedSession.ControlSession.GetPlaybackInfo();
            var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
            ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
            taskbarWindow?.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);
        }
    }

    private void OnHotKeyPressed(object? sender, HotKeyEventArgs e)
    {
        int vkCode = e.VkCode;
        bool isKeyDown = e.IsKeyDown;

        bool mediaKeysPressed = vkCode == 0xB3 || vkCode == 0xB0 || vkCode == 0xB1 || vkCode == 0xB2; // Play/Pause, next, previous, stop
        bool volumeKeysPressed = vkCode == 0xAD || vkCode == 0xAE || vkCode == 0xAF; // Mute, Volume Down, Volume Up

        if (isKeyDown && (mediaKeysPressed || (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeysPressed)))
        {
            TryShowMediaFlyoutDebounced();
        }

        if (SettingsManager.Current.LockKeysEnabled
            && !FullscreenDetector.IsFullscreenApplicationRunning()
            && !isKeyDown) // WM_KEYUP equivalent
        {
            if (vkCode == 0x14) // Caps Lock
            {
                lockWindow ??= new LockWindow();
                lockWindow.ShowLockFlyout(FindResource("LockWindow_CapsLock").ToString(), Keyboard.IsKeyToggled(Key.CapsLock));
            }
            else if (vkCode == 0x90) // Num Lock
            {
                lockWindow ??= new LockWindow();
                lockWindow.ShowLockFlyout(FindResource("LockWindow_NumLock").ToString(), Keyboard.IsKeyToggled(Key.NumLock));
            }
            else if (vkCode == 0x91) // Scroll Lock
            {
                lockWindow ??= new LockWindow();
                lockWindow.ShowLockFlyout(FindResource("LockWindow_ScrollLock").ToString(), Keyboard.IsKeyToggled(Key.Scroll));
            }
            else if (vkCode == 0x2D && SettingsManager.Current.LockKeysInsertEnabled) // Insert
            {
                lockWindow ??= new LockWindow();
                lockWindow.ShowLockFlyout(FindResource("LockWindow_Insert").ToString(), Keyboard.IsKeyToggled(Key.Insert));
            }
        }
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
        ShowMediaFlyout();
        return true;
    }

    public async void ShowMediaFlyout(bool toggleMode = false, bool forceShow = false)
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

        var focusedSession = _externalMediaService.GetPreferredSession();
        
        bool hasInternalTrack = SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null;
        
        if (focusedSession == null && !hasInternalTrack)
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

        if (nextUpWindow != null) // close NextUpWindow if it's open
        {
            nextUpWindow.Close();
            nextUpWindow = null;
        }

        bool needsAnimation = Visibility != Visibility.Visible || _isHiding;
        Visibility = Visibility.Visible;
        if (needsAnimation)
        {
            _isHiding = false;
            FlyoutAnimationService.OpenAnimation(this);
        }
        cts.Cancel();
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
        MediaFlyoutCloseButton.Visibility = SettingsManager.Current.MediaFlyoutAlwaysDisplay && !SettingsManager.Current.CompactLayout ? Visibility.Visible : Visibility.Collapsed;
        ControlClose.Visibility = SettingsManager.Current.MediaFlyoutAlwaysDisplay && SettingsManager.Current.CompactLayout ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateUI()
    {
        if (_layout != SettingsManager.Current.CompactLayout ||
            _shuffleEnabled != SettingsManager.Current.ShuffleEnabled ||
            _repeatEnabled != SettingsManager.Current.RepeatEnabled ||
            _playerInfoEnabled != SettingsManager.Current.PlayerInfoEnabled ||
            _centerTitleArtist != SettingsManager.Current.CenterTitleArtist ||
            _seekBarEnabled != SettingsManager.Current.SeekbarEnabled ||
            _alwaysDisplay != SettingsManager.Current.MediaFlyoutAlwaysDisplay)
            UpdateUILayout();

        Dispatcher.Invoke(() =>
        {
            UpdateMediaFlyoutCloseButtonVisibility();

            bool isInternal = IsInternalPlayerActive;
            var focusedSession = _externalMediaService.GetPreferredSession();
            
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

                        var croppedImage = BitmapHelper.CropToSquare(image);
                        BackgroundImageStyle1.Source = BackgroundImageStyle2.Source = BackgroundImageStyle3.Source = croppedImage;
                        
                        _lastTrackId = trackId;
                        _lastAlbumArt = image;
                    }

                    bool playingChanged = _lastIsPlaying != MusicPlayerService.Instance.IsPlaying;
                    if (playingChanged || trackChanged)
                    {
                        ControlPlayPause.IsEnabled = true;
                        ControlPlayPause.Opacity = 1;
                        SymbolPlayPause.Symbol = MusicPlayerService.Instance.IsPlaying ? 
                            Wpf.Ui.Controls.SymbolRegular.Pause16 : Wpf.Ui.Controls.SymbolRegular.Play16;
                        _lastIsPlaying = MusicPlayerService.Instance.IsPlaying;
                    }
                    
                    ControlBack.IsEnabled = ControlForward.IsEnabled = true;
                    ControlBack.Opacity = ControlForward.Opacity = 1;

                    if (_seekBarEnabled)
                    {
                        if (!_mediaSessionSupportsSeekbar)
                        {
                            _mediaSessionSupportsSeekbar = true;
                            UpdateUILayout();
                        }
                    }

                    BackgroundImageStyle1.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 1 ? Visibility.Visible : Visibility.Collapsed;
                    BackgroundImageStyle2.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 2 ? Visibility.Visible : Visibility.Collapsed;
                    BackgroundImageStyle3.Visibility = SettingsManager.Current.MediaFlyoutBackgroundBlur == 3 ? Visibility.Visible : Visibility.Collapsed;
                    
                    SongImagePlaceholder.Visibility = SongImage.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;
                    MediaIdStackPanel.Visibility = Visibility.Collapsed;

                    UpdateShuffleRepeatVisuals(
                        MusicPlayerService.Instance.IsShuffleEnabled,
                        MusicPlayerService.Instance.RepeatMode == FluentFlyoutWPF.Classes.RepeatMode.All,
                        MusicPlayerService.Instance.RepeatMode == FluentFlyoutWPF.Classes.RepeatMode.One,
                        true // forceVisible for internal player
                    );
                    
                    return;
                }
            }

            if (focusedSession == null)
            {
                SymbolPlayPause.Symbol = Wpf.Ui.Controls.SymbolRegular.Stop16;
                ControlPlayPause.IsEnabled = false;
                ControlPlayPause.Opacity = 0.35;
                ControlBack.IsEnabled = ControlForward.IsEnabled = false;
                ControlBack.Opacity = ControlForward.Opacity = 0.35;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                _lastTrackId = string.Empty;
                _lastAlbumArt = null;
                _lastIsPlaying = null;
                return;
            }

            var controlSession = focusedSession.ControlSession;
            if (controlSession == null) return;

            var playbackInfo = controlSession.GetPlaybackInfo();
            var songInfo = TryGetMediaProperties(controlSession);
            
            string extTrackId = (songInfo?.Title ?? "") + (songInfo?.Artist ?? "");
            bool extTrackChanged = _lastTrackId != extTrackId;

            if (extTrackChanged && songInfo != null)
            {
                var image = BitmapHelper.GetThumbnail(songInfo.Thumbnail);

                var croppedImage = BitmapHelper.CropToSquare(image);
                BackgroundImageStyle1.Source = BackgroundImageStyle2.Source = BackgroundImageStyle3.Source = croppedImage;
                
                _lastTrackId = extTrackId;
                _lastAlbumArt = image;
            }

            if (playbackInfo != null)
            {
                bool isPlaying = playbackInfo.Controls.IsPauseEnabled;
                if (_lastIsPlaying != isPlaying || extTrackChanged)
                {
                    SymbolPlayPause.Symbol = isPlaying ? 
                        Wpf.Ui.Controls.SymbolRegular.Pause16 : Wpf.Ui.Controls.SymbolRegular.Play16;
                    ControlPlayPause.IsEnabled = true;
                    ControlPlayPause.Opacity = 1;
                    _lastIsPlaying = isPlaying;
                }
                
                ControlBack.IsEnabled = ControlForward.IsEnabled = playbackInfo.Controls.IsNextEnabled;
                ControlBack.Opacity = ControlForward.Opacity = playbackInfo.Controls.IsNextEnabled ? 1 : 0.35;
            }

            SongImagePlaceholder.Visibility = SongImage.ImageSource == null ? Visibility.Visible : Visibility.Collapsed;

            if (_seekBarEnabled && playbackInfo != null)
            {
                var timeline = controlSession.GetTimelineProperties();
                bool sessionSupportsSeekbar = timeline.MaxSeekTime.TotalSeconds >= 1.0;

                if (_mediaSessionSupportsSeekbar != sessionSupportsSeekbar)
                {
                    _mediaSessionSupportsSeekbar = sessionSupportsSeekbar;
                    UpdateUILayout();
                    _isHiding = true;
                    ShowMediaFlyout(forceShow: true);
                }

                if (sessionSupportsSeekbar)
                {
                    Seekbar.Maximum = timeline.MaxSeekTime.TotalSeconds;
                    SeekbarMaxDuration.Text = timeline.MaxSeekTime.ToString(timeline.MaxSeekTime.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
                }
            }

            bool isShuffle = playbackInfo?.IsShuffleActive ?? false;
            bool isRepeatAll = playbackInfo?.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List;
            bool isRepeatOne = playbackInfo?.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track;
            
            UpdateShuffleRepeatVisuals(isShuffle, isRepeatAll, isRepeatOne);

            if (SettingsManager.Current.PlayerInfoEnabled && !SettingsManager.Current.CompactLayout)
            {
                MediaIdStackPanel.Visibility = Visibility.Visible;
                (string title, ImageSource? Icon) = MediaPlayerData.getMediaPlayerData(focusedSession.Id);
                MediaId.Text = title;
                if (Icon != null)
                {
                    MediaIdIcon.Source = Icon;
                    MediaIdIcon.Visibility = Visibility.Visible;
                }
                else MediaIdIcon.Visibility = Visibility.Collapsed;
            }
            else MediaIdStackPanel.Visibility = Visibility.Collapsed;
        });
    }

    private void UpdateShuffleRepeatVisuals(bool isShuffle, bool isRepeatAll, bool isRepeatOne, bool forceVisible = false)
    {
        var dominantBrush = BitmapHelper.SavedDominantColors.FirstOrDefault();
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && dominantBrush != null;
        
        var accentBrush = useAccent ? dominantBrush! : (Brush)Application.Current.TryFindResource("AccentFillColorDefaultBrush");
        var defaultBrush = Brushes.White;

        // Shuffle
        if ((forceVisible || SettingsManager.Current.ShuffleEnabled) && !SettingsManager.Current.CompactLayout)
        {
            ControlShuffle.Visibility = Visibility.Visible;
            SymbolShuffle.Symbol = isShuffle ? Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24 : Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
            SymbolShuffle.Foreground = isShuffle ? accentBrush : defaultBrush;
            SymbolShuffle.Opacity = isShuffle ? 1.0 : 0.4;
        }
        else ControlShuffle.Visibility = Visibility.Collapsed;

        // Repeat
        if ((forceVisible || SettingsManager.Current.RepeatEnabled) && !SettingsManager.Current.CompactLayout)
        {
            ControlRepeat.Visibility = Visibility.Visible;
            if (isRepeatOne)
            {
                SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124;
                SymbolRepeat.Foreground = accentBrush;
                SymbolRepeat.Opacity = 1.0;
            }
            else if (isRepeatAll)
            {
                SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24;
                SymbolRepeat.Foreground = accentBrush;
                SymbolRepeat.Opacity = 1.0;
            }
            else
            {
                SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24;
                SymbolRepeat.Foreground = defaultBrush;
                SymbolRepeat.Opacity = 0.4;
            }
        }
        else ControlRepeat.Visibility = Visibility.Collapsed;
    }

    private void UpdateUILayout() // update the layout based on the settings
    {
        Dispatcher.Invoke(() =>
        {
            int extraWidth = SettingsManager.Current.RepeatEnabled ? 36 : 0;
            extraWidth += SettingsManager.Current.ShuffleEnabled ? 36 : 0;
            extraWidth += SettingsManager.Current.PlayerInfoEnabled ? 72 : 72; // disabled player info should temporarily keep the widget the same width as no one seems to like the small version

            int extraHeight = (SettingsManager.Current.SeekbarEnabled && _mediaSessionSupportsSeekbar) || IsInternalPlayerActive ? 36 : 0;

            if (SettingsManager.Current.CompactLayout) // compact layout
            {
                Height = 64 + extraHeight;
                Width = 440;
                MainStackPanel.Margin = new Thickness(12, 4, 12, 4);
                
                MediaIdStackPanel.Visibility = Visibility.Collapsed;
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

                BodyGrid.Width = double.NaN;
                BodyGrid.Margin = new Thickness(12, 0, 0, 0);
            }
            else // normal layout
            {
                Height = 112 + extraHeight;
                Width = 310 - 72 + extraWidth;
                MainStackPanel.Margin = new Thickness(12);

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

                BodyGrid.Width = 182 - 72 + extraWidth;
                BodyGrid.Margin = new Thickness(12, 0, 0, 0);

                // Only show MediaIdStackPanel when PlayerInfo is enabled
                MediaIdStackPanel.Visibility = SettingsManager.Current.PlayerInfoEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (SettingsManager.Current.CenterTitleArtist)
            {
                SongTitle.HorizontalAlignment = HorizontalAlignment.Center;
                SongArtist.HorizontalAlignment = HorizontalAlignment.Center;
            }
            else
            {
                SongTitle.HorizontalAlignment = HorizontalAlignment.Left;
                SongArtist.HorizontalAlignment = HorizontalAlignment.Left;
            }

            if (SettingsManager.Current.SeekbarEnabled || IsInternalPlayerActive)
                SeekbarWrapper.Visibility = Visibility.Visible;
            else
                SeekbarWrapper.Visibility = Visibility.Collapsed;

            this.EnableBackdrop();
        });

        _layout = SettingsManager.Current.CompactLayout;
        _repeatEnabled = SettingsManager.Current.RepeatEnabled;
        _shuffleEnabled = SettingsManager.Current.ShuffleEnabled;
        _playerInfoEnabled = SettingsManager.Current.PlayerInfoEnabled;
        _centerTitleArtist = SettingsManager.Current.CenterTitleArtist;
        _seekBarEnabled = SettingsManager.Current.SeekbarEnabled;
        _alwaysDisplay = SettingsManager.Current.MediaFlyoutAlwaysDisplay;
    }

    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        var session = _externalMediaService.GetPreferredSession();
        if (session != null && !_externalMediaService.IsInternalSession(session))
        {
            await session.ControlSession.TrySkipPreviousAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.PlayPrevious();
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        var session = _externalMediaService.GetPreferredSession();
        if (session != null && !_externalMediaService.IsInternalSession(session))
        {
            keybd_event(0xB3, 0, 0, IntPtr.Zero); // Media Play/Pause key
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.TogglePlayPause();
        }

        // The UI will be updated via events (GSMTC or Internal)
    }

    private async void Forward_Click(object sender, RoutedEventArgs e)
    {
        var session = _externalMediaService.GetPreferredSession();
        if (session != null && !_externalMediaService.IsInternalSession(session))
        {
            await session.ControlSession.TrySkipNextAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.PlayNext();
        }
    }

    private async void Repeat_Click(object sender, RoutedEventArgs e)
    {
        var session = _externalMediaService.GetPreferredSession();
        if (session != null)
        {
            var playbackInfo = session.ControlSession.GetPlaybackInfo();
            if (playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.None)
            {
                SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24);
                await session.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.List);
            }
            else if (playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List)
            {
                SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124);
                await session.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.Track);
            }
            else if (playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track)
            {
                SymbolRepeat.Dispatcher.Invoke(() => SymbolRepeat.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24);
                await session.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.None);
            }
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            // Toggle internal repeat mode
            var current = MusicPlayerService.Instance.RepeatMode;
            MusicPlayerService.Instance.RepeatMode = current switch
            {
                FluentFlyoutWPF.Classes.RepeatMode.None => FluentFlyoutWPF.Classes.RepeatMode.All,
                FluentFlyoutWPF.Classes.RepeatMode.All => FluentFlyoutWPF.Classes.RepeatMode.One,
                FluentFlyoutWPF.Classes.RepeatMode.One => FluentFlyoutWPF.Classes.RepeatMode.None,
                _ => FluentFlyoutWPF.Classes.RepeatMode.None
            };
        }
    }

    private async void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        var session = _externalMediaService.GetPreferredSession();
        if (session != null)
        {
            if (session.ControlSession.GetPlaybackInfo().IsShuffleActive == true)
            {
                SymbolShuffle.Dispatcher.Invoke(() => SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24);
                await session.ControlSession.TryChangeShuffleActiveAsync(false);
            }
            else
            {
                SymbolShuffle.Dispatcher.Invoke(() => SymbolShuffle.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24);
                await session.ControlSession.TryChangeShuffleActiveAsync(true);
            }
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.IsShuffleEnabled = !MusicPlayerService.Instance.IsShuffleEnabled;
        }
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
        Dispatcher.Invoke(() =>
        {
            Seekbar.Value = targetSeconds;
        });
    }

    private async void Seekbar_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var seekPosition = TimeSpan.FromSeconds(Seekbar.Value);

        if (_externalMediaService.GetPreferredSession() is { } session)
        {
            // Use 1 tick (100ns) instead of 0 to signal "go to start" — the SMTC API
            // ignores a position of exactly 0 ticks (treats it as "no change").
            long ticks = seekPosition.Ticks > 0 ? seekPosition.Ticks : 1;
            await session.ControlSession.TryChangePlaybackPositionAsync(ticks);
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.Seek(seekPosition);
        }
        _isDragging = false;
    }

    private void Seekbar_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isDragging) return;
        var timespan = TimeSpan.FromSeconds(e.NewValue);
        Dispatcher.Invoke(() =>
        {
            SeekbarCurrentDuration.Text = timespan.ToString(timespan.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        });
    }

    private void SeekbarUpdateUi(object? sender)
    {
        // Skip if the media player recently pushed its own timeline update
        // to avoid fighting between self-updates and timer-interpolated updates
        if (DateTime.Now.Subtract(_lastSelfUpdateTimestamp).TotalSeconds < 1.5) return;

        if (!_seekBarEnabled || Visibility != Visibility.Visible || _isDragging) return;
        if (_externalMediaService.GetPreferredSession() is not { } session)
        {
            if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.IsPlaying && _seekBarEnabled && Visibility == Visibility.Visible && !_isDragging)
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
            Seekbar.Value = pos.TotalSeconds;
            SeekbarCurrentDuration.Text = pos.ToString(pos.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
            
            if (taskbarWindow?.Widget != null)
            {
                taskbarWindow.Widget.UpdateProgress(pos.TotalSeconds, Seekbar.Maximum);
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
                    RecreateTrayIconSafely();
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

    private void RecreateTrayIconSafely()
    {
        try
        {
            nIcon.Visibility = Visibility.Collapsed;

            if (!SettingsManager.Current.NIconHide)
            {
                if (SettingsManager.Current.NIconSymbol == true)
                {
                    var iconUri = new Uri(WindowsThemeHelper.GetCurrentWindowsTheme() == WindowsTheme.Dark
                        ? "pack://application:,,,/Resources/TrayIcons/FluentFlyoutWhite.png"
                        : "pack://application:,,,/Resources/TrayIcons/FluentFlyoutBlack.png");
                    nIcon.Icon = new BitmapImage(iconUri);
                }
                else
                {
                    var iconUi = new Uri("pack://application:,,,/Resources/FluentFlyout2.ico");
                    nIcon.Icon = new BitmapImage(iconUi);
                }

                nIcon.Visibility = Visibility.Visible;
                nIcon.Register();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate tray icon safely");
        }
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

            _systemHookService.Dispose();
            _externalMediaService.Stop();

            WeakReferenceMessenger.Default.UnregisterAll(this);

            // clean up other resources
            if (lockWindow?.IsLoaded == true)
                lockWindow.Close();

            if (nextUpWindow?.IsLoaded == true)
                nextUpWindow.Close();

            if (taskbarWindow?.IsLoaded == true)
                taskbarWindow.Close();

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
        ShowMediaFlyout();
    }

    private void NotifyIconQuit_Click(object sender, RoutedEventArgs e)
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
            SettingsManager.Current.IsPremiumUnlocked = LicenseManager.Instance.IsPremiumUnlocked;
            SettingsManager.Current.IsStoreVersion = LicenseManager.Instance.IsStoreVersion;
            SettingsManager.SaveSettings();

            Logger.Info($"License synced on startup - Store: {SettingsManager.Current.IsStoreVersion}, Premium: {SettingsManager.Current.IsPremiumUnlocked}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize license");
        }

        ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
        taskbarWindow = new TaskbarWindow();
        UpdateTaskbar();
    }

    public void RecreateTaskbarWindow()
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

            taskbarWindow = new();
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
        else if (SettingsManager.Current.NIconLeftClick == 1) ShowMediaFlyout();
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
        ShowMediaFlyout(toggleMode: true);
    }

    private void ApplyAccentColor(SolidColorBrush? brush, bool notifyOthers = false)
    {
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && brush != null;

        Dispatcher.Invoke(() =>
        {
            if (useAccent && brush != null)
            {
                // Apply to Play/Pause button background
                ControlPlayPause.Background = brush;
                
                // Apply to control icons for consistent theme
                SymbolBack.Foreground = brush;
                SymbolForward.Foreground = brush;

                // Subtle background for other controls
                var subtleBrush = brush.Clone();
                subtleBrush.Opacity = 0.15;
                subtleBrush.Freeze();
                ControlBack.Background = ControlForward.Background = ControlRepeat.Background = ControlShuffle.Background = subtleBrush;
                
                // Apply to Seekbar
                Seekbar.Foreground = brush;

                // Apply to Glow effect
                if (SongImageGlow != null)
                {
                    SongImageGlow.Color = brush.Color;
                    SongImageGlow.Opacity = 0.6;
                    SongImageGlow.BlurRadius = 25;
                    SongImageGlow.ShadowDepth = 0;
                }
                
                // Apply to placeholder
                SongImagePlaceholder.Foreground = brush;

                // For Repeat and Shuffle, we re-run the visuals
                UpdateShuffleRepeatVisuals(
                    IsInternalPlayerActive ? MusicPlayerService.Instance.IsShuffleEnabled : false,
                    IsInternalPlayerActive ? MusicPlayerService.Instance.RepeatMode == Classes.RepeatMode.All : false,
                    IsInternalPlayerActive ? MusicPlayerService.Instance.RepeatMode == Classes.RepeatMode.One : false,
                    IsInternalPlayerActive
                );
            }
            else
            {
                // Reset to defaults
                ControlPlayPause.ClearValue(BackgroundProperty);
                ControlBack.ClearValue(BackgroundProperty);
                ControlForward.ClearValue(BackgroundProperty);
                ControlRepeat.ClearValue(BackgroundProperty);
                ControlShuffle.ClearValue(BackgroundProperty);
                
                var defaultForeground = Brushes.White;
                SymbolBack.Foreground = defaultForeground;
                SymbolForward.Foreground = defaultForeground;
                
                Seekbar.ClearValue(ForegroundProperty);
                
                if (SongImageGlow != null)
                {
                    SongImageGlow.Opacity = 0;
                    SongImageGlow.ShadowDepth = 0;
                }

                var accentBrush = (SolidColorBrush)Application.Current.TryFindResource("AccentFillColorDefaultBrush");
                SongImagePlaceholder.Foreground = accentBrush;
            }

            if (notifyOthers)
            {
                WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
            }
        });
    }
}
