using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.SignalProcessing
{
    /// <summary>
    /// Scan converter for convex (curvilinear) array geometry.
    /// Transforms polar-coordinate scanline data (angle × depth) to
    /// Cartesian pixel coordinates for sector/fan-shaped display.
    /// </summary>
    public sealed class ScanConverter
    {
        private int[][]? _scanlineMap;   // [y][x] -> source scanline index
        private int[][]? _sampleMap;     // [y][x] -> source sample index
        private double[][]? _weightMap;  // [y][x] -> bilinear interpolation weight
        private int _outputWidth;
        private int _outputHeight;

        /// <summary>
        /// Precompute the coordinate mapping tables for the given output size.
        /// Must be called when depth, sector angle, or display size changes.
        /// </summary>
        public void Initialize(ProbeConfiguration config, ScanParameters parameters,
            int outputWidth, int outputHeight)
        {
            _outputWidth = outputWidth;
            _outputHeight = outputHeight;
            _scanlineMap = new int[outputHeight][];
            _sampleMap = new int[outputHeight][];
            _weightMap = new double[outputHeight][];

            double fovRad = parameters.SectorAngleDeg * Math.PI / 180.0;
            double startAngle = -fovRad / 2.0;
            double convexR = config.ConvexRadiusM;
            double maxDepth = parameters.DepthM;
            int scanlines = config.ScanlinesPerFrame;
            int samplesPerLine = (int)(2.0 * maxDepth / config.SpeedOfSoundMps * config.SamplingFrequencyHz);
            samplesPerLine = Math.Min(samplesPerLine, config.SamplesPerLine);

            // Compute the display geometry
            // The sector fan origin is at (outputWidth/2, 0) - top center
            // Radial depth maps from convexR to convexR + maxDepth
            double totalRadius = convexR + maxDepth;
            double sectorWidth = 2.0 * totalRadius * Math.Sin(fovRad / 2.0);
            double sectorHeight = totalRadius - convexR * Math.Cos(fovRad / 2.0);
            double pixelsPerMeter = Math.Min(outputWidth / sectorWidth, outputHeight / sectorHeight) * 0.95;

            double originX = outputWidth / 2.0;
            double originY = -convexR * pixelsPerMeter + outputHeight * 0.02;

            for (int y = 0; y < outputHeight; y++)
            {
                _scanlineMap[y] = new int[outputWidth];
                _sampleMap[y] = new int[outputWidth];
                _weightMap[y] = new double[outputWidth];

                for (int x = 0; x < outputWidth; x++)
                {
                    // Convert pixel to physical coordinates
                    double px = (x - originX) / pixelsPerMeter;
                    double pz = (y - originY) / pixelsPerMeter;

                    // Convert to polar relative to convex center (0, 0)
                    double radius = Math.Sqrt(px * px + pz * pz);
                    double angle = Math.Atan2(px, pz);

                    // Depth from probe surface
                    double depth = radius - convexR;

                    // Check if within sector
                    if (depth >= 0 && depth <= maxDepth &&
                        angle >= startAngle && angle <= startAngle + fovRad)
                    {
                        // Map to scanline index
                        double normalizedAngle = (angle - startAngle) / fovRad;
                        double scanlineF = normalizedAngle * (scanlines - 1);
                        int scanlineIdx = Math.Clamp((int)scanlineF, 0, scanlines - 1);

                        // Map to sample index
                        double normalizedDepth = depth / maxDepth;
                        double sampleF = normalizedDepth * (samplesPerLine - 1);
                        int sampleIdx = Math.Clamp((int)sampleF, 0, samplesPerLine - 1);

                        // Bilinear interpolation weight
                        double wx = scanlineF - (int)scanlineF;
                        double wy = sampleF - (int)sampleF;
                        double weight = (1.0 - wx) * (1.0 - wy);

                        _scanlineMap[y][x] = scanlineIdx;
                        _sampleMap[y][x] = sampleIdx;
                        _weightMap[y][x] = weight;
                    }
                    else
                    {
                        _scanlineMap[y][x] = -1;
                        _sampleMap[y][x] = -1;
                        _weightMap[y][x] = 0.0;
                    }
                }
            }
        }

        /// <summary>
        /// Convert polar scanline data to a Cartesian sector image.
        /// </summary>
        public byte[] Convert(byte[][] scanlineData, int outputWidth, int outputHeight)
        {
            if (_scanlineMap == null || _outputWidth != outputWidth || _outputHeight != outputHeight)
                return new byte[outputWidth * outputHeight];

            var image = new byte[outputWidth * outputHeight];
            int scanlines = scanlineData.Length;

            Parallel.For(0, outputHeight, y =>
            {
                for (int x = 0; x < outputWidth; x++)
                {
                    int lineIdx = _scanlineMap[y][x];
                    int sampleIdx = _sampleMap[y][x];

                    if (lineIdx >= 0 && lineIdx < scanlines &&
                        sampleIdx >= 0 && sampleIdx < scanlineData[lineIdx].Length)
                    {
                        // Bilinear interpolation with neighbor
                        double val = scanlineData[lineIdx][sampleIdx] * _weightMap[y][x];

                        int nextLine = Math.Min(lineIdx + 1, scanlines - 1);
                        int nextSample = Math.Min(sampleIdx + 1, scanlineData[nextLine].Length - 1);

                        val += scanlineData[nextLine][sampleIdx] * (1.0 - _weightMap[y][x]) * 0.5;
                        val += scanlineData[lineIdx][nextSample] * (1.0 - _weightMap[y][x]) * 0.5;

                        image[y * outputWidth + x] = (byte)Math.Clamp(val, 0, 255);
                    }
                }
            });

            return image;
        }

        /// <summary>
        /// Convert Doppler velocity data to a Cartesian image.
        /// Returns velocity values (not color-mapped) for overlay composition.
        /// </summary>
        public double[] ConvertDoppler(double[][] velocityData, int outputWidth, int outputHeight)
        {
            if (_scanlineMap == null)
                return new double[outputWidth * outputHeight];

            var image = new double[outputWidth * outputHeight];
            int scanlines = velocityData.Length;

            Parallel.For(0, outputHeight, y =>
            {
                for (int x = 0; x < outputWidth; x++)
                {
                    int lineIdx = _scanlineMap[y][x];
                    int sampleIdx = _sampleMap[y][x];

                    if (lineIdx >= 0 && lineIdx < scanlines &&
                        sampleIdx >= 0 && sampleIdx < velocityData[lineIdx].Length)
                    {
                        image[y * outputWidth + x] = velocityData[lineIdx][sampleIdx];
                    }
                }
            });

            return image;
        }

        /// <summary>
        /// Get the pixel coordinates for a given depth and angle.
        /// Used for measurement overlay positioning.
        /// </summary>
        public (int X, int Y) PhysicalToPixel(double depthM, double angleRad,
            ProbeConfiguration config, ScanParameters parameters)
        {
            if (_scanlineMap == null) return (0, 0);

            double fovRad = parameters.SectorAngleDeg * Math.PI / 180.0;
            double convexR = config.ConvexRadiusM;
            double totalRadius = convexR + parameters.DepthM;
            double sectorWidth = 2.0 * totalRadius * Math.Sin(fovRad / 2.0);
            double sectorHeight = totalRadius - convexR * Math.Cos(fovRad / 2.0);
            double pixelsPerMeter = Math.Min(_outputWidth / sectorWidth, _outputHeight / sectorHeight) * 0.95;

            double originX = _outputWidth / 2.0;
            double originY = -convexR * pixelsPerMeter + _outputHeight * 0.02;

            double radius = convexR + depthM;
            double px = radius * Math.Sin(angleRad) * pixelsPerMeter + originX;
            double py = radius * Math.Cos(angleRad) * pixelsPerMeter + originY;

            return ((int)px, (int)py);
        }
    }
}
