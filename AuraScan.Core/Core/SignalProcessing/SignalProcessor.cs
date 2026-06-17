using AuraScan_Ultrasound_System.Models;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace AuraScan_Ultrasound_System.Core.SignalProcessing
{
    /// <summary>
    /// Core signal processing operations for ultrasound RF data:
    /// Hilbert transform envelope detection, time-gain compensation,
    /// log compression, and filtering.
    /// </summary>
    public sealed class SignalProcessor
    {
        /// <summary>
        /// Extract the envelope of RF data using the Hilbert transform.
        /// Computes the analytic signal and returns its magnitude.
        /// </summary>
        public double[][] EnvelopeDetect(double[][] rfData)
        {
            int scanlines = rfData.Length;
            var envelope = new double[scanlines][];

            Parallel.For(0, scanlines, line =>
            {
                envelope[line] = HilbertEnvelope(rfData[line]);
            });

            return envelope;
        }

        /// <summary>
        /// Apply time-gain compensation to compensate for depth-dependent attenuation.
        /// </summary>
        public void ApplyTgc(double[][] data, double[] tgcCurve, double maxGainDb = 40.0)
        {
            if (data.Length == 0 || tgcCurve.Length == 0) return;

            int scanlines = data.Length;
            int samplesPerLine = data[0].Length;
            int zones = tgcCurve.Length;

            Parallel.For(0, scanlines, line =>
            {
                for (int s = 0; s < samplesPerLine; s++)
                {
                    // Interpolate TGC curve value for this depth
                    double normalizedDepth = (double)s / samplesPerLine;
                    double zonePos = normalizedDepth * (zones - 1);
                    int zoneIdx = Math.Min((int)zonePos, zones - 2);
                    double frac = zonePos - zoneIdx;
                    double tgcValue = tgcCurve[zoneIdx] * (1.0 - frac) + tgcCurve[zoneIdx + 1] * frac;

                    // Convert TGC slider (0-1) to gain in dB
                    double gainDb = tgcValue * maxGainDb;
                    double gainLinear = Math.Pow(10.0, gainDb / 20.0);

                    data[line][s] *= gainLinear;
                }
            });
        }

        /// <summary>
        /// Apply log compression to map the wide dynamic range to display range.
        /// </summary>
        public byte[][] LogCompress(double[][] envelopeData, double dynamicRangeDb)
        {
            int scanlines = envelopeData.Length;
            if (scanlines == 0) return [];

            int samplesPerLine = envelopeData[0].Length;

            // Find global maximum for normalization
            double maxVal = 0.0;
            for (int line = 0; line < scanlines; line++)
            {
                for (int s = 0; s < samplesPerLine; s++)
                {
                    double val = Math.Abs(envelopeData[line][s]);
                    if (val > maxVal) maxVal = val;
                }
            }

            if (maxVal <= 0.0) maxVal = 1.0;
            double minDb = -dynamicRangeDb;

            var compressed = new byte[scanlines][];

            Parallel.For(0, scanlines, line =>
            {
                compressed[line] = new byte[samplesPerLine];
                for (int s = 0; s < samplesPerLine; s++)
                {
                    double val = Math.Abs(envelopeData[line][s]);
                    double normalized = val / maxVal;

                    // Log compression
                    double dB;
                    if (normalized > 1e-10)
                        dB = 20.0 * Math.Log10(normalized);
                    else
                        dB = minDb;

                    // Map dB range to 0-255
                    double display = (dB - minDb) / (-minDb) * 255.0;
                    compressed[line][s] = (byte)Math.Clamp(display, 0, 255);
                }
            });

            return compressed;
        }

        /// <summary>
        /// Apply overall gain adjustment in dB.
        /// </summary>
        public void ApplyGain(double[][] data, double gainDb)
        {
            double gainLinear = Math.Pow(10.0, gainDb / 20.0);
            int scanlines = data.Length;

            Parallel.For(0, scanlines, line =>
            {
                for (int s = 0; s < data[line].Length; s++)
                    data[line][s] *= gainLinear;
            });
        }

        /// <summary>
        /// Apply frame persistence (temporal averaging) using exponential moving average.
        /// </summary>
        public byte[][] ApplyPersistence(byte[][] currentFrame, byte[][]? previousFrame, int persistence)
        {
            if (previousFrame == null || persistence == 0)
                return currentFrame;

            // Persistence weight: higher = more averaging
            double alpha = 1.0 / (1.0 + persistence);
            int scanlines = currentFrame.Length;
            int samples = currentFrame[0].Length;
            var result = new byte[scanlines][];

            Parallel.For(0, scanlines, line =>
            {
                result[line] = new byte[samples];
                for (int s = 0; s < samples; s++)
                {
                    int prevLine = Math.Min(line, previousFrame.Length - 1);
                    int prevSample = Math.Min(s, previousFrame[prevLine].Length - 1);
                    double blended = alpha * currentFrame[line][s] +
                                     (1.0 - alpha) * previousFrame[prevLine][prevSample];
                    result[line][s] = (byte)Math.Clamp(blended, 0, 255);
                }
            });

            return result;
        }

        /// <summary>
        /// Bandpass filter RF data around the transmit frequency.
        /// </summary>
        public void BandpassFilter(double[][] rfData, double centerHz, double bandwidthHz, double samplingHz)
        {
            int scanlines = rfData.Length;

            Parallel.For(0, scanlines, line =>
            {
                int n = rfData[line].Length;
                int fftSize = NextPowerOf2(n);
                var spectrum = new System.Numerics.Complex[fftSize];

                for (int i = 0; i < n; i++)
                    spectrum[i] = new System.Numerics.Complex(rfData[line][i], 0);

                Fourier.Forward(spectrum, FourierOptions.Default);

                double lowCutoff = (centerHz - bandwidthHz / 2.0) / samplingHz * fftSize;
                double highCutoff = (centerHz + bandwidthHz / 2.0) / samplingHz * fftSize;

                for (int k = 0; k < fftSize; k++)
                {
                    double freq = k < fftSize / 2 ? k : k - fftSize;
                    double absFreq = Math.Abs(freq);
                    if (absFreq < lowCutoff || absFreq > highCutoff)
                        spectrum[k] = System.Numerics.Complex.Zero;
                }

                Fourier.Inverse(spectrum, FourierOptions.Default);

                for (int i = 0; i < n; i++)
                    rfData[line][i] = spectrum[i].Real;
            });
        }

        private static double[] HilbertEnvelope(double[] signal)
        {
            int n = signal.Length;
            int fftSize = NextPowerOf2(n);
            var spectrum = new System.Numerics.Complex[fftSize];

            for (int i = 0; i < n; i++)
                spectrum[i] = new System.Numerics.Complex(signal[i], 0);

            Fourier.Forward(spectrum, FourierOptions.Default);

            // Create analytic signal: zero negative frequencies, double positive
            spectrum[0] = spectrum[0]; // DC unchanged
            for (int k = 1; k < fftSize / 2; k++)
                spectrum[k] *= 2.0;
            for (int k = fftSize / 2 + 1; k < fftSize; k++)
                spectrum[k] = System.Numerics.Complex.Zero;

            Fourier.Inverse(spectrum, FourierOptions.Default);

            var envelope = new double[n];
            for (int i = 0; i < n; i++)
                envelope[i] = spectrum[i].Magnitude;

            return envelope;
        }

        private static int NextPowerOf2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }
    }
}
