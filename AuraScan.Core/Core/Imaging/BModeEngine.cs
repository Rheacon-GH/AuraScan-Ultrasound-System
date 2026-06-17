using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraScan_Ultrasound_System.Core.SignalProcessing;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Imaging
{
    /// <summary>
    /// B-Mode (Brightness Mode) imaging engine.
    /// Produces real-time 2D grayscale sector images from convex probe RF data.
    /// Pipeline: RF → Beamform → Envelope → TGC → Log Compress → Scan Convert → Render.
    /// </summary>
    public sealed class BModeEngine : IImagingEngine
    {
        private readonly BeamformerEngine _beamformer;
        private readonly SignalProcessor _signalProcessor;
        private readonly ScanConverter _scanConverter;

        private ProbeConfiguration _probeConfig = new();
        private int _displayWidth;
        private int _displayHeight;
        private byte[][]? _previousFrame;
        private long _frameCount;
        private readonly Stopwatch _fpsTimer = new();
        private double _frameRateHz;

        public ImagingMode Mode => ImagingMode.BMode;
        public bool IsRunning { get; private set; }
        public double FrameRateHz => _frameRateHz;

        public event EventHandler<UltrasoundFrame>? FrameReady;

        public BModeEngine()
        {
            _beamformer = new BeamformerEngine(new ProbeConfiguration());
            _signalProcessor = new SignalProcessor();
            _scanConverter = new ScanConverter();
        }

        public void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams,
            int displayWidth, int displayHeight)
        {
            _probeConfig = probeConfig;
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;

            _beamformer.Initialize(scanParams);
            _scanConverter.Initialize(probeConfig, scanParams, displayWidth, displayHeight);
            _previousFrame = null;
            _frameCount = 0;
            _fpsTimer.Restart();
        }

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        public UltrasoundFrame ProcessFrame(double[][] rfData, ScanParameters scanParams)
        {
            // 1. Beamform (delay-and-sum for convex geometry)
            var beamformed = _beamformer.Beamform(rfData, scanParams);

            // 2. Envelope detection (Hilbert transform)
            var envelope = _signalProcessor.EnvelopeDetect(beamformed);

            // 3. Time-Gain Compensation
            _signalProcessor.ApplyTgc(envelope, scanParams.TgcCurve);

            // 4. Overall gain
            _signalProcessor.ApplyGain(envelope, scanParams.GainDb - 50.0);

            // 5. Log compression
            var compressed = _signalProcessor.LogCompress(envelope, scanParams.DynamicRangeDb);

            // 6. Frame persistence (temporal smoothing)
            compressed = _signalProcessor.ApplyPersistence(compressed, _previousFrame, scanParams.Persistence);
            _previousFrame = compressed;

            // 7. Scan conversion (polar to Cartesian for convex sector display)
            var imageData = _scanConverter.Convert(compressed, _displayWidth, _displayHeight);

            // 8. Render to WriteableBitmap
            var bitmap = RenderToBitmap(imageData, _displayWidth, _displayHeight);

            // Update frame rate
            _frameCount++;
            double elapsed = _fpsTimer.Elapsed.TotalSeconds;
            if (elapsed > 0.5)
            {
                _frameRateHz = _frameCount / elapsed;
                _frameCount = 0;
                _fpsTimer.Restart();
            }

            var frame = new UltrasoundFrame
            {
                FrameId = _frameCount,
                Timestamp = DateTime.UtcNow,
                RfData = rfData,
                BeamformedData = beamformed,
                EnvelopeData = envelope,
                BmodeImageData = imageData,
                RenderedImage = bitmap,
                ImageWidth = _displayWidth,
                ImageHeight = _displayHeight,
                ScanlineCount = rfData.Length,
                SamplesPerLine = rfData[0].Length,
                DepthM = scanParams.DepthM,
                Mode = ImagingMode.BMode,
                FrameRateHz = _frameRateHz,
                MechanicalIndex = CalculateMI(scanParams),
                ThermalIndex = CalculateTI(scanParams)
            };

            FrameReady?.Invoke(this, frame);
            return frame;
        }

        private static WriteableBitmap RenderToBitmap(byte[] imageData, int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
            bitmap.Lock();
            try
            {
                var backBuffer = bitmap.BackBuffer;
                int stride = bitmap.BackBufferStride;

                unsafe
                {
                    var ptr = (byte*)backBuffer;
                    for (int y = 0; y < height; y++)
                    {
                        int srcOffset = y * width;
                        int dstOffset = y * stride;
                        for (int x = 0; x < width; x++)
                        {
                            ptr[dstOffset + x] = imageData[srcOffset + x];
                        }
                    }
                }

                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                bitmap.Unlock();
            }
            bitmap.Freeze();
            return bitmap;
        }

        private static double CalculateMI(ScanParameters p)
        {
            // MI = PNP / sqrt(fc_MHz) — simplified estimation
            double fcMHz = p.TransmitFrequencyHz / 1e6;
            double estimatedPnpMPa = p.TransmitPower * 2.0;
            return estimatedPnpMPa / Math.Sqrt(fcMHz);
        }

        private static double CalculateTI(ScanParameters p)
        {
            // TIS = W_output / W_deg — simplified estimation
            return p.TransmitPower * 0.5;
        }

        public void Dispose() => Stop();
    }
}
