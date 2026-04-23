using FluentFlyout.Classes;
using System;
using System.Windows;
using System.Windows.Interop;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes;

public class SystemHookService : IDisposable
{
    private int WM_TASKBARCREATED;
    private int WM_SHELLHOOK;
    
    private IntPtr _hwnd;
    private bool _isDisposed;

    public event EventHandler<bool>? MediaOrVolumeCommandReceived;
    public event EventHandler? ExplorerRestarted;
    public event EventHandler? ThemeChanged;

    public void Initialize(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        
        WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");
        WM_SHELLHOOK = RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(_hwnd);
        
        HwndSource source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_SHELLHOOK && wParam == HSHELL_APPCOMMAND)
        {
            int highWord = (int)(lParam >> 16);
            int cmd = highWord & 0x0FFF;
            int device = highWord & 0xF000;

            bool isMediaCommand = cmd switch
            {
                APPCOMMAND_MEDIA_PLAY_PAUSE => true,
                APPCOMMAND_MEDIA_NEXTTRACK => true,
                APPCOMMAND_MEDIA_PREVIOUSTRACK => true,
                APPCOMMAND_MEDIA_STOP => true,
                _ => false
            };

            bool isVolumeCommand = cmd switch
            {
                APPCOMMAND_VOLUME_MUTE => true,
                APPCOMMAND_VOLUME_DOWN => true,
                APPCOMMAND_VOLUME_UP => true,
                _ => false
            };

            if (!isMediaCommand && !isVolumeCommand)
                return 0;

            bool isKeyCommand = device == FAPPCOMMAND_KEY;

            if (!isKeyCommand)
                return 0;

            if (isMediaCommand)
            {
                handled = true;
            }
            
            // Fire event so subscriber can handle the logic (e.g. showing flyout)
            MediaOrVolumeCommandReceived?.Invoke(this, isVolumeCommand);
        }
        else if (msg == WM_TASKBARCREATED)
        {
            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
            handled = true;
            return 0;
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        if (_hwnd != IntPtr.Zero)
        {
            DeregisterShellHookWindow(_hwnd);
            HwndSource source = HwndSource.FromHwnd(_hwnd);
            source?.RemoveHook(WndProc);
        }
        
        _isDisposed = true;
    }
}
