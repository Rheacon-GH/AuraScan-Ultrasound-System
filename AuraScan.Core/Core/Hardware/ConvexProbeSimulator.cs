using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// High-fidelity simulator for the Philips C5-1 convex (curvilinear) probe.
    /// Generates realistic RF data with tissue-mimicking phantom patterns including
    /// point targets, cyst regions, speckle noise, and Doppler flow patterns.
    /// </summary>
    public sealed class ConvexProbeSimulator : IUltrasoundProbe
    {
        private readonly Random _rng = new(42);
        private ScanParameters _parameters = new();
        private bool _acquiring;
        private long _frameCounter;
        private CancellationTokenSource? _acqCts;

        // Phantom structures
        private readonly List<PhantomTarget> _pointTargets;
        private readonly List<PhantomCyst> _cysts;
        private readonly List<PhantomVessel> _vessels;

        public ProbeConfiguration Configuration { get; }
        public bool IsConnected { get; private set; }
        public bool IsAcquiring => _acquiring;

        public event EventHandler<UltrasoundFrame>? FrameAcquired;
        public event EventHandler<string>? ErrorOccurred;

        public ConvexProbeSimulator()
        {
            Configuration = new ProbeConfiguration
            {
                ModelName = "C5-1",
                Manufacturer = "Philips",
                ProbeType = ProbeType.Convex,
                ElementCount = 128,
                CenterFrequencyHz = 3.0e6,
                BandwidthLowHz = 1.0e6,
                BandwidthHighHz = 5.0e6,
                ElementPitchM = 0.000480,
                ConvexRadiusM = 0.060,
                FieldOfViewDegrees = 70.0,
                MaxDepthM = 0.30,
                SpeedOfSoundMps = 1540.0,
                SamplingFrequencyHz = 40.0e6,
                SamplesPerLine = 4096,
                ScanlinesPerFrame = 256,
                FocalZones = 2
            };

            // Initialize phantom structures for realistic simulation
            _pointTargets = GeneratePointTargets();
            _cysts = GenerateCysts();
            _vessels = GenerateVessels();
        }

        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(CancellationToken ct = default)
        {
            _acquiring = false;
            IsConnected = false;
            try { _acqCts?.Cancel(); } catch (ObjectDisposedException) { }
            return Task.CompletedTask;
        }

        public Task StartAcquisitionAsync(ScanParameters parameters, CancellationToken ct = default)
        {
            _parameters = parameters;
            _acquiring = true;
            _frameCounter = 0;
            _acqCts?.Dispose();
            _acqCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            return Task.CompletedTask;
        }

        public Task StopAcquisitionAsync(CancellationToken ct = default)
        {
            _acquiring = false;
            try { _acqCts?.Cancel(); } catch (ObjectDisposedException) { }
            return Task.CompletedTask;
        }

        public Task UpdateParametersAsync(ScanParameters parameters, CancellationToken ct = default)
        {
            _parameters = parameters;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Generate one frame of simulated RF data for the convex probe geometry.
        /// </summary>
        public Task<double[][]> AcquireFrameAsync(CancellationToken ct = default)
        {
            int scanlines = Configuration.ScanlinesPerFrame;
            int samplesPerLine = CalculateSamplesForDepth(_parameters.DepthM);
            double fovRad = _parameters.SectorAngleDeg * Math.PI / 180.0;
            double startAngle = -fovRad / 2.0;
            double angleStep = fovRad / (scanlines - 1);
            double fs = Configuration.SamplingFrequencyHz;
            double fc = _parameters.TransmitFrequencyHz;
            double c = Configuration.SpeedOfSoundMps;
            double convexR = Configuration.ConvexRadiusM;

            var rfData = new double[scanlines][];
            double time = _frameCounter / 30.0; // ~30 fps simulation time

            for (int line = 0; line < scanlines; line++)
            {
                rfData[line] = new double[samplesPerLine];
                double theta = startAngle + line * angleStep;

                for (int s = 0; s < samplesPerLine; s++)
                {
                    double t = s / fs;
                    double depth = c * t / 2.0;

                    // Convert polar (angle, depth) to Cartesian relative to convex center
                    double x = (convexR + depth) * Math.Sin(theta);
                    double z = (convexR + depth) * Math.Cos(theta) - convexR;

                    // Base tissue speckle (Rayleigh-distributed envelope with RF modulation)
                    double speckle = GenerateSpeckle(x, z, fc, t);

                    // Depth-dependent attenuation (~0.5 dB/cm/MHz)
                    double attenuationDb = 0.5 * (depth * 100.0) * (fc / 1.0e6);
                    double attenuation = Math.Pow(10.0, -attenuationDb / 20.0);

                    double signal = speckle * attenuation;

                    // Add point target reflections
                    foreach (var target in _pointTargets)
                    {
                        double dx = x - target.X;
                        double dz = z - target.Z;
                        double dist = Math.Sqrt(dx * dx + dz * dz);
                        if (dist < 0.002) // 2mm point spread
                        {
                            double psf = target.Amplitude * Math.Exp(-dist * dist / (2.0 * 0.0005 * 0.0005));
                            signal += psf * Math.Sin(2.0 * Math.PI * fc * t) * attenuation;
                        }
                    }

                    // Subtract signal in cyst regions (anechoic)
                    foreach (var cyst in _cysts)
                    {
                        double dx = x - cyst.X;
                        double dz = z - cyst.Z;
                        double dist = Math.Sqrt(dx * dx + dz * dz);
                        if (dist < cyst.Radius)
                        {
                            double suppression = 1.0 - Math.Exp(-Math.Pow((dist - cyst.Radius) / (cyst.Radius * 0.1), 2));
                            signal *= suppression * 0.05;
                        }
                    }

                    // Add vessel flow echoes (for Doppler modes)
                    foreach (var vessel in _vessels)
                    {
                        double dx = x - vessel.CenterX;
                        double dz = z - vessel.CenterZ;
                        double distFromAxis = Math.Abs(
                            dx * Math.Sin(vessel.OrientationRad) -
                            dz * Math.Cos(vessel.OrientationRad));

                        if (distFromAxis < vessel.Radius)
                        {
                            // Parabolic flow profile
                            double normalizedR = distFromAxis / vessel.Radius;
                            double flowVelocity = vessel.PeakVelocity * (1.0 - normalizedR * normalizedR);

                            // Pulsatile flow with cardiac cycle
                            double cardiacPhase = 2.0 * Math.PI * 1.2 * time; // ~72 bpm
                            double pulsatility = 0.6 + 0.4 * Math.Sin(cardiacPhase);
                            flowVelocity *= pulsatility;

                            // Doppler frequency shift
                            double dopplerShift = 2.0 * fc * flowVelocity *
                                Math.Cos(vessel.OrientationRad) / c;

                            double flowSignal = vessel.ScatterAmplitude *
                                Math.Sin(2.0 * Math.PI * (fc + dopplerShift) * t) *
                                attenuation;

                            signal += flowSignal;
                        }
                    }

                    rfData[line][s] = signal;
                }
            }

            _frameCounter++;
            return Task.FromResult(rfData);
        }

        /// <summary>
        /// Generate a Doppler ensemble (multiple transmit-receive events at the same scanline positions).
        /// </summary>
        public async Task<double[][][]> AcquireDopplerEnsembleAsync(int ensembleLength, CancellationToken ct = default)
        {
            var ensemble = new double[ensembleLength][][];
            double pri = 1.0 / _parameters.DopplerPrfHz;

            for (int e = 0; e < ensembleLength; e++)
            {
                ct.ThrowIfCancellationRequested();
                ensemble[e] = await AcquireFrameAsync(ct);
                // Simulate inter-pulse interval timing
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(pri, 0.001)), ct);
            }

            return ensemble;
        }

        private int CalculateSamplesForDepth(double depthM)
        {
            double roundTripTime = 2.0 * depthM / Configuration.SpeedOfSoundMps;
            int samples = (int)(roundTripTime * Configuration.SamplingFrequencyHz);
            return Math.Min(samples, Configuration.SamplesPerLine);
        }

        private double GenerateSpeckle(double x, double z, double fc, double t)
        {
            // Deterministic speckle based on spatial position (coherent scattering model)
            double spatialHash1 = Math.Sin(x * 15000.0) * Math.Cos(z * 12000.0);
            double spatialHash2 = Math.Sin(x * 23000.0 + 1.7) * Math.Cos(z * 19000.0 + 2.3);
            double spatialHash3 = Math.Sin(x * 37000.0 + 3.1) * Math.Cos(z * 31000.0 + 0.7);

            // Rayleigh-like amplitude distribution from multiple scatterers
            double envelope = Math.Sqrt(spatialHash1 * spatialHash1 + spatialHash2 * spatialHash2) * 0.3;

            // RF modulation at center frequency
            double rf = envelope * Math.Sin(2.0 * Math.PI * fc * t + spatialHash3 * Math.PI);

            // Add thermal noise
            double noise = (_rng.NextDouble() - 0.5) * 0.02;

            return rf + noise;
        }

        private List<PhantomTarget> GeneratePointTargets()
        {
            // Point targets at known positions for resolution assessment
            return
            [
                new() { X = 0.00, Z = 0.03, Amplitude = 5.0 },
                new() { X = 0.00, Z = 0.06, Amplitude = 5.0 },
                new() { X = 0.00, Z = 0.09, Amplitude = 5.0 },
                new() { X = 0.00, Z = 0.12, Amplitude = 5.0 },
                new() { X = -0.02, Z = 0.05, Amplitude = 4.0 },
                new() { X = 0.02, Z = 0.05, Amplitude = 4.0 },
                new() { X = -0.01, Z = 0.08, Amplitude = 3.0 },
                new() { X = 0.01, Z = 0.08, Amplitude = 3.0 },
            ];
        }

        private List<PhantomCyst> GenerateCysts()
        {
            // Anechoic cyst phantoms for contrast assessment
            return
            [
                new() { X = -0.03, Z = 0.07, Radius = 0.010 },
                new() { X = 0.025, Z = 0.10, Radius = 0.008 },
                new() { X = 0.00, Z = 0.14, Radius = 0.012 },
            ];
        }

        private List<PhantomVessel> GenerateVessels()
        {
            // Simulated vessels with flow for Doppler testing
            return
            [
                // Main vessel (~carotid-like) with moderate flow
                new()
                {
                    CenterX = 0.015, CenterZ = 0.05,
                    Radius = 0.004, OrientationRad = 0.3,
                    PeakVelocity = 0.6, ScatterAmplitude = 0.15
                },
                // Deeper vessel with slower flow
                new()
                {
                    CenterX = -0.01, CenterZ = 0.09,
                    Radius = 0.003, OrientationRad = -0.2,
                    PeakVelocity = 0.3, ScatterAmplitude = 0.10
                },
                // Small vessel
                new()
                {
                    CenterX = 0.005, CenterZ = 0.12,
                    Radius = 0.002, OrientationRad = 0.1,
                    PeakVelocity = 0.2, ScatterAmplitude = 0.08
                },
            ];
        }

        public void Dispose()
        {
            _acquiring = false;
            IsConnected = false;
            try { _acqCts?.Cancel(); } catch (ObjectDisposedException) { }
            try { _acqCts?.Dispose(); } catch (ObjectDisposedException) { }
            _acqCts = null;
        }

        // --- Phantom structure definitions ---

        private sealed class PhantomTarget
        {
            public double X { get; init; }
            public double Z { get; init; }
            public double Amplitude { get; init; }
        }

        private sealed class PhantomCyst
        {
            public double X { get; init; }
            public double Z { get; init; }
            public double Radius { get; init; }
        }

        private sealed class PhantomVessel
        {
            public double CenterX { get; init; }
            public double CenterZ { get; init; }
            public double Radius { get; init; }
            public double OrientationRad { get; init; }
            public double PeakVelocity { get; init; }
            public double ScatterAmplitude { get; init; }
        }
    }
}
