using System;
using NAudio.CoreAudioApi;
using FluentFlyout.Classes.Settings;

namespace FluentFlyoutWPF.Classes;

public class SystemVolumeService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static SystemVolumeService? _instance;
    private static readonly object _instanceLock = new();

    private MMDevice? _defaultDevice;
    private bool _isDisposed;

    public event EventHandler<float>? VolumeChanged;

    public static SystemVolumeService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new SystemVolumeService();
                }
            }
            return _instance;
        }
    }

    private SystemVolumeService()
    {
        InitializeDevice();
        AudioDeviceMonitor.Instance.DefaultDeviceChanged += (s, e) => InitializeDevice();
    }

    private void InitializeDevice()
    {
        try
        {
            if (_defaultDevice != null)
            {
                _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
            }

            var enumerator = new MMDeviceEnumerator();
            _defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _defaultDevice.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
            
            Logger.Info($"SystemVolumeService initialized with device: {_defaultDevice.FriendlyName}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize system volume monitoring");
        }
    }

    private void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
    {
        VolumeChanged?.Invoke(this, data.MasterVolume);
    }

    public float GetVolume()
    {
        try
        {
            return _defaultDevice?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f;
        }
        catch
        {
            return 0f;
        }
    }

    public void SetVolume(float volume)
    {
        try
        {
            if (_defaultDevice != null)
            {
                _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to set system volume");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_defaultDevice != null)
        {
            _defaultDevice.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
        }

        GC.SuppressFinalize(this);
    }
}
