using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.ViewModels;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Windows;

/// <summary>
/// Interaction logic for TaskbarWindow.xaml
/// </summary>
public partial class TaskbarWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly DispatcherTimer _timer;
    private const int TaskbarWidgetVisualizerGap = 4;
    private readonly int _nativeWidgetsPadding = 216;
    private readonly double _scale = 0.9;

    private IntPtr _trayHandle;
    private AutomationElement? _widgetElement;
    private AutomationElement? _trayElement;
    private AutomationElement? _taskbarFrameElement;
    private int _lastSelectedMonitor = -1;
    private bool _positionUpdateInProgress;
    private readonly Dictionary<string, Task> _pendingAutomationTasks = [];
    
    private GlobalSystemMediaTransportControlsSessionPlaybackStatus? _lastPlaybackStatus;
    private DispatcherTimer? _autoHideTimer;

    public NowPlayingViewModel NowPlaying { get; }

    public TaskbarWindow(SettingsShellViewModel settingsViewModel, NowPlayingViewModel nowPlayingViewModel)
    {
        NowPlaying = nowPlayingViewModel;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        // Set DataContext for bindings
        DataContext = settingsViewModel;

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += async (s, e) => await UpdatePositionAsync();
        _timer.Start();

        // Listen to theme changes
        WeakReferenceMessenger.Default.Register<ApplyTaskbarWidgetThemeMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(ApplyTaskbarTheme);
        });
    }

    private void ApplyTaskbarTheme()
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        ApplyThemeToDictionary(this.Resources, theme);
        ApplyThemeToTree(this, theme);
    }

    private void ApplyThemeToDictionary(ResourceDictionary dictionary, Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (mergedDictionary.GetType().Name == "ThemesDictionary")
            {
                var themeProperty = mergedDictionary.GetType().GetProperty("Theme");
                themeProperty?.SetValue(mergedDictionary, theme);
            }

            if (mergedDictionary.MergedDictionaries.Count > 0)
            {
                ApplyThemeToDictionary(mergedDictionary, theme);
            }
        }
    }

    private void ApplyThemeToTree(DependencyObject root, Wpf.Ui.Appearance.ApplicationTheme theme)
    {
        if (root == null) return;

        if (root is FrameworkElement frameworkElement)
        {
            if (frameworkElement.Resources != null && frameworkElement.Resources.MergedDictionaries.Count > 0)
            {
                ApplyThemeToDictionary(frameworkElement.Resources, theme);
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                ApplyThemeToTree(dependencyObject, theme);
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromDependencyObject(this);
        source.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Some interface mods may collect information from all windows associated with the taskbar,
        // causing the widget and the entire taskbar to freeze.
        // For example, Nilesoft Shell and "Click on empty taskbar space" from Windhawk.
        // Therefore, we are preventing the propagation of this message.
        // Also prevents the widget from blocking taskbar's message processing, which is another source of freezes.
        switch (msg)
        {
            case 0x003D: // WM_GETOBJECT (Sent by Microsoft UI Automation to obtain information about an accessible object contained in a server application)
            case 0x0018: // WM_SHOWWINDOW
            case 0x0046: // WM_WINDOWPOSCHANGING - Triggers during alt-tabs, window changes
            case 0x0083: // WM_NCCALCSIZE - Can trigger layout storms
            case 0x0281: // WM_IME_SETCONTEXT - IME conflicts
            case 0x0282: // WM_IME_NOTIFY
                handled = true;
                return IntPtr.Zero;

                // Handle other known harmless messages that are sent when FluentFlyout starts, Windows locks, etc.
                // Needs testing
                //case 0x0047:
                //case 0x02B1:
                //case 0x001E:
                //case 0x0164:
                //case 0xC25F:
                //    handled = true;
                //    return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        ApplyTaskbarTheme();
    }

    private IntPtr GetSelectedTaskbarHandle(out bool isMainTaskbarSelected)
    {
        var monitors = MonitorUtil.GetMonitors();
        var selectedMonitor = monitors[Math.Clamp(SettingsManager.Current.TaskbarWidgetSelectedMonitor, 0, monitors.Count - 1)];
        isMainTaskbarSelected = true;

        // Get the main taskbar and check if it is on the selected monitor.
        var mainHwnd = FindWindow("Shell_TrayWnd", null);
        if (MonitorUtil.GetMonitor(mainHwnd).deviceId == selectedMonitor.deviceId)
            return mainHwnd;

        if (monitors.Count == 1)
            return mainHwnd;

        isMainTaskbarSelected = false;
        if (monitors.Count == 2)
        {
            var hwnd = FindWindow("Shell_SecondaryTrayWnd", null);
            if (MonitorUtil.GetMonitor(hwnd).deviceId == selectedMonitor.deviceId)
            {
                return hwnd;
            }
            else
            {
                isMainTaskbarSelected = true;
                return mainHwnd;
            }
        }

        // If there are more than two monitors, we will need to enumerate all existing windows
        // to find all Shell_SecondaryTrayWnd among them.

        IntPtr secondHwnd = IntPtr.Zero;
        StringBuilder className = new(256); // 256 is the maximum class name length
        IntPtr checkWindowClass(IntPtr wnd)
        {
            var len = GetClassName(wnd, className, className.Capacity);
            if (className.ToString() == "Shell_SecondaryTrayWnd")
            {
                if (MonitorUtil.GetMonitor(wnd).deviceId == selectedMonitor.deviceId)
                {
                    return wnd;
                }
            }
            return IntPtr.Zero;
        }

        // Get the threadId of the main taskbar and check all windows created in the same thread.
        // This is very fast, but in some cases Shell_TrayWnd and other Shell_SecondaryTrayWnd's may be created in different threads.
        // Actually, I couldn't achieve that kind of behavior.
        if (mainHwnd != IntPtr.Zero)
        {
            uint threadId = GetWindowThreadProcessId(mainHwnd, IntPtr.Zero);
            EnumThreadWindows(threadId, (wnd, param) =>
            {
                secondHwnd = checkWindowClass(wnd);
                if (secondHwnd != IntPtr.Zero)
                    return false; // stop

                return true;
            }, IntPtr.Zero);

            if (secondHwnd != IntPtr.Zero)
                return secondHwnd;
        }

        // If for some reason the taskbars were created in different threads or simply could not be found,
        // we try to find them among all existing windows.
        EnumWindows((wnd, param) =>
        {
            secondHwnd = checkWindowClass(wnd);
            if (secondHwnd != IntPtr.Zero)
                return false; // stop

            return true;
        }, IntPtr.Zero);

        if (secondHwnd != IntPtr.Zero)
            return secondHwnd;

        // Logger.Debug($"No taskbar found on the selected monitor. Using the main taskbar.");
        isMainTaskbarSelected = true;
        return mainHwnd;
    }

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarWindowHandle = interop.Handle;

            //Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);

            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(taskbarWindowHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(taskbarWindowHandle, GWL_STYLE, style);

            if (taskbarHandle != IntPtr.Zero)
            {
                SetParent(taskbarWindowHandle, taskbarHandle);
            }
            else
            {
                Logger.Warn("Taskbar handle is NULL during setup — the Taskbar may not be loaded yet.");
            }

            _ = CalculateAndSetPositionAsync(taskbarHandle, taskbarWindowHandle, isMainTaskbarSelected);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during setup");
        }
    }

    private void UpdateWindowRegion(IntPtr windowHandle, params Rect[] rects)
    {
        IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
        foreach (var r in rects)
        {
            // make sure rect is not empty - happens when setting elements to collapsed
            if (r == Rect.Empty)
                continue;

            IntPtr newRgn = CreateRectRgn((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
            if (newRgn == IntPtr.Zero)
            {
                Logger.Error($"Taskbar Widget error during CreateRectRgn({(int)r.Left}, {(int)r.Top}, {(int)r.Right}, {(int)r.Bottom}).");
                goto on_error;
            }

            if (CombineRgn(rgn, rgn, newRgn, 2 /*RGN_OR*/) == 0)
            {
                Logger.Error($"Taskbar Widget error during CombineRgn. Combined regions: {string.Join(", ", rects.Select(i => $"RECT({(int)i.Left}, {(int)i.Top}, {(int)i.Right}, {(int)i.Bottom})"))}");
                DeleteObject(newRgn);
                goto on_error;
            }

            DeleteObject(newRgn);
        }

        if (SetWindowRgn(windowHandle, rgn, true) == 0)
        {
            Logger.Error($"Taskbar Widget error during SetWindowRgn.");
            goto on_error;
        }

        // Simple debugging to display the window region:
#if false
        var whiteRect = WidgetCanvas.Children.Cast<FrameworkElement>().FirstOrDefault(e => e.Name == "test_border");
        if (whiteRect == null)
        {
            whiteRect = new System.Windows.Shapes.Rectangle() { Name = "test_border", Width = 20000, Height = 20000, Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) };
            WidgetCanvas.Children.Add(whiteRect);
            Canvas.SetLeft(whiteRect, -10000);
            Canvas.SetTop(whiteRect, -10000);
        }
#endif

        return;

    on_error:

        // All regions that were not sent without errors to SetWindowRgn must be destroyed manually
        DeleteObject(rgn);
        if (SetWindowRgn(windowHandle, IntPtr.Zero, true) == 0)
            Logger.Error("Taskbar Widget error during window region reset.");
    }

    private void UpdatePosition()
    {
        _ = UpdatePositionAsync();
    }

    private async Task UpdatePositionAsync()
    {
        if (MainWindow.ExplorerRestarting)
        {
            // Explorer is restarting -- do NOTHING
            return;
        }

        // Check premium status before allowing widget to be displayed
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);

            if (interop.Handle == IntPtr.Zero)
            {
                if (MainWindow.ExplorerRestarting)
                {
                    Logger.Info("Skipping TaskbarWindow recovery during Explorer restart");
                    return;
                }

                _timer.Stop();

                try
                {
                    WeakReferenceMessenger.Default.Send(new RecreateTaskbarWindowMessage());
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to signal MainWindow to recover Taskbar Widget window");
                }

                return;
            }

            // If the Taskbar was not found during initialization or another taskbar was selected,
            // then we need to set the Taskbar as the Parent here.
            if (GetParent(interop.Handle) != taskbarHandle && taskbarHandle != IntPtr.Zero)
            {
                SetParent(interop.Handle, taskbarHandle);
            }

            if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
            {
                await CalculateAndSetPositionAsync(taskbarHandle, interop.Handle, isMainTaskbarSelected);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during position update");
        }
    }

    private async Task CalculateAndSetPositionAsync(IntPtr taskbarHandle, IntPtr taskbarWindowHandle, bool isMainTaskbarSelected)
    {
        // Prevent overlapping updates - if a previous update is still running
        // (e.g. waiting for an automation query timeout), skip this tick.
        if (_positionUpdateInProgress)
            return;
        _positionUpdateInProgress = true;

        try
        {
            // get DPI scaling
            double dpiScale = GetDpiForWindow(taskbarHandle) / 96.0;

            // Guard against invalid DPI (e.g. during explorer restart when handle is stale)
            if (dpiScale <= 0)
                return;

            if (_lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
            {
                ResetTaskbarElementCaches();
            }

            // Get Taskbar dimensions
            RECT taskbarRect;

            if (!SettingsManager.Current.LegacyTaskbarWidthEnabled)
            {
                // first, try to find the Taskbar.TaskbarFrame element in the XAML
                // this should give us the actual bounds of the taskbar, excluding invisible margins on some Windows configurations
                (bool success, Rect result) = await GetTaskbarFrameRectAsync(taskbarHandle);
                if (success)
                {
                    taskbarRect = new RECT
                    {
                        Left = (int)result.Left,
                        Top = (int)result.Top,
                        Right = (int)result.Right,
                        Bottom = (int)result.Bottom
                    };
                }
                else
                {
                    // fallback to GetWindowRect if we fail to get the frame bounds for some reason
                    GetWindowRect(taskbarHandle, out taskbarRect);
                }
            }
            else
            {
                // legacy method - GetWindowRect on the entire taskbar, which includes invisible margins on some Windows configurations
                GetWindowRect(taskbarHandle, out taskbarRect);
            }

            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

            int containerWidth = taskbarWidth;
            int containerHeight = taskbarHeight;

            // Following SetWindowPos will set the position relative to the parent window,
            // so those coordinates need to be converted.
            POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
            ScreenToClient(taskbarHandle, ref containerPos);

            // Apply using SetWindowPos (Bypassing WPF layout engine)
            SetWindowPos(taskbarWindowHandle, 0,
                     containerPos.X, containerPos.Y,
                     containerWidth, containerHeight,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);

            var wRect = await PositionWidgetAsync(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected);
            var vRect = PositionVisualizer(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected);

            UpdateWindowRegion(taskbarWindowHandle, wRect, vRect);

            _lastSelectedMonitor = SettingsManager.Current.TaskbarWidgetSelectedMonitor;
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private async Task<Rect> PositionWidgetAsync(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected)
    {
        if (!SettingsManager.Current.TaskbarWidgetEnabled)
            return Rect.Empty;

        // Calculate widget size
        var (logicalWidth, logicalHeight) = Widget.CalculateSize(dpiScale);

        int physicalWidth = (int)(logicalWidth * dpiScale * _scale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // Calculate vertical position (centered in taskbar)
        int widgetTop = (taskbarHeight - physicalHeight) / 2;

        // Calculate horizontal position based on alignment setting
        int widgetLeft = 0;
        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // left aligned with some padding (like native widgets)
                widgetLeft = 20;

                if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 0)
                    widgetLeft += (int)((TaskbarVisualizer.Width + TaskbarWidgetVisualizerGap) * dpiScale);

                if (!SettingsManager.Current.TaskbarWidgetPadding)
                    break;

                // automatic widget padding to the left
                try
                {
                    // find widget button in XAML
                    (bool found, Rect widgetRect) = await GetTaskbarWidgetRectAsync(taskbarHandle);

                    // make sure it's on the left side, otherwise ignore (widget might be to the right)
                    if (found && widgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2)
                    {
                        // Convert absolute screen position to relative position within taskbar
                        widgetLeft = (int)(widgetRect.Right - taskbarRect.Left) + 2;
                    }
                }
                catch (Exception ex)
                {
                    // fallback to default padding
                    Logger.Warn(ex, "Failed to get Widgets button position.");
                    widgetLeft += _nativeWidgetsPadding + 2;
                }
                break;

            case 1: // center of the taskbar
                widgetLeft = (taskbarRect.Right - taskbarRect.Left - physicalWidth) / 2;

                if (SettingsManager.Current.TaskbarVisualizerEnabled)
                {
                    int visualizerWidthPhysical = (int)(TaskbarVisualizer.Width * dpiScale);
                    int gapPhysical = (int)(TaskbarWidgetVisualizerGap * dpiScale);
                    if (SettingsManager.Current.TaskbarVisualizerPosition == 0)
                        widgetLeft += (visualizerWidthPhysical + gapPhysical) / 2;
                    else
                        widgetLeft -= (visualizerWidthPhysical + gapPhysical) / 2;
                }

                if (SettingsManager.Current.TaskbarWidgetPadding)
                {
                    widgetLeft = await ApplyCenterAutomaticPaddingAsync(
                        taskbarHandle,
                        taskbarRect,
                        widgetLeft,
                        widgetTop,
                        physicalWidth,
                        physicalHeight,
                        dpiScale,
                        isMainTaskbarSelected);
                }

                break;

            case 2: // right aligned next to system tray with tiny bit of padding
                try
                {
                    int visualizerWidthPhys = (int)(TaskbarVisualizer.Width * dpiScale);
                    int gapPhys = (int)(TaskbarWidgetVisualizerGap * dpiScale);
                    if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 1)
                        widgetLeft -= (visualizerWidthPhys + gapPhys);

                    // try to position next to widgets button if enabled
                    if (SettingsManager.Current.TaskbarWidgetPadding)
                    {
                        try
                        {
                            // find widget button in XAML
                            (bool found, Rect widgetRect) = await GetTaskbarWidgetRectAsync(taskbarHandle);

                            // make sure it's on the right side, otherwise ignore (widget might be to the left)
                            if (found && widgetRect.Left > (taskbarRect.Left + taskbarRect.Right) / 2)
                            {
                                // Convert absolute screen position to relative position within taskbar
                                widgetLeft += (int)(widgetRect.Left - taskbarRect.Left) - 1 - physicalWidth;
                                break; // early exit so we don't move it back next to tray below
                            }
                        }
                        catch (Exception ex) // catch exception when getting widget position
                        {
                            Logger.Warn(ex, "Failed to get Widgets button position.");
                        }
                    }

                    // try to position next to system tray
                    if (!isMainTaskbarSelected)
                    {
                        // find secondary tray with automation
                        (bool found, Rect secondaryTrayRect) = await GetSystemTrayRectAsync(taskbarHandle);

                        if (found)
                        {
                            // Convert absolute screen position to relative position within taskbar
                            widgetLeft += (int)(secondaryTrayRect.Left - taskbarRect.Left) - physicalWidth - 1;
                            break;
                        }
                    }
                    else if (_trayHandle == IntPtr.Zero || _lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
                    {
                        if (isMainTaskbarSelected)
                        {
                            // find primary tray handle
                            _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
                        }
                    }

                    // the code reaches here because:
                    // primary taskbar monitor is selected and auto widget padding setting is off

                    // if the tray handle is zero, fallback to right alignment,
                    // since we are aligning to the right side and know the size of the taskbar.
                    if (_trayHandle == IntPtr.Zero)
                    {
                        widgetLeft += taskbarRect.Right - taskbarRect.Left - physicalWidth - 20;
                        break;
                    }
                    GetWindowRect(_trayHandle, out RECT trayRect);
                    // Convert absolute screen position to relative position within taskbar
                    widgetLeft += trayRect.Left - taskbarRect.Left - physicalWidth - 1;
                }
                catch (Exception ex)
                {
                    // Fallback to left alignment
                    Logger.Warn(ex, "Failed to get System Tray position.");
                    widgetLeft = 20;
                }
                break;
        }

        widgetLeft += (int)Math.Round(SettingsManager.Current.TaskbarWidgetManualPadding * dpiScale);

        // Set widget position within canvas
        Canvas.SetLeft(Widget, widgetLeft / dpiScale);
        Canvas.SetTop(Widget, widgetTop / dpiScale);
        Widget.Width = logicalWidth;
        Widget.Height = logicalHeight;

        return new Rect(Canvas.GetLeft(Widget) * dpiScale, Canvas.GetTop(Widget) * dpiScale, Widget.Width * dpiScale * _scale, Widget.Height * dpiScale);
    }

    private async Task<int> ApplyCenterAutomaticPaddingAsync(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        int widgetLeft,
        int widgetTop,
        int widgetWidth,
        int widgetHeight,
        double dpiScale,
        bool isMainTaskbarSelected)
    {
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        Rect groupBounds = GetWidgetGroupBounds(widgetLeft, widgetTop, widgetWidth, widgetHeight, dpiScale);

        var (hasLeftReserved, leftReserved) = await TryGetReservedTaskbarBoundsAsync(
            taskbarHandle,
            taskbarRect,
            isMainTaskbarSelected,
            preferLeftSide: true);
        var (hasRightReserved, rightReserved) = await TryGetReservedTaskbarBoundsAsync(
            taskbarHandle,
            taskbarRect,
            isMainTaskbarSelected,
            preferLeftSide: false);

        bool leftCollision = hasLeftReserved && RectsOverlapHorizontally(groupBounds, leftReserved);
        bool rightCollision = hasRightReserved && RectsOverlapHorizontally(groupBounds, rightReserved);

        if (leftCollision && rightCollision)
        {
            Logger.Warn("Taskbar widget center padding collided with reserved taskbar areas on both sides.");
        }

        if (leftCollision)
        {
            widgetLeft += (int)Math.Ceiling(leftReserved.Right + 2 - groupBounds.Left);
            groupBounds = GetWidgetGroupBounds(widgetLeft, widgetTop, widgetWidth, widgetHeight, dpiScale);
        }

        if (rightCollision || (hasRightReserved && RectsOverlapHorizontally(groupBounds, rightReserved)))
        {
            widgetLeft -= (int)Math.Ceiling(groupBounds.Right - (rightReserved.Left - 1));
            groupBounds = GetWidgetGroupBounds(widgetLeft, widgetTop, widgetWidth, widgetHeight, dpiScale);
        }

        if (groupBounds.Left < 0)
        {
            widgetLeft += (int)Math.Ceiling(-groupBounds.Left);
            groupBounds = GetWidgetGroupBounds(widgetLeft, widgetTop, widgetWidth, widgetHeight, dpiScale);
        }

        if (groupBounds.Right > taskbarWidth)
        {
            widgetLeft -= (int)Math.Ceiling(groupBounds.Right - taskbarWidth);
        }

        return widgetLeft;
    }

    private Rect GetWidgetGroupBounds(int widgetLeft, int widgetTop, int widgetWidth, int widgetHeight, double dpiScale)
    {
        double left = widgetLeft;
        double right = widgetLeft + widgetWidth;

        if (SettingsManager.Current.TaskbarVisualizerEnabled)
        {
            int visualizerWidth = (int)Math.Round(TaskbarVisualizer.Width * dpiScale);
            int gap = (int)Math.Round(TaskbarWidgetVisualizerGap * dpiScale);

            if (SettingsManager.Current.TaskbarVisualizerPosition == 0)
            {
                left -= visualizerWidth + gap;
            }
            else
            {
                right += visualizerWidth + gap;
            }
        }

        return new Rect(left, widgetTop, Math.Max(0, right - left), widgetHeight);
    }

    private async Task<(bool found, Rect bounds)> TryGetReservedTaskbarBoundsAsync(
        IntPtr taskbarHandle,
        RECT taskbarRect,
        bool isMainTaskbarSelected,
        bool preferLeftSide)
    {
        int taskbarCenter = (taskbarRect.Right - taskbarRect.Left) / 2;

        try
        {
            var (foundWidget, widgetRect) = await GetTaskbarWidgetRectAsync(taskbarHandle);
            if (foundWidget)
            {
                Rect relativeWidgetRect = ToRelativeTaskbarRect(widgetRect, taskbarRect);
                bool widgetIsLeft = relativeWidgetRect.Right < taskbarCenter;
                bool widgetIsRight = relativeWidgetRect.Left > taskbarCenter;

                if ((preferLeftSide && widgetIsLeft) || (!preferLeftSide && widgetIsRight))
                {
                    return (true, relativeWidgetRect);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to query Widgets button bounds for taskbar padding.");
            _widgetElement = null;
        }

        if (preferLeftSide)
        {
            return (false, Rect.Empty);
        }

        try
        {
            if (!isMainTaskbarSelected)
            {
                var (foundTray, secondaryTrayRect) = await GetSystemTrayRectAsync(taskbarHandle);
                return foundTray
                    ? (true, ToRelativeTaskbarRect(secondaryTrayRect, taskbarRect))
                    : (false, Rect.Empty);
            }

            if (_trayHandle == IntPtr.Zero || _lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
            {
                _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
            }

            if (_trayHandle == IntPtr.Zero)
            {
                return (false, Rect.Empty);
            }

            GetWindowRect(_trayHandle, out RECT trayRectNative);
            Rect mainTrayRect = new(trayRectNative.Left, trayRectNative.Top, trayRectNative.Right - trayRectNative.Left, trayRectNative.Bottom - trayRectNative.Top);
            return (true, ToRelativeTaskbarRect(mainTrayRect, taskbarRect));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to query system tray bounds for taskbar padding.");
            _trayElement = null;
            _trayHandle = IntPtr.Zero;
            return (false, Rect.Empty);
        }
    }

    private static Rect ToRelativeTaskbarRect(Rect absoluteRect, RECT taskbarRect)
    {
        return new Rect(
            absoluteRect.Left - taskbarRect.Left,
            absoluteRect.Top - taskbarRect.Top,
            absoluteRect.Width,
            absoluteRect.Height);
    }

    private static bool RectsOverlapHorizontally(Rect first, Rect second)
    {
        return first.Left < second.Right && first.Right > second.Left;
    }

    private void ResetTaskbarElementCaches()
    {
        _trayHandle = IntPtr.Zero;
        _widgetElement = null;
        _trayElement = null;
        _taskbarFrameElement = null;
        _pendingAutomationTasks.Clear();
    }

    private Rect PositionVisualizer(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected)
    {
        if (!SettingsManager.Current.TaskbarVisualizerEnabled)
            return Rect.Empty;

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int visualizerTop = (taskbarHeight - (int)(TaskbarVisualizer.Height * dpiScale)) / 2 - 1; // -1 to align because Windows taskbar positions native elements slightly above the exact center

        int visualizerWidthPhysical = (int)(TaskbarVisualizer.Width * dpiScale);
        int gapPhysical = (int)(TaskbarWidgetVisualizerGap * dpiScale);

        int widgetLeftPhysical = (int)(Canvas.GetLeft(Widget) * dpiScale);
        int visualWidgetWidth = (int)(Widget.Width * _scale * dpiScale);

        int visualizerLeft = 0;
        switch (SettingsManager.Current.TaskbarVisualizerPosition)
        {
            case 0: // left aligned next to widget
                visualizerLeft = widgetLeftPhysical - visualizerWidthPhysical - gapPhysical;
                break;

            case 1: // right aligned next to widget
                visualizerLeft = widgetLeftPhysical + visualWidgetWidth + gapPhysical;
                break;
        }

        // Set visualizer position within canvas
        Canvas.SetLeft(TaskbarVisualizer, visualizerLeft / dpiScale);
        Canvas.SetTop(TaskbarVisualizer, visualizerTop / dpiScale);

        return new Rect(Canvas.GetLeft(TaskbarVisualizer) * dpiScale, Canvas.GetTop(TaskbarVisualizer) * dpiScale, TaskbarVisualizer.Width * dpiScale, TaskbarVisualizer.Height * dpiScale);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateUi(title, artist, icon, playbackStatus, playbackControls));
            return;
        }

        // Check premium status - hide widget if not unlocked
        if ((!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked))
        {
            if (_timer.IsEnabled) // pause timer to save resources
                _timer.Stop();

            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
            });
            return;
        }
        
        // Autohide - Widget hides when playback is paused
        _lastPlaybackStatus = playbackStatus;
        
        if ((SettingsManager.Current.TaskbarWidgetAutoHide))
        {
            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = null;

                Dispatcher.Invoke(() => 
                {
                    Visibility = Visibility.Visible;
                });
            }
            else
            {
                // Start delayed hide
                if (_autoHideTimer == null)
                {
                    _autoHideTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(750)
                    };

                    _autoHideTimer.Tick += (s, e) =>
                    {
                        _autoHideTimer.Stop();
                        _autoHideTimer = null;

                        if (_lastPlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Visibility = Visibility.Collapsed;
                            });
                        }
                    };

                    _autoHideTimer.Start();
                }
            }
        }

        if (!_timer.IsEnabled)
            _timer.Start();

        // Delegate UI update to widget control
        Widget.UpdateUi(title, artist, icon, playbackStatus, playbackControls);

        // Update taskbar visualizer background style as well
        TaskbarVisualizer?.UpdateBackground(icon);

        // Update position after UI change
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility.Visible;
        });
    }

    private async Task<(bool, Rect)> GetTaskbarXamlElementRectAsync(IntPtr taskbarHandle, AutomationElement? elementCache, string elementName, Action<AutomationElement?> updateCache)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        try
        {
            // reset if monitor changed
            if (_lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
            {
                updateCache(null);
                elementCache = null;
            }

            // find widget in XAML
            if (elementCache == null)
            {
                if (_pendingAutomationTasks.TryGetValue(elementName, out var pendingTask) && !pendingTask.IsCompleted)
                    return (false, Rect.Empty);

                AutomationElement? found = null;
                var findTask = Task.Run(() =>
                {
                    var root = AutomationElement.FromHandle(taskbarHandle);
                    found = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, elementName));
                });
                _pendingAutomationTasks[elementName] = findTask;

                using (var cts = new CancellationTokenSource(1000))
                {
                    try
                    {
                        await findTask.WaitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warn("Timeout querying taskbar XAML element: " + elementName);
                        return (false, Rect.Empty);
                    }
                }

                elementCache = found;
                updateCache(found);
            }

            if (elementCache == null) // widget most likely disabled
                return (false, Rect.Empty);

            try
            {
                if (_pendingAutomationTasks.TryGetValue(elementName, out var pendingTask) && !pendingTask.IsCompleted)
                {
                    updateCache(null);
                    return (false, Rect.Empty);
                }

                var cachedElement = elementCache;
                var boundsTask = Task.Run(() => cachedElement.Current.BoundingRectangle);
                _pendingAutomationTasks[elementName] = boundsTask;

                using (var cts = new CancellationTokenSource(500))
                {
                    try
                    {
                        await boundsTask.WaitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warn("Timeout getting bounds for taskbar XAML element: " + elementName);
                        updateCache(null);
                        return (false, Rect.Empty);
                    }
                }

                Rect elementRect = await boundsTask;

                if (elementRect == Rect.Empty) // widget shown before but most likely disabled now
                {
                    updateCache(null); // reset cache
                    return (false, Rect.Empty);
                }

                return (true, elementRect);
            }
            catch (ElementNotAvailableException)
            {
                // element became stale, reset cache
                Logger.Warn("Taskbar XAML element became stale, resetting cache: " + elementName);
                updateCache(null);
                return (false, Rect.Empty);
            }
        }
        catch (COMException ex)
        {
            Logger.Warn(ex, "COM error retrieving taskbar XAML element Rect: " + elementName);
            updateCache(null); // reset cache on error
            return (false, Rect.Empty);
        }
        catch (ElementNotAvailableException)
        {
            Logger.Warn("Taskbar XAML element not available, resetting cache: " + elementName);
            updateCache(null);
            return (false, Rect.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving taskbar XAML element Rect: " + elementName);
            updateCache(null); // reset cache on error
            return (false, Rect.Empty);
        }
    }

    private Task<(bool, Rect)> GetTaskbarWidgetRectAsync(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRectAsync(taskbarHandle, _widgetElement, "WidgetsButton", (e) => _widgetElement = e);
    }

    private Task<(bool, Rect)> GetSystemTrayRectAsync(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRectAsync(taskbarHandle, _trayElement, "SystemTrayIcon", (e) => _trayElement = e);
    }

    private Task<(bool, Rect)> GetTaskbarFrameRectAsync(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRectAsync(taskbarHandle, _taskbarFrameElement, "TaskbarFrame", (e) => _taskbarFrameElement = e);
    }
}
