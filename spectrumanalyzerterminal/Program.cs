using System.Numerics;
using NAudio.Wave;

class Program
{
    const int fftSize = 1024;
    const int sampleRate = 44100;
    static float[] circularBuffer = new float[fftSize * 10];
    static int writeIndex = 0;

    static double[] smoothed = null;
    static int[] previousHeights = null;
    static double smoothingFactor = 0.4;

    static void Main()
    {
        var fftWindow = new FftSharp.Windows.Hanning();
        var capture = new WasapiLoopbackCapture();
        int bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
        int channels = capture.WaveFormat.Channels;

        capture.DataAvailable += (s, e) =>
        {
            for (int i = 0; i < e.BytesRecorded; i += bytesPerSample * channels)
            {
                float sample = 0;
                for (int c = 0; c < channels; c++)
                {
                    int offset = i + c * bytesPerSample;
                    sample += BitConverter.ToSingle(e.Buffer, offset);
                }
                sample /= channels;
                AddSampleToBuffer(sample);
            }
        };

        capture.StartRecording();

        new Thread(() =>
        {
            double[] fftInput = new double[fftSize];
            while (true)
            {
                if (TryGetLatestSamples(fftInput))
                {
                    fftWindow.ApplyInPlace(fftInput);
                    Complex[] spectrum = FftSharp.FFT.Forward(fftInput);
                    double[] magnitude = FftSharp.FFT.Magnitude(spectrum);

                    // Smoothing
                    if (smoothed == null || smoothed.Length != magnitude.Length)
                        smoothed = (double[])magnitude.Clone();
                    else
                    {
                        for (int i = 0; i < magnitude.Length; i++)
                            smoothed[i] = smoothingFactor * smoothed[i] + (1 - smoothingFactor) * magnitude[i];
                    }

                    RenderSpectrum(smoothed, capture.WaveFormat.SampleRate);
                }

                Thread.Sleep(16);
            }
        }).Start();

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
        capture.StopRecording();
    }

    static void AddSampleToBuffer(float sample)
    {
        circularBuffer[writeIndex % circularBuffer.Length] = sample;
        writeIndex++;
    }

    static bool TryGetLatestSamples(double[] output)
    {
        if (writeIndex < fftSize) return false;

        int start = (writeIndex - fftSize) % circularBuffer.Length;
        for (int i = 0; i < fftSize; i++)
        {
            output[i] = circularBuffer[(start + i) % circularBuffer.Length];
        }

        return true;
    }

    static void RenderSpectrum(double[] magnitudes, int sampleRate)
    {
        int width = Console.WindowWidth - 1;
        int height = Console.WindowHeight - 2;

        if (previousHeights == null || previousHeights.Length != width)
            previousHeights = new int[width];

        double freqPerBin = sampleRate / (double)fftSize;
        double freqMin = 20;
        double freqMax = 22000;

        for (int x = 0; x < width; x++)
        {
            double logFreqMin = Math.Log10(freqMin);
            double logFreqMax = Math.Log10(freqMax);
            double freqStart = Math.Pow(10, logFreqMin + (logFreqMax - logFreqMin) * x / width);
            double freqEnd = Math.Pow(10, logFreqMin + (logFreqMax - logFreqMin) * (x + 1) / width);

            double sum = 0;
            int count = 0;
            for (int b = 0; b < magnitudes.Length; b++)
            {
                double binFreq = b * freqPerBin;
                if (binFreq >= freqStart && binFreq < freqEnd)
                {
                    sum += magnitudes[b];
                    count++;
                }
            }

            double avg = count > 0 ? sum / count : 0;
            double db = 20 * Math.Log10(avg + 1e-10);

            double minDb = -85;
            double maxDb = -25;

            int barHeight = (int)(((db - minDb) / (maxDb - minDb)) * height);
            barHeight = Math.Clamp(barHeight, 0, height);

            int prevHeight = previousHeights[x];
            previousHeights[x] = barHeight;

            if (barHeight > prevHeight)
            {
                for (int y = prevHeight; y < barHeight; y++)
                {
                    Console.SetCursorPosition(x, height - y);
                    Console.Write('┆');
                }
            }
            else if (barHeight < prevHeight)
            {
                for (int y = barHeight; y < prevHeight; y++)
                {
                    Console.SetCursorPosition(x, height - y);
                    Console.Write(' ');
                }
            }
        }
    }
}
