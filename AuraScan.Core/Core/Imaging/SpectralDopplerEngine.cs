using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraScan_Ultrasound_System.Core.SignalProcessing;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Imaging
{
    /// <summary>
    /// Spectral (PW) Doppler imaging engine.
    /// Displays velocity spectrum over time at a user-selected sample volume gate.
    /// Output is a scrolling spectrogram with the velocity axis and a baseline.
    /// </summary>
    public sealed class SpectralDopplerEngine : IImagingEngine
    {
        private readonly BModeEngine _bmodeEngine;
        private readonly DopplerProcessor _dopplerProcessor;

        private ProbeConfiguration _probeConfig = new();
        private int _displayWidth;
        private int _displayHeight;
        private long _frameCount;
        private readonly Stopwatch _fpsTimer = new();
        private double _frameRateHz;

        // Spectrogram scroll buffer [timeColumn][frequencyBin]
        private double[][]? _spectrogramBuffer;
        private int _spectrogramWriteIndex;

        // Spectral display dimensions (lower panel of split view)
        private int _spectralWidth;
        private int _spectralHeight;

        public ImagingMode Mode => ImagingMode.SpectralDoppler;
        public bool IsRunning { get; private set; }
        public double FrameRateHz => _frameRateHz;

        /// <summary>Selected scanline for Doppler gate.</summary>
        public int GateScanline { get; set; }

        /// <summary>Selected sample index for Doppler gate center.</summary>
        public int GateSampleIndex { get; set; }

        /// <summary>Gate size in samples.</summary>
        public int GateSize { get; set; } = 10;

        /// <summary>Doppler ensemble data for spectral computation.</summary>
        public double[][][]? CurrentEnsemble { get; set; }

        /// <summary>Latest spectral display bitmap.</summary>
        public WriteableBitmap? SpectralImage { get; private set; }

        public event EventHandler<UltrasoundFrame>? FrameReady;

        public SpectralDopplerEngine()
        {
            _bmodeEngine = new BModeEngine();
            _dopplerProcessor = new DopplerProcessor();
        }

        public void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams,
            int displayWidth, int displayHeight)
        {
            _probeConfig = probeConfig;
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;

            // Upper 40%: B-Mode with gate marker, Lower 60%: Spectral display
            int bmodeHeight = (int)(displayHeight * 0.4);
            _spectralWidth = displayWidth;
            _spectralHeight = displayHeight - bmodeHeight;

            _bmodeEngine.Initialize(probeConfig, scanParams, displayWidth, bmodeHeight);

            // Default gate at center
            GateScanline = probeConfig.ScanlinesPerFrame / 2;
            GateSampleIndex = probeConfig.SamplesPerLine / 4;

            // Initialize spectrogram buffer
            int fftSize = scanParams.SpectralFftSize;
            _spectrogramBuffer = new double[_spectralWidth][];
            for (int i = 0; i < _spectralWidth; i++)
                _spectrogramBuffer[i] = new double[fftSize];
            _spectrogramWriteIndex = 0;

            _frameCount = 0;
            _fpsTimer.Restart();
        }

        public void Start()
        {
            IsRunning = true;
            _bmodeEngine.Start();
        }

        public void Stop()
        {
            IsRunning = false;
            _bmodeEngine.Stop();
        }

        public UltrasoundFrame ProcessFrame(double[][] rfData, ScanParameters scanParams)
        {
            // Process B-Mode (upper panel)
            int bmodeHeight = (int)(_displayHeight * 0.4);
            var bmodeFrame = _bmodeEngine.ProcessFrame(rfData, scanParams);

            // Process Spectral Doppler if ensemble is available
            if (CurrentEnsemble != null && CurrentEnsemble.Length > 0)
            {
                var spectrogram = _dopplerProcessor.ComputeSpectralDoppler(
                    CurrentEnsemble, GateScanline, GateSampleIndex, GateSize,
                    scanParams, _probeConfig);

                // Add new spectral columns to the scrolling buffer
                for (int col = 0; col < spectrogram.Length; col++)
                {
                    if (_spectrogramBuffer != null)
                    {
                        _spectrogramBuffer[_spectrogramWriteIndex] = spectrogram[col];
                        _spectrogramWriteIndex = (_spectrogramWriteIndex + 1) % _spectralWidth;
                    }
                }
            }

            // Render spectral display
            SpectralImage = RenderSpectrogram(scanParams);

            // Composite B-Mode + Spectral into full display
            var compositeImage = CompositeBModeAndSpectral(
                bmodeFrame.RenderedImage!, SpectralImage, _displayWidth, _displayHeight, bmodeHeight);

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
                RenderedImage = compositeImage,
                SpectralData = _spectrogramBuffer,
                ImageWidth = _displayWidth,
                ImageHeight = _displayHeight,
                DepthM = scanParams.DepthM,
                Mode = ImagingMode.SpectralDoppler,
                FrameRateHz = _frameRateHz
            };

            FrameReady?.Invoke(this, frame);
            return frame;
        }

        private WriteableBitmap RenderSpectrogram(ScanParameters scanParams)
        {
            int width = _spectralWidth;
            int height = _spectralHeight;
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            if (_spectrogramBuffer == null)
                return bitmap;

            int fftSize = scanParams.SpectralFftSize;
            double dynamicRange = 50.0; // dB display range

            bitmap.Lock();
            try
            {
                unsafe
                {
                    var ptr = (byte*)bitmap.BackBuffer;
                    int stride = bitmap.BackBufferStride;

                    for (int x = 0; x < width; x++)
                    {
                        int bufIdx = (_spectrogramWriteIndex + x) % width;

                        for (int y = 0; y < height; y++)
                        {
                            // Map y to frequency bin (0=+Nyquist at top, height-1=-Nyquist at bottom)
                            int freqBin = (int)((double)y / height * fftSize);
                            freqBin = Math.Clamp(freqBin, 0, fftSize - 1);

                            double magnitude = _spectrogramBuffer[bufIdx].Length > freqBin
                                ? _spectrogramBuffer[bufIdx][freqBin]
                                : -100.0;

                            // Normalize to display range
                            double normalized = (magnitude + dynamicRange) / dynamicRange;
                            normalized = Math.Clamp(normalized, 0, 1);
                            byte intensity = (byte)(normalized * 255);

                            int pixelOffset = y * stride + x * 4;
                            ptr[pixelOffset + 0] = intensity;  // B
                            ptr[pixelOffset + 1] = intensity;  // G
                            ptr[pixelOffset + 2] = intensity;  // R
                            ptr[pixelOffset + 3] = 255;        // A
                        }
                    }

                    // Draw baseline (zero velocity) at center
                    int baselineY = height / 2;
                    for (int x = 0; x < width; x++)
                    {
                        int pixelOffset = baselineY * stride + x * 4;
                        ptr[pixelOffset + 0] = 80;   // B (cyan-ish)
                        ptr[pixelOffset + 1] = 200;   // G
                        ptr[pixelOffset + 2] = 200;   // R
                        ptr[pixelOffset + 3] = 255;
                    }

                    // Draw sweep line
                    int sweepX = width - 1;
                    for (int y = 0; y < height; y++)
                    {
                        int pixelOffset = y * stride + sweepX * 4;
                        ptr[pixelOffset + 0] = 0;
                        ptr[pixelOffset + 1] = 255;
                        ptr[pixelOffset + 2] = 255;
                        ptr[pixelOffset + 3] = 255;
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

        private static WriteableBitmap CompositeBModeAndSpectral(WriteableBitmap bmode,
            WriteableBitmap spectral, int width, int totalHeight, int bmodeHeight)
        {
            var composite = new WriteableBitmap(width, totalHeight, 96, 96, PixelFormats.Bgra32, null);
            int spectralHeight = totalHeight - bmodeHeight;

            composite.Lock();
            try
            {
                unsafe
                {
                    var dst = (byte*)composite.BackBuffer;
                    int dstStride = composite.BackBufferStride;

                    // Copy B-Mode (upper panel) - convert Gray8 to BGRA32
                    var bmodePixels = new byte[bmode.PixelWidth * bmode.PixelHeight];
                    bmode.CopyPixels(bmodePixels, bmode.BackBufferStride, 0);

                    for (int y = 0; y < bmodeHeight; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIdx = y * bmode.BackBufferStride + x;
                            byte gray = srcIdx < bmodePixels.Length ? bmodePixels[srcIdx] : (byte)0;
                            int dstIdx = y * dstStride + x * 4;
                            dst[dstIdx + 0] = gray;
                            dst[dstIdx + 1] = gray;
                            dst[dstIdx + 2] = gray;
                            dst[dstIdx + 3] = 255;
                        }
                    }

                    // Copy Spectral (lower panel)
                    var spectralPixels = new byte[width * spectralHeight * 4];
                    spectral.CopyPixels(spectralPixels, width * 4, 0);

                    for (int y = 0; y < spectralHeight; y++)
                    {
                        int srcOffset = y * width * 4;
                        int dstOffset = (bmodeHeight + y) * dstStride;
                        for (int x = 0; x < width * 4 && srcOffset + x < spectralPixels.Length; x++)
                            dst[dstOffset + x] = spectralPixels[srcOffset + x];
                    }

                    // Draw divider line between panels
                    for (int x = 0; x < width; x++)
                    {
                        int dstIdx = bmodeHeight * dstStride + x * 4;
                        dst[dstIdx + 0] = 100;
                        dst[dstIdx + 1] = 100;
                        dst[dstIdx + 2] = 100;
                        dst[dstIdx + 3] = 255;
                    }
                }
                composite.AddDirtyRect(new Int32Rect(0, 0, width, totalHeight));
            }
            finally
            {
                composite.Unlock();
            }
            composite.Freeze();
            return composite;
        }

        public void Dispose()
        {
            Stop();
            _bmodeEngine.Dispose();
        }
    }
}
