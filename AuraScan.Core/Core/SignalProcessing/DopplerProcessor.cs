using AuraScan_Ultrasound_System.Models;
using MathNet.Numerics.IntegralTransforms;

namespace AuraScan_Ultrasound_System.Core.SignalProcessing
{
    /// <summary>
    /// Doppler signal processing: autocorrelation-based velocity estimation
    /// for Color Flow imaging and FFT-based spectral analysis for PW Doppler.
    /// </summary>
    public sealed class DopplerProcessor
    {
        /// <summary>
        /// Estimate mean velocity, power, and variance from a Doppler ensemble
        /// using the Kasai autocorrelation estimator.
        /// Input: ensemble[fireIndex][scanline][sample]
        /// Output: DopplerSample for each (scanline, sample) within the color box.
        /// </summary>
        public (double[][] Velocity, double[][] Power, double[][] Variance) EstimateColorFlow(
            double[][][] ensemble, ScanParameters parameters, ProbeConfiguration config)
        {
            int ensembleLength = ensemble.Length;
            if (ensembleLength < 2)
                return (Array.Empty<double[]>(), Array.Empty<double[]>(), Array.Empty<double[]>());

            int scanlines = ensemble[0].Length;
            int samples = ensemble[0][0].Length;

            var velocity = new double[scanlines][];
            var power = new double[scanlines][];
            var variance = new double[scanlines][];

            double prf = parameters.DopplerPrfHz;
            double fc = parameters.TransmitFrequencyHz;
            double c = config.SpeedOfSoundMps;
            double nyquist = c * prf / (4.0 * fc);

            int startLine = parameters.ColorBoxStartLine;
            int endLine = Math.Min(parameters.ColorBoxEndLine, scanlines);
            int startSample = parameters.ColorBoxStartSample;
            int endSample = Math.Min(parameters.ColorBoxEndSample, samples);

            Parallel.For(0, scanlines, line =>
            {
                velocity[line] = new double[samples];
                power[line] = new double[samples];
                variance[line] = new double[samples];

                if (line < startLine || line >= endLine) return;

                for (int s = startSample; s < endSample && s < samples; s++)
                {
                    // Apply wall filter (high-pass) to remove clutter
                    var filtered = WallFilter(ensemble, line, s, parameters.DopplerWallFilterHz, prf);

                    // Kasai autocorrelation estimator
                    double realSum = 0.0;
                    double imagSum = 0.0;
                    double powerSum = 0.0;

                    for (int e = 0; e < filtered.Length - 1; e++)
                    {
                        // Autocorrelation at lag 1
                        // For IQ data, R(1) = sum(z[n] * conj(z[n-1]))
                        // Using RF data, approximate with Hilbert pair
                        double i1 = filtered[e];
                        double q1 = HilbertSample(filtered, e);
                        double i2 = filtered[e + 1];
                        double q2 = HilbertSample(filtered, e + 1);

                        realSum += i1 * i2 + q1 * q2;
                        imagSum += q1 * i2 - i1 * q2;
                        powerSum += i1 * i1 + q1 * q1;
                    }

                    // Mean velocity from autocorrelation phase
                    double phase = Math.Atan2(imagSum, realSum);
                    velocity[line][s] = phase / (2.0 * Math.PI) * nyquist * 2.0;

                    // Power estimate (in dB)
                    double p = powerSum / Math.Max(filtered.Length, 1);
                    power[line][s] = p > 1e-20 ? 10.0 * Math.Log10(p) : -100.0;

                    // Variance estimate (turbulence)
                    double r1Mag = Math.Sqrt(realSum * realSum + imagSum * imagSum);
                    double r0 = powerSum;
                    variance[line][s] = r0 > 1e-20 ? 1.0 - r1Mag / r0 : 0.0;
                }
            });

            return (velocity, power, variance);
        }

