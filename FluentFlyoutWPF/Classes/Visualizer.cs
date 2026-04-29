using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes
{
    public class Visualizer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private static Visualizer? _instance;
        public static Visualizer Instance => _instance ??= new Visualizer();

        public int BarCount { get; private set; } = 10;
        private readonly int ImageWidth = 76 * 3;
        private readonly int ImageHeight = 32 * 3;
        private readonly int BarSpacing = 2 * 3;

        // Pre-computed frequency band data to avoid recalculating every frame
        private const double MinFreq = 40.0;   // Hz
        private const double MaxFreq = 8000.0; // Hz

        private struct FrequencyBand
        {
            public int StartBin;
            public int EndBin;
            public float AWeightMultiplier;
        }

        private FrequencyBand[]? _frequencyBands;
        private int _cachedSampleRate;
        private int _cachedBarCount;
        private int _cachedStereoMode;

        private WasapiLoopbackCapture? _capture;
        private MMDevice? _renderDevice;
        private float[]? _barValues;
        private WriteableBitmap? _bitmap;
        private bool _isRunning;
        private readonly object _lock = new();

        private int _clientCount = 0;

        public void AddClient()
        {
            lock (_lock)
            {
                _clientCount++;
            }
            if (ShouldBeRunning) Start();
        }

        public void RemoveClient()
        {
            lock (_lock)
            {
                _clientCount--;
                if (_clientCount < 0) _clientCount = 0;
            }
            if (!ShouldBeRunning) Stop();
        }

        public bool ShouldBeRunning => SettingsManager.Current.TaskbarVisualizerEnabled || _clientCount > 0;

        private readonly int _fftLength = 1024;
        private int _fftPosL = 0;
        private readonly Complex[] _fftBufferL;
        private readonly Complex[] _fftBufferR;
        private readonly float[] _hammingWindow;

        private readonly int _targetFps = 60;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private DateTime _lastFftTime = DateTime.UtcNow;
        private SolidColorBrush? _cachedAccentBrush;
        private DateTime _lastBrushUpdate = DateTime.MinValue;


        private System.Timers.Timer? _captureWatchdog;
        private DateTime _lastDataAvailableUtc = DateTime.MinValue;
        private int _restartInProgress; // 0=false, 1=true (Interlocked)
        private readonly object _barValuesLock = new(); // Lock for thread-safe access to _barValues

        private readonly struct BarGeometry
        {
            public readonly float Left, Right, Top, Bottom;
            public readonly float InnerLeft, InnerRight, InnerTop, InnerBottom;

            public BarGeometry(int x, int width, int y, int endY, float radius)
            {
                Left = x;
                Right = x + width;
                Top = y;
                Bottom = endY;

                InnerLeft = Left + radius;
                InnerRight = Right - radius;
                InnerTop = Top + radius;
                InnerBottom = Bottom - radius;
            }
        }

        public WriteableBitmap? Bitmap
        {
            get
            {
                lock (_lock)
                {
                    return _bitmap;
                }
            }
        }

        public Visualizer()
        {
            InitializeBitmap();

            _fftBufferL = new Complex[_fftLength];
            _fftBufferR = new Complex[_fftLength];
            _hammingWindow = new float[_fftLength];
            for (int i = 0; i < _fftLength; i++)
            {
                _hammingWindow[i] = (float)FastFourierTransform.HammingWindow(i, _fftLength);
            }

            SetBarCountInternal(SettingsManager.Current.TaskbarVisualizerBarCount);
            AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;
            TryRegisterSystemEvents();
        }

        private void TryRegisterSystemEvents()
        {
            try
            {
                SystemEvents.SessionSwitch += OnSessionSwitch;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                // On some environments (e.g. non-interactive sessions), SystemEvents may not be available.
                Logger.Warn(ex, "Failed to register SystemEvents handlers for visualizer auto-restart");
            }
        }

        private void TryUnregisterSystemEvents()
        {
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to unregister SystemEvents handlers for visualizer auto-restart");
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (!ShouldBeRunning)
                return;

            // When unlocking after device disconnect (e.g. Bluetooth earbuds), WASAPI loopback can get stuck.
            // Restart capture on unlock / logon to recover without user action.
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
            {
                RequestRestart($"session switch: {e.Reason}");
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (!ShouldBeRunning)
                return;

            if (e.Mode == PowerModes.Resume)
            {
                RequestRestart("power resume");
            }
        }

        private void InitializeBitmap()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    _bitmap = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
                }
            });
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            if (e.DataFlow != DataFlow.Render || e.Role != Role.Multimedia)
                return;

            // Even if capture isn't currently running (e.g. restart attempt failed while the device was reconfiguring),
            // we still want to try restarting as soon as Windows reports a usable default endpoint again.
            if (!ShouldBeRunning)
                return;
            RequestRestart("default audio output device changed");
        }

        private void RequestRestart(string reason)
        {
            if (!ShouldBeRunning)
                return;

            if (Interlocked.Exchange(ref _restartInProgress, 1) == 1)
                return;

            Logger.Info($"Restarting visualizer ({reason})");

            Task.Run(async () =>
            {
                try
                {
                    Stop();

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        await Task.Delay(500);
                        Start();
                        if (_isRunning)
                            return;
                        Logger.Warn($"Visualizer restart attempt {attempt + 1} failed, retrying...");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Visualizer restart failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _restartInProgress, 0);
                }
            });
        }

        public static void ResizeBarList(int newBarCount)
        {
            Instance.SetBarCountInternal(newBarCount);
        }

        private void SetBarCountInternal(int newBarCount)
        {
            BarCount = newBarCount;
            _barValues = new float[BarCount];
            RebuildFrequencyBands();
        }

        public void Start()
        {
            if (_isRunning)
                return;

            float barCount = BarCount >= 0 ? BarCount : 8;
            _barValues = new float[(int)barCount];

            try
            {
                // Explicitly bind to the current default render endpoint.
                // Using the parameterless capture can throw transient COM errors when the default endpoint is
                // reconfiguring (e.g. Bluetooth earbuds disconnect/reconnect around lock/unlock).
                _renderDevice?.Dispose();
                _renderDevice = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();

                if (_renderDevice == null)
                {
                    return;
                }

                _capture = new WasapiLoopbackCapture(_renderDevice);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isRunning = true;
                _lastDataAvailableUtc = DateTime.UtcNow;

                // Rebuild frequency bands if sample rate changed
                RebuildFrequencyBands();

                // automatic update timer in case audio data is not updated
                _captureWatchdog = new(500)
                {
                    AutoReset = false
                };
                _captureWatchdog.Elapsed += (_, _) =>
                {
                    if (_isRunning)
                    {
                        lock (_barValuesLock)
                        {
                            if (_barValues != null)
                            {
                                for (int i = 0; i < _barValues.Length; i++)
                                {
                                    _barValues[i] = 0;
                                }
                            }
                        }
                        UpdateBitmap();

                        if (!SettingsManager.Current.TaskbarVisualizerBaseline) // if baseline is enabled, don't switch the setting
                            SettingsManager.Current.TaskbarVisualizerHasContent = false;

                        // If we stop receiving loopback callbacks entirely (common after lock/unlock + device changes),
                        // the timer fires once and then never again. Use it as a recovery trigger.
                        var silenceFor = DateTime.UtcNow - _lastDataAvailableUtc;
                        if (silenceFor > TimeSpan.FromSeconds(2))
                        {
                            RequestRestart($"no audio callbacks for {silenceFor.TotalSeconds:0.0}s");
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start visualizer");
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            _capture?.DataAvailable -= OnDataAvailable;
            _capture?.RecordingStopped -= OnRecordingStopped;
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;

            _renderDevice?.Dispose();
            _renderDevice = null;

            _captureWatchdog?.Stop();
            _captureWatchdog?.Dispose();
            _captureWatchdog = null;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || e.BytesRecorded == 0)
                return;

            _lastDataAvailableUtc = DateTime.UtcNow;

            _captureWatchdog?.Stop();
            _captureWatchdog?.Start();

            int bytesPerSample = _capture!.WaveFormat.BitsPerSample / 8;
            int channels = _capture.WaveFormat.Channels;
            int frameCount = e.BytesRecorded / (bytesPerSample * channels);
            bool ignoreAudio = !SettingsManager.Current.SystemMediaControlEnabled && !MusicPlayerService.Instance.IsPlaying;

            for (int frame = 0; frame < frameCount; frame++)
            {
                float sampleL = 0, sampleR = 0;

                if (!ignoreAudio)
                {
                    int offset = frame * channels * bytesPerSample;

                    if (bytesPerSample == 4)
                    {
                        sampleL = BitConverter.ToSingle(e.Buffer, offset);
                        sampleR = channels >= 2 ? BitConverter.ToSingle(e.Buffer, offset + 4) : sampleL;
                    }
                    else if (bytesPerSample == 2)
                    {
                        sampleL = BitConverter.ToInt16(e.Buffer, offset) / 32768f;
                        sampleR = channels >= 2 ? BitConverter.ToInt16(e.Buffer, offset + 2) / 32768f : sampleL;
                    }
                }

                float windowVal = _hammingWindow[_fftPosL];

                _fftBufferL[_fftPosL].X = sampleL * windowVal;
                _fftBufferL[_fftPosL].Y = 0;
                _fftBufferR[_fftPosL].X = sampleR * windowVal;
                _fftBufferR[_fftPosL].Y = 0;
                _fftPosL++;

                // When buffer isn't full, skip processing and continue filling
                if (_fftPosL < _fftLength)
                    continue;

                // perform FFT on both channels
                _fftPosL = 0;
                ProcessFftData();

                // Update UI with frame rate limiting
                DateTime now = DateTime.UtcNow;
                double minFrameTime = 1000.0 / _targetFps;
                double timeSinceLastUpdate = (now - _lastUpdateTime).TotalMilliseconds;

                if (timeSinceLastUpdate < minFrameTime)
                    continue;

                _lastUpdateTime = now;
                SettingsManager.Current.TaskbarVisualizerHasContent = true;

                if (SettingsManager.Current.TaskbarVisualizerBaseline)
                {
                    // if baseline is enabled, we want to keep showing the bars even when they are all zero
                    UpdateBitmap();
                    break;
                }

                // check if bars are all zero, if so set has content to false to disable hover effect
                bool allZero = true;
                for (int j = 0; j < BarCount; j++)
                {
                    if (_barValues != null && _barValues[j] > 0.01f)
                    {
                        allZero = false;
                        break;
                    }
                }

                // update bars if they have content
                if (!allZero)
                    UpdateBitmap();
                else
                    SettingsManager.Current.TaskbarVisualizerHasContent = false;
            }
        }

        /// <summary>
        /// Rebuilds the cached frequency band lookup table.
        /// Called when BarCount or sample rate changes.
        /// </summary>
        private void RebuildFrequencyBands()
        {
            int sampleRate = _capture?.WaveFormat.SampleRate ?? 44100;
            int barCount = BarCount;
            int stereoMode = SettingsManager.Current.TaskbarVisualizerStereoMode;

            // Skip if nothing changed
            if (_frequencyBands != null && _cachedSampleRate == sampleRate && _cachedBarCount == barCount && _cachedStereoMode == stereoMode)
                return;

            _cachedSampleRate = sampleRate;
            _cachedBarCount = barCount;
            _cachedStereoMode = stereoMode;

            double frequencyPerBin = (double)sampleRate / _fftLength;
            int halfFft = _fftLength / 2;

            // In stereo mirror mode, each side (half of the bars) displays the full frequency range.
            int distinctBands = (stereoMode == 1) ? (barCount / 2) : barCount;
            if (distinctBands < 1) distinctBands = 1;

            var bands = new FrequencyBand[distinctBands];

            for (int i = 0; i < distinctBands; i++)
            {
                double startFreq = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)i / distinctBands);
                double endFreq = MinFreq * Math.Pow(MaxFreq / MinFreq, (double)(i + 1) / distinctBands);

                int startBin = (int)(startFreq / frequencyPerBin);
                int endBin = (int)(endFreq / frequencyPerBin);

                if (endBin <= startBin) endBin = startBin + 1;
                if (endBin >= halfFft) endBin = halfFft - 1;

                // Pre-compute A-weighting multiplier for the center frequency of this band
                double centerFreq = startFreq + (endFreq - startFreq) / 2.0;
                float aWeight = AWeighting(centerFreq);

                bands[i] = new FrequencyBand
                {
                    StartBin = startBin,
                    EndBin = endBin,
                    AWeightMultiplier = MathF.Pow(10f, aWeight / 20f)
                };
            }

            _frequencyBands = bands;
            Logger.Debug($"Rebuilt frequency bands: {distinctBands} bands, {sampleRate} Hz sample rate, StereoMode: {stereoMode}");
        }

        private void ProcessFftData()
        {
            int fftLog = (int)Math.Log(_fftLength, 2.0);

            // Run FFT on both channels
            FastFourierTransform.FFT(true, fftLog, _fftBufferL);
            FastFourierTransform.FFT(true, fftLog, _fftBufferR);

            // Ensure frequency bands are cached and correct for current mode
            RebuildFrequencyBands();

            var bands = _frequencyBands!;

            float minDb = -20f - (SettingsManager.Current.TaskbarVisualizerAudioSensitivity * 8f);
            float maxDb = -45f + (SettingsManager.Current.TaskbarVisualizerAudioPeakLevel * 5f);

            if (minDb >= maxDb) minDb = maxDb - 10f;
            
            float dbRangeInv = 1f / (maxDb - minDb);

            bool stereoMirror = SettingsManager.Current.TaskbarVisualizerStereoMode == 1;
            float[] currentBars = new float[BarCount];
            float maxDbDetected = -100f;

            if (stereoMirror && BarCount >= 2)
            {
                // Stereo mirror mode: left half = L channel, right half = R channel
                int halfBars = BarCount / 2;
                bool isOdd = BarCount % 2 != 0;

                // Process left channel (rendered from center to left, so reversed)
                for (int i = 0; i < halfBars; i++)
                {
                    ref readonly FrequencyBand band = ref bands[i];
                    float maxAmp = GetMaxAmplitude(_fftBufferL, band);
                    maxAmp *= band.AWeightMultiplier;
                    if (maxAmp < 1e-7f) maxAmp = 1e-7f;
                    float db = 20f * (float)Math.Log10(maxAmp);
                    if (db > maxDbDetected) maxDbDetected = db;
                    float intensity = Math.Clamp((db - minDb) * dbRangeInv, 0f, 1f);
                    // Map band i to mirrored position: center-1 going left
                    currentBars[halfBars - 1 - i] = intensity;
                }

                // Process right channel (rendered from center to right)
                for (int i = 0; i < halfBars; i++)
                {
                    ref readonly FrequencyBand band = ref bands[i];
                    float maxAmp = GetMaxAmplitude(_fftBufferR, band);
                    maxAmp *= band.AWeightMultiplier;
                    if (maxAmp < 1e-7f) maxAmp = 1e-7f;
                    float db = 20f * (float)Math.Log10(maxAmp);
                    if (db > maxDbDetected) maxDbDetected = db;
                    float intensity = Math.Clamp((db - minDb) * dbRangeInv, 0f, 1f);
                    // Right channel starts at halfBars if even, or halfBars + 1 if odd
                    currentBars[halfBars + (isOdd ? 1 : 0) + i] = intensity;
                }

                // If odd, fill the very center bar with the average of the lowest bands (i=0) of L and R
                if (isOdd)
                {
                    ref readonly FrequencyBand band = ref bands[0];
                    float maxAmpL = GetMaxAmplitude(_fftBufferL, band);
                    float maxAmpR = GetMaxAmplitude(_fftBufferR, band);
                    float maxAmp = (maxAmpL + maxAmpR) * 0.5f;
                    maxAmp *= band.AWeightMultiplier;
                    if (maxAmp < 1e-7f) maxAmp = 1e-7f;
                    float db = 20f * (float)Math.Log10(maxAmp);
                    if (db > maxDbDetected) maxDbDetected = db;
                    float intensity = Math.Clamp((db - minDb) * dbRangeInv, 0f, 1f);
                    currentBars[halfBars] = intensity;
                }
            }
            else
            {
                // Mono mode: average L+R channels
                for (int i = 0; i < BarCount; i++)
                {
                    ref readonly FrequencyBand band = ref bands[i];

                    float maxAmpL = GetMaxAmplitude(_fftBufferL, band);
                    float maxAmpR = GetMaxAmplitude(_fftBufferR, band);
                    float maxAmplitude = (maxAmpL + maxAmpR) * 0.5f;

                    maxAmplitude *= band.AWeightMultiplier;
                    if (maxAmplitude < 1e-7f) maxAmplitude = 1e-7f;

                    float db = 20f * (float)Math.Log10(maxAmplitude);
                    if (db > maxDbDetected) maxDbDetected = db;

                    float intensity = Math.Clamp((db - minDb) * dbRangeInv, 0f, 1f);
                    currentBars[i] = intensity;
                }
            }

            // Update UI absolute level meter (Map -100dB..10dB to 0..100)
            float level = (maxDbDetected + 100f) / 110f * 100f;
            SettingsManager.Current.TaskbarVisualizerCurrentLevel = Math.Clamp(level, 0, 100);

            // Update calibrated level meter based on user sensitivity and peak settings
            float calibratedLevel = (maxDbDetected - minDb) / (maxDb - minDb) * 100f;
            SettingsManager.Current.TaskbarVisualizerCalibratedLevel = Math.Clamp(calibratedLevel, 0, 100);

            DateTime now = DateTime.UtcNow;
            float elapsed = (float)(now - _lastFftTime).TotalSeconds;
            _lastFftTime = now;
            float decay = MathF.Pow(0.005f, elapsed); // Adapts to FFT rate, ~0.92 at 60fps

            if (_barValues != null)
            {
                lock (_barValuesLock)
                {
                    for (int i = 0; i < BarCount; i++)
                    {
                        if (currentBars[i] > _barValues[i])
                        {
                            // Jump up quickly
                            _barValues[i] = currentBars[i];
                        }
                        else
                        {
                            // Fall down smoothly based on elapsed time
                            _barValues[i] = (_barValues[i] * decay) + (currentBars[i] * (1f - decay));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Finds the maximum amplitude within a pre-computed frequency band from an FFT buffer.
        /// </summary>
        private static float GetMaxAmplitude(Complex[] fftBuffer, in FrequencyBand band)
        {
            float maxAmplitude = 0;
            for (int j = band.StartBin; j < band.EndBin; j++)
            {
                float amplitude = (float)Math.Sqrt(fftBuffer[j].X * fftBuffer[j].X + fftBuffer[j].Y * fftBuffer[j].Y);
                if (amplitude > maxAmplitude)
                    maxAmplitude = amplitude;
            }
            return maxAmplitude;
        }

        private static float AWeighting(double f)
        {
            double f2 = f * f;
            double num = 12194.0 * 12194.0 * f2 * f2;
            double den = (f2 + 20.6 * 20.6) * Math.Sqrt((f2 + 107.7 * 107.7) * (f2 + 737.9 * 737.9)) * (f2 + 12194.0 * 12194.0);
            return (float)(2.0 + 20.0 * Math.Log10(num / den));
        }


        private void UpdateBitmap()
        {
            if (_bitmap == null)
                return;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (_bitmap == null)
                        return;

                    _bitmap.Lock();

                    try
                    {
                        unsafe
                        {
                            IntPtr pBackBuffer = _bitmap.BackBuffer;
                            int stride = _bitmap.BackBufferStride;
                            int bufferSize = stride * ImageHeight;

                            Span<byte> buffer = new Span<byte>(pBackBuffer.ToPointer(), bufferSize);

                            buffer.Clear();

                            DrawBars(stride, buffer);
                        }

                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, ImageWidth, ImageHeight));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private unsafe void DrawBars(int stride, Span<byte> buffer)
        {
            // Resolve brush with caching (once per second or if not cached)
            DateTime now = DateTime.UtcNow;
            if (_cachedAccentBrush == null || (now - _lastBrushUpdate).TotalSeconds > 1)
            {
                _cachedAccentBrush = BitmapHelper.SavedDominantColors.Count > 0
                    ? BitmapHelper.SavedDominantColors.Last()
                    : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary")
                    ?? new SolidColorBrush(Colors.DeepSkyBlue);
                _lastBrushUpdate = now;
            }
            
            byte b = _cachedAccentBrush.Color.B;
            byte g = _cachedAccentBrush.Color.G;
            byte r = _cachedAccentBrush.Color.R;

            var settings = SettingsManager.Current;
            bool centeredBars = settings.TaskbarVisualizerCenteredBars;
            int barBaseline = settings.TaskbarVisualizerBaseline ? 4 : 0;

            int centerY = ImageHeight / 2;

            // Horizontal layout 
            ComputeLayout(ImageWidth, BarCount, BarSpacing,
                out int barWidth,
                out int offsetX);

            // Radius 
            float baseRadius = GetCornerRadius();

            // AA constants 
            const float aa = 1.25f;
            float invAA = 1f / aa;

            for (int i = 0; i < BarCount; i++)
            {
                int barX = offsetX + i * (barWidth + BarSpacing);

                float val = 0f;
                lock (_barValuesLock)
                {
                    if (_barValues != null && i < _barValues.Length)
                        val = _barValues[i];
                }

                int barHeight = GetBarHeight(val, barBaseline);

                if (barHeight <= 0)
                    continue;

                ComputeVertical(centeredBars, centerY, barHeight, out int barY, out int barEndY);

                // Clamp radius per bar
                float radius = ClampRadius(baseRadius, barWidth, barHeight);
                float radiusSq = radius * radius;

                RasterizeBar(
                    buffer, stride,
                    barX, barWidth,
                    barY, barEndY,
                    centeredBars,
                    radius, radiusSq, invAA,
                    b, g, r);
            }
        }

        private static void ComputeLayout(
            int imageWidth,
            int barCount,
            int spacing,
            out int barWidth,
            out int offsetX)
        {
            int totalSpacing = (barCount - 1) * spacing;

            int availableWidth = imageWidth - totalSpacing - 1;

            barWidth = availableWidth / barCount;

            int usedWidth = barWidth * barCount + totalSpacing;

            // Center safely
            offsetX = (imageWidth - usedWidth) >> 1;
        }

        private void ComputeVertical(bool centered, int centerY, int height, out int y, out int endY)
        {
            if (centered)
            {
                int half = height >> 1; // faster than /2
                y = centerY - half;
                endY = centerY + half;
            }
            else
            {
                y = ImageHeight - height;
                endY = ImageHeight;
            }
        }

        private int GetBarHeight(float value, int baseline)
        {
            return Math.Max((int)(Math.Clamp(value, 0f, 1f) * ImageHeight), baseline);
        }
        private static float GetCornerRadius()
        {
            return 6f / MathF.Max(1f, SettingsManager.Current.TaskbarVisualizerBarCount / 10f);
        }

        private static float ClampRadius(float r, int width, int height)
        {
            float max = MathF.Min(width, height) * 0.5f;
            return r > max ? max : r;
        }

        private unsafe void RasterizeBar(
            Span<byte> buffer,
            int stride,
            int barX,
            int barWidth,
            int barY,
            int barEndY,
            bool centeredBars,
            float radius,
            float radiusSq,
            float invAA,
            byte b, byte g, byte r)
        {
            float left = barX;
            float right = barX + barWidth;
            float top = barY;
            float bottom = barEndY;

            float innerLeft = left + radius;
            float innerRight = right - radius;
            float innerTop = top + radius;
            float innerBottom = bottom - radius;

            for (int y = barY; y < barEndY && y < ImageHeight && y >= 0; y++)
            {
                int row = y * stride;

                for (int x = barX; x < barX + barWidth && x < ImageWidth; x++)
                {
                    int index = row + (x << 2); // x * 4 (bitshift faster)
                    if (index + 3 >= buffer.Length)
                        continue;

                    // CENTER
                    if (x >= innerLeft && x <= innerRight)
                    {
                        WritePixel(buffer, index, b, g, r, 255);
                        continue;
                    }

                    // SIDES
                    if (y >= innerTop && y <= innerBottom)
                    {
                        WritePixel(buffer, index, b, g, r, 255);
                        continue;
                    }

                    // FLAT BOTTOM
                    if (!centeredBars && y >= innerBottom)
                    {
                        WritePixel(buffer, index, b, g, r, 255);
                        continue;
                    }

                    // CORNERS
                    float cx = x < innerLeft ? innerLeft : (x > innerRight ? innerRight : x);
                    float cy = y < innerTop ? innerTop : (y > innerBottom ? innerBottom : y);

                    float dx = x - cx;
                    float dy = y - cy;

                    float distSq = dx * dx + dy * dy;
                    float sdf = (distSq - radiusSq) / (2f * radius);

                    float alpha = 0.5f - sdf * invAA;

                    if (alpha <= 0f)
                        continue;

                    if (alpha > 1f) alpha = 1f;

                    WritePixel(buffer, index, b, g, r, (byte)(255 * alpha));
                }
            }
        }

        private static void WritePixel(Span<byte> buffer, int index, byte b, byte g, byte r, byte a)
        {
            buffer[index] = b;
            buffer[index + 1] = g;
            buffer[index + 2] = r;
            buffer[index + 3] = a;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error(e.Exception, "Visualizer recording stopped due to an error");
            }
        }

        public void Dispose()
        {
            Stop();

            AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            TryUnregisterSystemEvents();

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}