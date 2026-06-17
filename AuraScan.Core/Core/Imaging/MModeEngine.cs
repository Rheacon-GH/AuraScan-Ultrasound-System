using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraScan_Ultrasound_System.Core.SignalProcessing;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Imaging
{
    /// <summary>
    /// M-Mode (Motion Mode) imaging engine.
    /// Displays tissue motion along a single scanline over time as a scrolling strip.
    /// The display shows depth (vertical) vs. time (horizontal).
    /// </summary>
    public sealed class MModeEngine : IImagingEngine
    {
        private readonly SignalProcessor _signalProcessor;

        private ProbeConfiguration _probeConfig = new();
        private int _displayWidth;
        private int _displayHeight;
        private int _mModeLine; // Selected scanline for M-Mode
        private byte[][] _stripBuffer; // [timeColumn][depthPixel] — circular buffer
        private int _writeIndex;
        private long _frameCount;
        private readonly Stopwatch _fpsTimer = new();
        private double _frameRateHz;

        public ImagingMode Mode => ImagingMode.MMode;
        public bool IsRunning { get; private set; }
        public double FrameRateHz => _frameRateHz;

        /// <summary>Selected scanline index for M-Mode display.</summary>
        public int SelectedScanline
        {
            get => _mModeLine;
            set => _mModeLine = value;
        }

        public event EventHandler<UltrasoundFrame>? FrameReady;

        public MModeEngine()
        {
            _signalProcessor = new SignalProcessor();
            _stripBuffer = [];
        }

        public void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams,
            int displayWidth, int displayHeight)
        {
            _probeConfig = probeConfig;
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;
            _mModeLine = probeConfig.ScanlinesPerFrame / 2; // Default to center scanline

            // Initialize circular strip buffer
            _stripBuffer = new byte[displayWidth][];
            for (int i = 0; i < displayWidth; i++)
                _stripBuffer[i] = new byte[displayHeight];

            _writeIndex = 0;
            _frameCount = 0;
            _fpsTimer.Restart();
        }

        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;

        public UltrasoundFrame ProcessFrame(double[][] rfData, ScanParameters scanParams)
        {
            // Extract the selected scanline
            int lineIdx = Math.Clamp(_mModeLine, 0, rfData.Length - 1);
            double[] scanlineRf = rfData[lineIdx];

            // Envelope detection on single scanline
            double[][] singleLine = [scanlineRf];
            var envelope = _signalProcessor.EnvelopeDetect(singleLine);

            // TGC
            _signalProcessor.ApplyTgc(envelope, scanParams.TgcCurve);

            // Gain
            _signalProcessor.ApplyGain(envelope, scanParams.GainDb - 50.0);

            // Log compression
            var compressed = _signalProcessor.LogCompress(envelope, scanParams.DynamicRangeDb);

            // Resample to display height (depth axis)
            var column = ResampleToHeight(compressed[0], _displayHeight);

            // Write to circular buffer
            _stripBuffer[_writeIndex] = column;
            _writeIndex = (_writeIndex + 1) % _displayWidth;

            // Render strip to bitmap
            var imageData = RenderStrip();
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
                BmodeImageData = imageData,
                RenderedImage = bitmap,
                ImageWidth = _displayWidth,
                ImageHeight = _displayHeight,
                ScanlineCount = 1,
                SamplesPerLine = scanlineRf.Length,
                DepthM = scanParams.DepthM,
                Mode = ImagingMode.MMode,
                FrameRateHz = _frameRateHz,
                MechanicalIndex = 0.0,
                ThermalIndex = 0.0
            };

            FrameReady?.Invoke(this, frame);
            return frame;
        }

        private static byte[] ResampleToHeight(byte[] source, int targetHeight)
        {
            var result = new byte[targetHeight];
            double scale = (double)source.Length / targetHeight;

            for (int i = 0; i < targetHeight; i++)
            {
                double srcPos = i * scale;
                int srcIdx = (int)srcPos;
                double frac = srcPos - srcIdx;

                if (srcIdx + 1 < source.Length)
                    result[i] = (byte)(source[srcIdx] * (1.0 - frac) + source[srcIdx + 1] * frac);
                else if (srcIdx < source.Length)
                    result[i] = source[srcIdx];
            }

            return result;
        }

        private byte[] RenderStrip()
        {
            var imageData = new byte[_displayWidth * _displayHeight];

            for (int x = 0; x < _displayWidth; x++)
            {
                // Read from circular buffer so current write position is at the right edge
                int bufIdx = (_writeIndex + x) % _displayWidth;

                for (int y = 0; y < _displayHeight; y++)
                {
                    imageData[y * _displayWidth + x] = _stripBuffer[bufIdx][y];
                }
            }

            // Draw sweep line at current position
            int sweepX = _displayWidth - 1;
            for (int y = 0; y < _displayHeight; y++)
                imageData[y * _displayWidth + sweepX] = 255;

            return imageData;
        }

        private static WriteableBitmap RenderToBitmap(byte[] imageData, int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
            bitmap.Lock();
            try
            {
                unsafe
                {
                    var ptr = (byte*)bitmap.BackBuffer;
                    int stride = bitmap.BackBufferStride;
                    for (int y = 0; y < height; y++)
                    {
                        int srcOffset = y * width;
                        int dstOffset = y * stride;
                        for (int x = 0; x < width; x++)
                            ptr[dstOffset + x] = imageData[srcOffset + x];
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

        public void Dispose() => Stop();
    }
}
