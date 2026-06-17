using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraScan_Ultrasound_System.Core.SignalProcessing;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Imaging
{
    /// <summary>
    /// Color Doppler imaging engine.
    /// Overlays color-coded velocity/power data on top of the B-Mode image.
    /// Uses autocorrelation (Kasai estimator) for mean velocity estimation.
    /// </summary>
    public sealed class ColorDopplerEngine : IImagingEngine
    {
        private readonly BModeEngine _bmodeEngine;
        private readonly DopplerProcessor _dopplerProcessor;
        private readonly ScanConverter _scanConverter;

        private ProbeConfiguration _probeConfig = new();
        private int _displayWidth;
        private int _displayHeight;
        private long _frameCount;
        private readonly Stopwatch _fpsTimer = new();
        private double _frameRateHz;

        public ImagingMode Mode => ImagingMode.ColorDoppler;
        public bool IsRunning { get; private set; }
        public double FrameRateHz => _frameRateHz;

        /// <summary>Doppler ensemble data for velocity estimation.</summary>
        public double[][][]? CurrentEnsemble { get; set; }

        public event EventHandler<UltrasoundFrame>? FrameReady;

        public ColorDopplerEngine()
        {
            _bmodeEngine = new BModeEngine();
            _dopplerProcessor = new DopplerProcessor();
            _scanConverter = new ScanConverter();
        }

        public void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams,
            int displayWidth, int displayHeight)
        {
            _probeConfig = probeConfig;
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;

            _bmodeEngine.Initialize(probeConfig, scanParams, displayWidth, displayHeight);
            _scanConverter.Initialize(probeConfig, scanParams, displayWidth, displayHeight);

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
            // Process B-Mode background
            var bmodeFrame = _bmodeEngine.ProcessFrame(rfData, scanParams);

            // Process Color Doppler if ensemble data is available
            if (CurrentEnsemble != null && CurrentEnsemble.Length >= 2)
            {
                var (velocity, power, variance) = _dopplerProcessor.EstimateColorFlow(
                    CurrentEnsemble, scanParams, _probeConfig);

                // Scan convert velocity data
                var velocityImage = _scanConverter.ConvertDoppler(velocity, _displayWidth, _displayHeight);

                // Color-map and overlay onto B-Mode
                var colorOverlay = CreateColorOverlay(velocityImage, power, scanParams);

                var compositeImage = CompositeImages(bmodeFrame.RenderedImage!, colorOverlay,
                    _displayWidth, _displayHeight);

                bmodeFrame.DopplerVelocityData = velocity;
                bmodeFrame.DopplerPowerData = power;
                bmodeFrame.DopplerOverlay = colorOverlay;
                bmodeFrame.RenderedImage = compositeImage;
                bmodeFrame.Mode = scanParams.Mode == ImagingMode.PowerDoppler
                    ? ImagingMode.PowerDoppler
                    : ImagingMode.ColorDoppler;
            }

            // Update frame rate
            _frameCount++;
            double elapsed = _fpsTimer.Elapsed.TotalSeconds;
            if (elapsed > 0.5)
            {
                _frameRateHz = _frameCount / elapsed;
                _frameCount = 0;
                _fpsTimer.Restart();
            }

            bmodeFrame.FrameRateHz = _frameRateHz;
            FrameReady?.Invoke(this, bmodeFrame);
            return bmodeFrame;
        }

        private WriteableBitmap CreateColorOverlay(double[] velocityImage,
            double[][] powerData, ScanParameters scanParams)
        {
            int width = _displayWidth;
            int height = _displayHeight;
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            double nyquist = scanParams.NyquistVelocityMps;

            bitmap.Lock();
            try
            {
                unsafe
                {
                    var ptr = (byte*)bitmap.BackBuffer;
                    int stride = bitmap.BackBufferStride;

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int idx = y * width + x;
                            double velocity = velocityImage[idx];
                            int pixelOffset = y * stride + x * 4;

                            if (Math.Abs(velocity) < nyquist * 0.02)
                            {
                                // Below noise threshold — transparent
                                ptr[pixelOffset + 3] = 0; // Alpha
                                continue;
                            }

                            byte r, g, b, a;

                            if (scanParams.Mode == ImagingMode.PowerDoppler)
                            {
                                // Power Doppler: orange/yellow map
                                double normalizedPower = Math.Clamp(Math.Abs(velocity) / nyquist, 0, 1);
                                r = (byte)(255 * normalizedPower);
                                g = (byte)(165 * normalizedPower);
                                b = 0;
                                a = (byte)(200 * normalizedPower);
                            }
                            else
                            {
                                // Color Flow: Red-Blue velocity map
                                double normalizedV = Math.Clamp(velocity / nyquist, -1, 1);
                                (r, g, b) = MapVelocityToColor(normalizedV, scanParams.ColorMap);
                                a = (byte)(180 * Math.Min(Math.Abs(normalizedV) * 2.0, 1.0));
                            }

                            ptr[pixelOffset + 0] = b; // Blue
                            ptr[pixelOffset + 1] = g; // Green
                            ptr[pixelOffset + 2] = r; // Red
                            ptr[pixelOffset + 3] = a; // Alpha
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

        private static (byte R, byte G, byte B) MapVelocityToColor(double normalizedVelocity, DopplerColorMap colorMap)
        {
            return colorMap switch
            {
                DopplerColorMap.RedBlue => normalizedVelocity >= 0
                    ? ((byte)(255 * normalizedVelocity), (byte)0, (byte)0)           // Toward = Red
                    : ((byte)0, (byte)0, (byte)(255 * -normalizedVelocity)),          // Away = Blue
                DopplerColorMap.BlueRed => normalizedVelocity >= 0
                    ? ((byte)0, (byte)0, (byte)(255 * normalizedVelocity))
                    : ((byte)(255 * -normalizedVelocity), (byte)0, (byte)0),
                DopplerColorMap.Velocity => normalizedVelocity >= 0
                    ? ((byte)(255 * normalizedVelocity), (byte)(100 * normalizedVelocity), (byte)0)
                    : ((byte)0, (byte)(100 * -normalizedVelocity), (byte)(255 * -normalizedVelocity)),
                _ => normalizedVelocity >= 0
                    ? ((byte)(255 * normalizedVelocity), (byte)0, (byte)0)
                    : ((byte)0, (byte)0, (byte)(255 * -normalizedVelocity))
            };
        }

        private static WriteableBitmap CompositeImages(WriteableBitmap bmode, WriteableBitmap overlay,
            int width, int height)
        {
            var composite = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            // Convert B-Mode Gray8 to BGRA32 and blend with overlay
            composite.Lock();
            try
            {
                unsafe
                {
                    var dst = (byte*)composite.BackBuffer;
                    int dstStride = composite.BackBufferStride;

                    // Copy B-Mode as grayscale BGRA
                    var bmodeBuffer = new byte[bmode.PixelWidth * bmode.PixelHeight];
                    bmode.CopyPixels(bmodeBuffer, bmode.BackBufferStride, 0);

                    var overlayBuffer = new byte[width * height * 4];
                    overlay.CopyPixels(overlayBuffer, width * 4, 0);

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int bmodeIdx = y * bmode.BackBufferStride + x;
                            byte gray = bmodeIdx < bmodeBuffer.Length ? bmodeBuffer[bmodeIdx] : (byte)0;

                            int overlayIdx = (y * width + x) * 4;
                            byte ob = overlayBuffer[overlayIdx + 0];
                            byte og = overlayBuffer[overlayIdx + 1];
                            byte or2 = overlayBuffer[overlayIdx + 2];
                            byte oa = overlayBuffer[overlayIdx + 3];

                            int dstIdx = y * dstStride + x * 4;
                            double alpha = oa / 255.0;

                            dst[dstIdx + 0] = (byte)(ob * alpha + gray * (1.0 - alpha)); // B
                            dst[dstIdx + 1] = (byte)(og * alpha + gray * (1.0 - alpha)); // G
                            dst[dstIdx + 2] = (byte)(or2 * alpha + gray * (1.0 - alpha)); // R
                            dst[dstIdx + 3] = 255; // A
                        }
                    }
                }
                composite.AddDirtyRect(new Int32Rect(0, 0, width, height));
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
