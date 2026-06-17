using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.SignalProcessing
{
    /// <summary>
    /// Delay-and-sum beamformer for convex (curvilinear) array geometry.
    /// Computes receive focusing delays based on curved element positions
    /// and applies dynamic receive apodization.
    /// </summary>
    public sealed class BeamformerEngine
    {
        private readonly ProbeConfiguration _config;
        private double[][]? _delayTable; // [scanline][element] in samples
        private double[][]? _apodizationTable; // [scanline][element]

        public BeamformerEngine(ProbeConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Precompute delay and apodization tables for current scan parameters.
        /// Must be called when depth, focus, or sector angle changes.
        /// </summary>
        public void Initialize(ScanParameters parameters)
        {
            int scanlines = _config.ScanlinesPerFrame;
            int elements = _config.ElementCount;
            double fovRad = parameters.SectorAngleDeg * Math.PI / 180.0;
            double startAngle = -fovRad / 2.0;
            double angleStep = fovRad / (scanlines - 1);
            double c = _config.SpeedOfSoundMps;
            double fs = _config.SamplingFrequencyHz;
            double convexR = _config.ConvexRadiusM;
            double elemAngularSpan = fovRad / elements;

            _delayTable = new double[scanlines][];
            _apodizationTable = new double[scanlines][];

            for (int line = 0; line < scanlines; line++)
            {
                _delayTable[line] = new double[elements];
                _apodizationTable[line] = new double[elements];

                double thetaLine = startAngle + line * angleStep;

                for (int elem = 0; elem < elements; elem++)
                {
                    // Element position on the convex arc
                    double thetaElem = startAngle + (elem + 0.5) * elemAngularSpan;
                    double elemX = convexR * Math.Sin(thetaElem);
                    double elemZ = convexR * Math.Cos(thetaElem);

                    // Focus point along the scanline direction at focal depth
                    double focalDepth = parameters.FocalDepthsM.Length > 0
                        ? parameters.FocalDepthsM[0]
                        : parameters.DepthM / 2.0;

                    double focusX = (convexR + focalDepth) * Math.Sin(thetaLine);
                    double focusZ = (convexR + focalDepth) * Math.Cos(thetaLine);

                    // Distance from element to focus point
                    double dx = focusX - elemX;
                    double dz = focusZ - elemZ;
                    double distance = Math.Sqrt(dx * dx + dz * dz);

                    // Reference distance (center element to focus)
                    double refDistance = focalDepth;

                    // Delay in samples (relative to reference)
                    double delaySeconds = (distance - refDistance) / c;
                    _delayTable[line][elem] = delaySeconds * fs;

                    // Hanning apodization based on angular distance
                    double angularDiff = Math.Abs(thetaElem - thetaLine);
                    double maxAngularAperture = fovRad / 4.0;
                    if (angularDiff < maxAngularAperture)
                    {
                        double w = angularDiff / maxAngularAperture;
                        _apodizationTable[line][elem] = 0.5 * (1.0 + Math.Cos(Math.PI * w));
                    }
                    else
                    {
                        _apodizationTable[line][elem] = 0.0;
                    }
                }
            }
        }

        /// <summary>
        /// Apply delay-and-sum beamforming to raw RF channel data.
        /// Input: rfData[scanline][sample] (pre-beamformed from simulator).
        /// For real hardware, input would be rfChannelData[element][sample].
        /// </summary>
        public double[][] Beamform(double[][] rfData, ScanParameters parameters)
        {
            if (_delayTable == null || _apodizationTable == null)
                Initialize(parameters);

            // Capture stable local references so a concurrent Initialize call
            // cannot null-out the tables while Parallel.For is iterating.
            var delayTable = _delayTable!;
            var apodTable = _apodizationTable!;

            int scanlines = Math.Min(rfData.Length, _config.ScanlinesPerFrame);
            int samplesPerLine = rfData[0].Length;
            var result = new double[scanlines][];

            // Clamp scanlines to the table dimensions in case of a mismatch
            scanlines = Math.Min(scanlines, delayTable.Length);

            // For simulator data (already spatially indexed by scanline),
            // apply dynamic receive focusing via interpolated delay-and-sum
            Parallel.For(0, scanlines, line =>
            {
                result[line] = new double[samplesPerLine];
                var lineDelays = delayTable[line];
                var lineApod = apodTable[line];

                for (int s = 0; s < samplesPerLine; s++)
                {
                    double sum = 0.0;
                    double weightSum = 0.0;

                    // Sum contributions from neighboring scanlines (synthetic aperture)
                    int apertureHalf = Math.Min(8, scanlines / 4);
                    for (int offset = -apertureHalf; offset <= apertureHalf; offset++)
                    {
                        int neighborLine = line + offset;
                        if (neighborLine < 0 || neighborLine >= scanlines)
                            continue;

                        // Apply delay from precomputed table
                        int elemIndex = neighborLine % _config.ElementCount;
                        if (elemIndex >= lineDelays.Length)
                            continue;

                        double delaySamples = lineDelays[elemIndex];
                        int delayedSample = s + (int)Math.Round(delaySamples * offset / apertureHalf);

                        if (delayedSample < 0 || delayedSample >= samplesPerLine)
                            continue;

                        double weight = lineApod[elemIndex];
                        sum += rfData[neighborLine][delayedSample] * weight;
                        weightSum += weight;
                    }

                    result[line][s] = weightSum > 0 ? sum / weightSum : 0.0;
                }
            });

            return result;
        }
    }
}
