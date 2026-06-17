using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AuraScan_Ultrasound_System.Core.SignalProcessing;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Imaging
{
    /// <summary>
    /// 3D Volume imaging engine.
    /// Acquires a series of B-Mode slices across a sweep angle,
    /// reconstructs a 3D volume, and provides multi-planar reformatting (MPR)
    /// and surface rendering via HelixToolkit.
    /// </summary>
    public sealed class VolumeEngine : IImagingEngine
    {
        private readonly BModeEngine _bmodeEngine;

        private ProbeConfiguration _probeConfig = new();
        private int _displayWidth;
        private int _displayHeight;
        private long _frameCount;
        private readonly Stopwatch _fpsTimer = new();
        private double _frameRateHz;

        // Volume data storage
        private byte[][]? _volumeSlices; // [sliceIndex] = scan-converted image data
        private int _sliceCount;
        private int _currentSlice;
        private bool _volumeComplete;

        // Volume dimensions
        private int _sliceWidth;
        private int _sliceHeight;

        public ImagingMode Mode => ImagingMode.Volume3D;
        public bool IsRunning { get; private set; }
        public double FrameRateHz => _frameRateHz;

        /// <summary>Whether volume acquisition is complete.</summary>
        public bool IsVolumeComplete => _volumeComplete;

        /// <summary>Progress of volume acquisition (0.0 to 1.0).</summary>
        public double AcquisitionProgress => _sliceCount > 0 ? (double)_currentSlice / _sliceCount : 0;

        /// <summary>The reconstructed 3D volume data [x, y, z].</summary>
        public byte[,,]? VolumeData { get; private set; }

        public event EventHandler<UltrasoundFrame>? FrameReady;

        /// <summary>Event raised when volume acquisition is complete.</summary>
        public event EventHandler? VolumeAcquisitionComplete;

        public VolumeEngine()
        {
            _bmodeEngine = new BModeEngine();
        }

        public void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams,
            int displayWidth, int displayHeight)
        {
            _probeConfig = probeConfig;
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;
            _sliceWidth = displayWidth;
            _sliceHeight = displayHeight;

            _bmodeEngine.Initialize(probeConfig, scanParams, displayWidth, displayHeight);

            _sliceCount = scanParams.VolumeSliceCount;
            _volumeSlices = new byte[_sliceCount][];
            _currentSlice = 0;
            _volumeComplete = false;

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

        /// <summary>
        /// Reset volume acquisition for a new sweep.
        /// </summary>
        public void ResetAcquisition()
        {
            _currentSlice = 0;
            _volumeComplete = false;
            if (_volumeSlices != null)
            {
                for (int i = 0; i < _volumeSlices.Length; i++)
                    _volumeSlices[i] = null!;
            }
            VolumeData = null;
        }

        public UltrasoundFrame ProcessFrame(double[][] rfData, ScanParameters scanParams)
        {
            // Process as B-Mode slice
            var bmodeFrame = _bmodeEngine.ProcessFrame(rfData, scanParams);

            if (!_volumeComplete && _volumeSlices != null && _currentSlice < _sliceCount)
            {
                // Store this slice
                _volumeSlices[_currentSlice] = bmodeFrame.BmodeImageData ?? [];
                _currentSlice++;

                if (_currentSlice >= _sliceCount)
                {
                    _volumeComplete = true;
                    ReconstructVolume();
                    VolumeAcquisitionComplete?.Invoke(this, EventArgs.Empty);
                }
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

            bmodeFrame.Mode = ImagingMode.Volume3D;
            bmodeFrame.FrameRateHz = _frameRateHz;
            FrameReady?.Invoke(this, bmodeFrame);
            return bmodeFrame;
        }

        /// <summary>
        /// Reconstruct 3D volume from acquired slices.
        /// </summary>
        private void ReconstructVolume()
        {
            if (_volumeSlices == null) return;

            VolumeData = new byte[_sliceWidth, _sliceHeight, _sliceCount];

            for (int z = 0; z < _sliceCount; z++)
            {
                var slice = _volumeSlices[z];
                if (slice == null) continue;

                for (int y = 0; y < _sliceHeight; y++)
                {
                    for (int x = 0; x < _sliceWidth; x++)
                    {
                        int idx = y * _sliceWidth + x;
                        VolumeData[x, y, z] = idx < slice.Length ? slice[idx] : (byte)0;
                    }
                }
            }
        }

        /// <summary>
        /// Extract a multi-planar reformat (MPR) slice from the volume.
        /// </summary>
        /// <param name="plane">0=Axial (XY), 1=Sagittal (YZ), 2=Coronal (XZ)</param>
        /// <param name="sliceIndex">Index along the perpendicular axis</param>
        public WriteableBitmap? GetMprSlice(int plane, int sliceIndex)
        {
            if (VolumeData == null) return null;

            int dimX = VolumeData.GetLength(0);
            int dimY = VolumeData.GetLength(1);
            int dimZ = VolumeData.GetLength(2);

            int width, height;
            byte[] imageData;

            switch (plane)
            {
                case 0: // Axial (XY at given Z)
                    sliceIndex = Math.Clamp(sliceIndex, 0, dimZ - 1);
                    width = dimX;
                    height = dimY;
                    imageData = new byte[width * height];
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            imageData[y * width + x] = VolumeData[x, y, sliceIndex];
                    break;

                case 1: // Sagittal (YZ at given X)
                    sliceIndex = Math.Clamp(sliceIndex, 0, dimX - 1);
                    width = dimZ;
                    height = dimY;
                    imageData = new byte[width * height];
                    for (int y = 0; y < height; y++)
                        for (int z = 0; z < width; z++)
                            imageData[y * width + z] = VolumeData[sliceIndex, y, z];
                    break;

                case 2: // Coronal (XZ at given Y)
                    sliceIndex = Math.Clamp(sliceIndex, 0, dimY - 1);
                    width = dimX;
                    height = dimZ;
                    imageData = new byte[width * height];
                    for (int z = 0; z < height; z++)
                        for (int x = 0; x < width; x++)
                            imageData[z * width + x] = VolumeData[x, sliceIndex, z];
                    break;

                default:
                    return null;
            }

            return RenderSliceToBitmap(imageData, width, height);
        }

        /// <summary>
        /// Generate a 3D mesh model from the volume data using isosurface extraction.
        /// Returns mesh geometry suitable for HelixToolkit viewport.
        /// </summary>
        public MeshGeometry3D? GenerateIsosurfaceMesh(byte threshold = 80)
        {
            if (VolumeData == null) return null;

            int dimX = VolumeData.GetLength(0);
            int dimY = VolumeData.GetLength(1);
            int dimZ = VolumeData.GetLength(2);

            var mesh = new MeshGeometry3D();
            var positions = new Point3DCollection();
            var normals = new Vector3DCollection();
            var indices = new Int32Collection();

            // Simplified marching cubes: extract surface at threshold
            double scaleX = 1.0 / dimX;
            double scaleY = 1.0 / dimY;
            double scaleZ = 1.0 / dimZ;

            for (int z = 0; z < dimZ - 1; z++)
            {
                for (int y = 0; y < dimY - 1; y++)
                {
                    for (int x = 0; x < dimX - 1; x++)
                    {
                        // Check if this voxel is on the surface boundary
                        byte v000 = VolumeData[x, y, z];
                        byte v100 = VolumeData[x + 1, y, z];
                        byte v010 = VolumeData[x, y + 1, z];
                        byte v001 = VolumeData[x, y, z + 1];

                        bool inside = v000 >= threshold;
                        bool xBoundary = (v100 >= threshold) != inside;
                        bool yBoundary = (v010 >= threshold) != inside;
                        bool zBoundary = (v001 >= threshold) != inside;

                        if (xBoundary || yBoundary || zBoundary)
                        {
                            // Add a small quad at this boundary voxel
                            double px = x * scaleX - 0.5;
                            double py = y * scaleY - 0.5;
                            double pz = z * scaleZ - 0.5;
                            double s = scaleX * 0.5;

                            int baseIdx = positions.Count;

                            if (xBoundary)
                            {
                                double fx = (x + 0.5) * scaleX - 0.5;
                                positions.Add(new Point3D(fx, py, pz));
                                positions.Add(new Point3D(fx, py + s, pz));
                                positions.Add(new Point3D(fx, py + s, pz + s));
                                positions.Add(new Point3D(fx, py, pz + s));

                                var normal = new Vector3D(inside ? -1 : 1, 0, 0);
                                for (int i = 0; i < 4; i++) normals.Add(normal);

                                indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
                                indices.Add(baseIdx); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
                                baseIdx += 4;
                            }

                            if (yBoundary)
                            {
                                double fy = (y + 0.5) * scaleY - 0.5;
                                positions.Add(new Point3D(px, fy, pz));
                                positions.Add(new Point3D(px + s, fy, pz));
                                positions.Add(new Point3D(px + s, fy, pz + s));
                                positions.Add(new Point3D(px, fy, pz + s));

                                var normal = new Vector3D(0, inside ? -1 : 1, 0);
                                for (int i = 0; i < 4; i++) normals.Add(normal);

                                indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
                                indices.Add(baseIdx); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
                                baseIdx += 4;
                            }

                            if (zBoundary)
                            {
                                double fz = (z + 0.5) * scaleZ - 0.5;
                                positions.Add(new Point3D(px, py, fz));
                                positions.Add(new Point3D(px + s, py, fz));
                                positions.Add(new Point3D(px + s, py + s, fz));
                                positions.Add(new Point3D(px, py + s, fz));

                                var normal = new Vector3D(0, 0, inside ? -1 : 1);
                                for (int i = 0; i < 4; i++) normals.Add(normal);

                                indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
                                indices.Add(baseIdx); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
                            }
                        }
                    }
                }
            }

            mesh.Positions = positions;
            mesh.Normals = normals;
            mesh.TriangleIndices = indices;
            mesh.Freeze();
            return mesh;
        }

        private static WriteableBitmap RenderSliceToBitmap(byte[] imageData, int width, int height)
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
                        int srcOff = y * width;
                        int dstOff = y * stride;
                        for (int x = 0; x < width; x++)
                            ptr[dstOff + x] = imageData[srcOff + x];
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

        public void Dispose()
        {
            Stop();
            _bmodeEngine.Dispose();
        }
    }
}
