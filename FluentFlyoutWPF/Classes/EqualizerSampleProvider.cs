using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace FluentFlyoutWPF.Classes;

/// <summary>
/// ISampleProvider that applies a 10-band parametric equalizer using NAudio BiQuadFilter.
/// Sits in the audio pipeline between the AudioFileReader and the SampleAggregator.
/// Each band uses a peaking EQ filter that boosts or cuts at the specified frequency.
/// </summary>
public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EqualizerService _eqService;
    private readonly BiQuadFilter[,] _filters; // [bandIndex, channelIndex]
    private readonly int _channels;
    private float[] _lastAppliedGains;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _eqService = EqualizerService.Instance;
        _channels = source.WaveFormat.Channels;

        int bandCount = _eqService.Bands.Length;
        _filters = new BiQuadFilter[bandCount, _channels];
        _lastAppliedGains = new float[bandCount];

        // Initialize filters for each band and channel
        for (int band = 0; band < bandCount; band++)
        {
            var eqBand = _eqService.Bands[band];
            for (int ch = 0; ch < _channels; ch++)
            {
                _filters[band, ch] = BiQuadFilter.PeakingEQ(
                    source.WaveFormat.SampleRate,
                    eqBand.Frequency,
                    eqBand.Bandwidth,
                    eqBand.Gain);
            }
            _lastAppliedGains[band] = eqBand.Gain;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        // Skip processing if EQ is disabled
        if (!_eqService.IsEnabled)
            return samplesRead;

        // Check if any gains changed and update filters accordingly
        UpdateFiltersIfNeeded();

        // Apply all band filters to each sample
        for (int n = 0; n < samplesRead; n++)
        {
            int ch = n % _channels;
            float sample = buffer[offset + n];

            for (int band = 0; band < _eqService.Bands.Length; band++)
            {
                // Only apply if gain is non-zero for performance
                if (Math.Abs(_lastAppliedGains[band]) > 0.01f)
                {
                    sample = _filters[band, ch].Transform(sample);
                }
            }

            buffer[offset + n] = sample;
        }

        return samplesRead;
    }

    /// <summary>
    /// Checks if any band gain has changed and re-creates the BiQuadFilter if so.
    /// This allows real-time adjustment of EQ bands while audio is playing.
    /// </summary>
    private void UpdateFiltersIfNeeded()
    {
        for (int band = 0; band < _eqService.Bands.Length; band++)
        {
            float currentGain = _eqService.Bands[band].Gain;
            if (Math.Abs(_lastAppliedGains[band] - currentGain) > 0.01f)
            {
                var eqBand = _eqService.Bands[band];
                for (int ch = 0; ch < _channels; ch++)
                {
                    _filters[band, ch] = BiQuadFilter.PeakingEQ(
                        _source.WaveFormat.SampleRate,
                        eqBand.Frequency,
                        eqBand.Bandwidth,
                        currentGain);
                }
                _lastAppliedGains[band] = currentGain;
            }
        }
    }
}
