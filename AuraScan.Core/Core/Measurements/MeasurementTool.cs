using System.Windows;
using System.Windows.Media;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Measurements
{
    /// <summary>
    /// Clinical measurement tools for ultrasound imaging.
    /// Provides distance, area (ellipse and freehand trace), volume (prolate ellipsoid),
    /// and velocity measurements with proper physical unit calibration.
    /// </summary>
    public sealed class MeasurementTool
    {
        private readonly List<MeasurementResult> _measurements = [];
        private List<Point>? _activeTracePoints;

        /// <summary>All completed measurements.</summary>
        public IReadOnlyList<MeasurementResult> Measurements => _measurements;

        /// <summary>Active trace points during freehand measurement.</summary>
        public IReadOnlyList<Point>? ActiveTracePoints => _activeTracePoints;

        /// <summary>Current measurement mode.</summary>
        public MeasurementType? ActiveMeasurementType { get; set; }

        /// <summary>First caliper point for active measurement.</summary>
        public Point? FirstPoint { get; set; }

        /// <summary>Second caliper point for active measurement.</summary>
        public Point? SecondPoint { get; set; }

        /// <summary>
        /// Calculate distance between two points in the image.
        /// Returns result in centimeters based on pixel-to-physical calibration.
        /// </summary>
        public MeasurementResult MeasureDistance(Point p1, Point p2,
            double pixelsPerCmX, double pixelsPerCmY)
        {
            double dx = (p2.X - p1.X) / pixelsPerCmX;
            double dy = (p2.Y - p1.Y) / pixelsPerCmY;
            double distanceCm = Math.Sqrt(dx * dx + dy * dy);

            var result = new MeasurementResult
            {
                Type = MeasurementType.Distance,
                Value = distanceCm,
                Unit = "cm",
                StartPoint = (p1.X, p1.Y),
                EndPoint = (p2.X, p2.Y),
                Label = $"D: {distanceCm:F2} cm"
            };

            _measurements.Add(result);
            return result;
        }

        /// <summary>
        /// Calculate area of an ellipse defined by two endpoints (major axis).
        /// The minor axis is assumed perpendicular (can be specified).
        /// Returns result in cm².
        /// </summary>
        public MeasurementResult MeasureEllipseArea(Point center, double majorAxisPx,
            double minorAxisPx, double pixelsPerCmX, double pixelsPerCmY)
        {
            double majorCm = majorAxisPx / pixelsPerCmX;
            double minorCm = minorAxisPx / pixelsPerCmY;
            double areaCm2 = Math.PI * (majorCm / 2.0) * (minorCm / 2.0);

            var result = new MeasurementResult
            {
                Type = MeasurementType.Area,
                Value = areaCm2,
                Unit = "cm²",
                StartPoint = (center.X - majorAxisPx / 2, center.Y),
                EndPoint = (center.X + majorAxisPx / 2, center.Y),
                Label = $"A: {areaCm2:F2} cm²"
            };

            _measurements.Add(result);
            return result;
        }

        /// <summary>
        /// Calculate area from a freehand trace using the shoelace formula.
        /// </summary>
        public MeasurementResult MeasureTraceArea(List<Point> tracePoints,
            double pixelsPerCmX, double pixelsPerCmY)
        {
            if (tracePoints.Count < 3)
                return new MeasurementResult { Type = MeasurementType.Area, Value = 0, Unit = "cm²" };

            // Shoelace formula for polygon area
            double areaPx2 = 0;
            for (int i = 0; i < tracePoints.Count; i++)
            {
                var p1 = tracePoints[i];
                var p2 = tracePoints[(i + 1) % tracePoints.Count];
                areaPx2 += p1.X * p2.Y - p2.X * p1.Y;
            }
            areaPx2 = Math.Abs(areaPx2) / 2.0;

            double areaCm2 = areaPx2 / (pixelsPerCmX * pixelsPerCmY);

            // Circumference
            double circumferenceCm = 0;
            for (int i = 0; i < tracePoints.Count; i++)
            {
                var p1 = tracePoints[i];
                var p2 = tracePoints[(i + 1) % tracePoints.Count];
                double dx = (p2.X - p1.X) / pixelsPerCmX;
                double dy = (p2.Y - p1.Y) / pixelsPerCmY;
                circumferenceCm += Math.Sqrt(dx * dx + dy * dy);
            }

            var bounds = GetBounds(tracePoints);
            var result = new MeasurementResult
            {
                Type = MeasurementType.Area,
                Value = areaCm2,
                Unit = "cm²",
                StartPoint = (bounds.Left, bounds.Top),
                EndPoint = (bounds.Right, bounds.Bottom),
                Label = $"A: {areaCm2:F2} cm²  C: {circumferenceCm:F2} cm"
            };

            _measurements.Add(result);
            return result;
        }

        /// <summary>
        /// Calculate volume using the prolate ellipsoid method (3-axis measurement).
        /// V = π/6 × D1 × D2 × D3. Returns result in mL (cm³).
        /// </summary>
        public MeasurementResult MeasureVolume(double d1Cm, double d2Cm, double d3Cm)
        {
            double volumeMl = Math.PI / 6.0 * d1Cm * d2Cm * d3Cm;

            var result = new MeasurementResult
            {
                Type = MeasurementType.Volume,
                Value = volumeMl,
                Unit = "mL",
                Label = $"V: {volumeMl:F2} mL ({d1Cm:F1}×{d2Cm:F1}×{d3Cm:F1} cm)"
            };

            _measurements.Add(result);
            return result;
        }

        /// <summary>
        /// Measure velocity from Doppler spectral trace.
        /// Converts pixel position on spectrogram to velocity in cm/s.
        /// </summary>
        public MeasurementResult MeasureVelocity(double pixelY, int spectralHeight,
            double nyquistVelocityMps, double angleCorrectionDeg)
        {
            // Map pixel Y to velocity: top = +Nyquist, bottom = -Nyquist, center = 0
            double normalizedY = (spectralHeight / 2.0 - pixelY) / (spectralHeight / 2.0);
            double velocityMps = normalizedY * nyquistVelocityMps;

            // Apply angle correction: V_true = V_measured / cos(θ)
            double angleRad = angleCorrectionDeg * Math.PI / 180.0;
            double correctedVelocityMps = velocityMps / Math.Cos(angleRad);

            double velocityCmPerS = correctedVelocityMps * 100.0;

            var result = new MeasurementResult
            {
                Type = MeasurementType.Velocity,
                Value = velocityCmPerS,
                Unit = "cm/s",
                Label = $"V: {velocityCmPerS:F1} cm/s  (θ={angleCorrectionDeg:F0}°)"
            };

            _measurements.Add(result);
            return result;
        }

        /// <summary>
        /// Measure peak systolic and end diastolic velocities for RI and PI calculation.
        /// </summary>
        public (double RI, double PI, string Label) CalculateResistiveIndices(
            double peakSystolicCmS, double endDiastolicCmS)
        {
            // Resistive Index: RI = (PSV - EDV) / PSV
            double ri = Math.Abs(peakSystolicCmS) > 0
                ? (Math.Abs(peakSystolicCmS) - Math.Abs(endDiastolicCmS)) / Math.Abs(peakSystolicCmS)
                : 0;

            // Pulsatility Index: PI = (PSV - EDV) / mean velocity
            // Simplified: use (PSV + EDV) / 2 as mean estimate
            double meanVelocity = (Math.Abs(peakSystolicCmS) + Math.Abs(endDiastolicCmS)) / 2.0;
            double pi = meanVelocity > 0
                ? (Math.Abs(peakSystolicCmS) - Math.Abs(endDiastolicCmS)) / meanVelocity
                : 0;

            string label = $"PSV: {Math.Abs(peakSystolicCmS):F1} cm/s  EDV: {Math.Abs(endDiastolicCmS):F1} cm/s\n" +
                          $"RI: {ri:F2}  PI: {pi:F2}";

            return (ri, pi, label);
        }

        /// <summary>
        /// Start a freehand trace measurement.
        /// </summary>
        public void BeginTrace()
        {
            _activeTracePoints = [];
        }

        /// <summary>
        /// Add a point to the active freehand trace.
        /// </summary>
        public void AddTracePoint(Point point)
        {
            _activeTracePoints?.Add(point);
        }

        /// <summary>
        /// Complete the active freehand trace and compute the area.
        /// </summary>
        public MeasurementResult? EndTrace(double pixelsPerCmX, double pixelsPerCmY)
        {
            if (_activeTracePoints == null || _activeTracePoints.Count < 3)
            {
                _activeTracePoints = null;
                return null;
            }

            var result = MeasureTraceArea(_activeTracePoints, pixelsPerCmX, pixelsPerCmY);
            _activeTracePoints = null;
            return result;
        }

        /// <summary>
        /// Clear all measurements.
        /// </summary>
        public void ClearAll()
        {
            _measurements.Clear();
            _activeTracePoints = null;
            ActiveMeasurementType = null;
            FirstPoint = null;
            SecondPoint = null;
        }

        /// <summary>
        /// Remove the last measurement.
        /// </summary>
        public void Undo()
        {
            if (_measurements.Count > 0)
                _measurements.RemoveAt(_measurements.Count - 1);
        }

        /// <summary>
        /// Get pixel-to-physical calibration factors from scan parameters and display size.
        /// </summary>
        public static (double PixelsPerCmX, double PixelsPerCmY) GetCalibration(
            ScanParameters scanParams, ProbeConfiguration probeConfig,
            int displayWidth, int displayHeight)
        {
            double depthCm = scanParams.DepthM * 100.0;
            double fovRad = scanParams.SectorAngleDeg * Math.PI / 180.0;
            double widthCm = 2.0 * (probeConfig.ConvexRadiusM + scanParams.DepthM) *
                            Math.Sin(fovRad / 2.0) * 100.0;

            double pxPerCmX = displayWidth / widthCm;
            double pxPerCmY = displayHeight / depthCm;

            return (pxPerCmX, pxPerCmY);
        }

        private static Rect GetBounds(List<Point> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
