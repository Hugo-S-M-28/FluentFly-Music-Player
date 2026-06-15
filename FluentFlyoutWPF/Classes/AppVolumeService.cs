using System;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using FluentFlyout.Classes.Settings;

namespace FluentFlyoutWPF.Classes;

public class AppVolumeService : IAudioSessionEventsHandler, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static AppVolumeService? _instance;
    private static readonly object _instanceLock = new();

    private MMDevice? _defaultDevice;
    private AudioSessionControl? _sessionControl;
    private bool _isDisposed;

    public event EventHandler<float>? VolumeChanged;

    public static AppVolumeService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new AppVolumeService();
                }
            }
            return _instance;
        }
    }

    private AppVolumeService()
    {
        InitializeDevice();
        AudioDeviceMonitor.Instance.DefaultDeviceChanged += (s, e) => InitializeDevice();
    }

    private void InitializeDevice()
    {
        try
        {
            CleanupSession();

            if (_defaultDevice != null)
            {
                _defaultDevice.Dispose();
                _defaultDevice = null;
            }

            using (var enumerator = new MMDeviceEnumerator())
            {
                _defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            
            FindSession();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize app volume monitoring");
        }
    }

    private void CleanupSession()
    {
        if (_sessionControl != null)
        {
            try
            {
                _sessionControl.UnRegisterEventClient(this);
                _sessionControl.Dispose();
                _sessionControl = null;
            }
            catch { }
        }
    }

    private bool FindSession()
    {
        try
        {
            if (_defaultDevice == null) return false;

            var sessionManager = _defaultDevice.AudioSessionManager;
            sessionManager.RefreshSessions();

            int processId = Process.GetCurrentProcess().Id;

            for (int i = 0; i < sessionManager.Sessions.Count; i++)
            {
                var session = sessionManager.Sessions[i];
                if (session.GetProcessID == (uint)processId)
                {
                    _sessionControl = session;
                    _sessionControl.RegisterEventClient(this);

                    try
                    {
                        // Explicitly set the session branding for the Volume Mixer
                        _sessionControl.DisplayName = "FluentFlyout";
                        _sessionControl.IconPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Failed to set audio session branding");
                    }

                    Logger.Info($"AppVolumeService bound to session for PID: {processId}");
                    return true;
                }
            }

            Logger.Info("AppVolumeService: Could not find audio session for current process.");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Error searching for audio session");
        }
        return false;
    }

    public void OnVolumeChanged(float volume, bool isMuted)
    {
        VolumeChanged?.Invoke(this, volume);
    }

    public void OnDisplayNameChanged(string displayName) { }
    public void OnIconPathChanged(string iconPath) { }
    public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
    public void OnGroupingParamChanged(ref Guid groupingId) { }
    public void OnStateChanged(AudioSessionState state) { }
    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { }

    public float GetVolume()
    {
        try
        {
            return _sessionControl?.SimpleAudioVolume.Volume ?? 1f;
        }
        catch
        {
            return 1f;
        }
    }

    public void SetVolume(float volume)
    {
        try
        {
            if (_sessionControl != null)
            {
                _sessionControl.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
            }
            else
            {
                // If session is null, maybe it just started playing, try to initialize again
                InitializeDevice();
                if (_sessionControl != null)
                {
                    _sessionControl.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to set app volume");
        }
    }

    public void RefreshSession()
    {
        if (!FindSession())
        {
            // If not found, try one more time after a short delay (session might be still initializing)
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);
                FindSession();
            });
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_sessionControl != null)
        {
            _sessionControl.UnRegisterEventClient(this);
            _sessionControl.Dispose();
        }

        if (_defaultDevice != null)
        {
            _defaultDevice.Dispose();
            _defaultDevice = null;
        }

        GC.SuppressFinalize(this);
    }
}