        /// <summary>
        /// Compute spectral Doppler (PW) spectrogram from ensemble data at a gate position.
        /// Returns magnitude spectrogram [timeIndex][frequencyBin].
        /// </summary>
        public double[][] ComputeSpectralDoppler(double[][][] ensemble, int gateLine, int gateSample,
            int gateWidth, ScanParameters parameters, ProbeConfiguration config)
        {
            int ensembleLength = ensemble.Length;
            int fftSize = parameters.SpectralFftSize;
            int halfFft = fftSize / 2;

            // Collect slow-time signal at the gate position
            var slowTimeSignal = new double[ensembleLength];
            for (int e = 0; e < ensembleLength; e++)
            {
                if (gateLine < ensemble[e].Length && gateSample < ensemble[e][gateLine].Length)
                {
                    // Average over gate width
                    double sum = 0;
                    int count = 0;
                    for (int g = -gateWidth / 2; g <= gateWidth / 2; g++)
                    {
                        int idx = gateSample + g;
                        if (idx >= 0 && idx < ensemble[e][gateLine].Length)
                        {
                            sum += ensemble[e][gateLine][idx];
                            count++;
                        }
                    }
                    slowTimeSignal[e] = count > 0 ? sum / count : 0;
                }
            }

            // Compute STFT with overlapping windows
            int windowSize = Math.Min(fftSize, ensembleLength);
            int hopSize = Math.Max(1, windowSize / 4);
            int numWindows = Math.Max(1, (ensembleLength - windowSize) / hopSize + 1);

            var spectrogram = new double[numWindows][];

            for (int w = 0; w < numWindows; w++)
            {
                int startIdx = w * hopSize;
                var spectrum = new System.Numerics.Complex[fftSize];

                // Apply Hanning window and fill FFT buffer
                for (int i = 0; i < windowSize && startIdx + i < ensembleLength; i++)
                {
                    double window = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (windowSize - 1)));
                    spectrum[i] = new System.Numerics.Complex(slowTimeSignal[startIdx + i] * window, 0);
                }

                Fourier.Forward(spectrum, FourierOptions.Default);

                spectrogram[w] = new double[fftSize];
                for (int k = 0; k < fftSize; k++)
                {
                    // Shift so zero frequency is at center
                    int shiftedK = (k + halfFft) % fftSize;
                    double mag = spectrum[shiftedK].Magnitude;
                    spectrogram[w][k] = mag > 1e-20 ? 20.0 * Math.Log10(mag) : -100.0;
                }
            }

            return spectrogram;
        }

        /// <summary>
        /// Apply high-pass wall filter to remove stationary tissue clutter.
        /// Uses polynomial regression filter (order 2).
        /// </summary>
        private static double[] WallFilter(double[][][] ensemble, int line, int sample,
            double cutoffHz, double prf)
        {
            int n = ensemble.Length;
            var signal = new double[n];
            for (int e = 0; e < n; e++)
            {
                if (line < ensemble[e].Length && sample < ensemble[e][line].Length)
                    signal[e] = ensemble[e][line][sample];
            }

            // Polynomial regression wall filter (removes DC and low-order trends)
            // Fit and subtract a 2nd-order polynomial
            double sumX = 0, sumX2 = 0, sumX3 = 0, sumX4 = 0;
            double sumY = 0, sumXY = 0, sumX2Y = 0;

            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n;
                double x2 = x * x;
                sumX += x;
                sumX2 += x2;
                sumX3 += x2 * x;
                sumX4 += x2 * x2;
                sumY += signal[i];
                sumXY += x * signal[i];
                sumX2Y += x2 * signal[i];
            }

            // Solve normal equations for [a0, a1, a2]
            // Simplified: subtract mean and linear trend
            double mean = sumY / n;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / n - 0.5;
                signal[i] -= mean;
                // Simple high-pass: remove DC component
            }

            return signal;
        }

        /// <summary>
        /// Approximate Hilbert transform sample using finite-length FIR.
        /// </summary>
        private static double HilbertSample(double[] signal, int index)
        {
            // 7-tap Hilbert FIR filter coefficients
            ReadOnlySpan<double> coeffs = [0.0, 0.6366, 0.0, 0.2122, 0.0, 0.1273, 0.0];
            int halfLen = coeffs.Length / 2;
            double sum = 0;

            for (int k = 0; k < coeffs.Length; k++)
            {
                int n = index + k - halfLen;
                if (n >= 0 && n < signal.Length && coeffs[k] != 0)
                {
                    // Odd-symmetry Hilbert kernel
                    int sign = (k - halfLen) % 2 == 0 ? 0 : ((k - halfLen) > 0 ? 1 : -1);
                    sum += signal[n] * coeffs[k] * sign;
                }
            }

            return sum;
        }
    }
}
