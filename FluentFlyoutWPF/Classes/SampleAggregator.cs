using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace FluentFlyoutWPF.Classes;

public class SampleAggregator : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _fftSize;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _fftResults;
    private int _fftPos;

    public event EventHandler<float[]>? FftCalculated;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SampleAggregator(ISampleProvider source, int fftSize = 1024)
    {
        if (!IsPowerOfTwo(fftSize)) throw new ArgumentException("FFT size must be a power of two");
        _source = source;
        _fftSize = fftSize;
        _fftBuffer = new Complex[fftSize];
        _fftResults = new float[fftSize / 2];
    }

    private static bool IsPowerOfTwo(int x) => (x != 0) && ((x & (x - 1)) == 0);

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        for (int n = 0; n < samplesRead; n += WaveFormat.Channels)
        {
            AddSample(buffer[offset + n]);
        }

        return samplesRead;
    }

    private void AddSample(float value)
    {
        _fftBuffer[_fftPos].X = (float)(value * FastFourierTransform.HammingWindow(_fftPos, _fftSize));
        _fftBuffer[_fftPos].Y = 0;
        _fftPos++;

        if (_fftPos >= _fftSize)
        {
            _fftPos = 0;
            FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2.0), _fftBuffer);

            for (int i = 0; i < _fftSize / 2; i++)
            {
                // Magnitude
                _fftResults[i] = (float)Math.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);
            }

            FftCalculated?.Invoke(this, _fftResults);
        }
    }
}
