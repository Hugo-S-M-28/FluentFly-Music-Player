using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentFlyout.Classes;

namespace FluentFlyoutWPF.Classes;

public class HotKeyEventArgs : EventArgs
{
    public int VkCode { get; }
    public bool IsKeyDown { get; }

    public HotKeyEventArgs(int vkCode, bool isKeyDown)
    {
        VkCode = vkCode;
        IsKeyDown = isKeyDown;
    }
}

public class HotKeyService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static HotKeyService? _instance;
    private static readonly object _instanceLock = new();

    private IntPtr _hookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private bool _isDisposed;

    public event EventHandler<HotKeyEventArgs>? HotKeyPressed;

    public static HotKeyService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new HotKeyService();
                }
            }
            return _instance;
        }
    }

    private HotKeyService()
    {
        _hookProc = HookCallback;
        _hookId = SetHook(_hookProc);
    }

    private IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
        {
            return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc,
                NativeMethods.GetModuleHandle(curModule?.ModuleName ?? "FluentFlyout"), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            bool isKeyDown = wParam == (IntPtr)NativeMethods.WM_KEYDOWN;
            bool isKeyUp = wParam == (IntPtr)NativeMethods.WM_KEYUP;

            if (isKeyDown || isKeyUp)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                HotKeyPressed?.Invoke(this, new HotKeyEventArgs(vkCode, isKeyDown));
            }
        }
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }
}
