using System.Windows.Media.Imaging;

namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Represents a single processed ultrasound frame with raw and rendered data.
    /// </summary>
    public class UltrasoundFrame
    {
        public long FrameId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>Raw RF data [scanline][sample].</summary>
        public double[][]? RfData { get; set; }

        /// <summary>Beamformed data [scanline][sample].</summary>
        public double[][]? BeamformedData { get; set; }

        /// <summary>Envelope-detected data [scanline][sample].</summary>
        public double[][]? EnvelopeData { get; set; }

        /// <summary>B-mode grayscale image data (8-bit, row-major).</summary>
        public byte[]? BmodeImageData { get; set; }

        /// <summary>Rendered display image.</summary>
        public WriteableBitmap? RenderedImage { get; set; }

        /// <summary>Image width in pixels.</summary>
        public int ImageWidth { get; set; }

        /// <summary>Image height in pixels.</summary>
        public int ImageHeight { get; set; }

        /// <summary>Number of scanlines in this frame.</summary>
        public int ScanlineCount { get; set; }

        /// <summary>Number of samples per scanline.</summary>
        public int SamplesPerLine { get; set; }

        /// <summary>Imaging depth in meters.</summary>
        public double DepthM { get; set; }

        /// <summary>Imaging mode used for this frame.</summary>
        public ImagingMode Mode { get; set; }

        /// <summary>Current frame rate in Hz.</summary>
        public double FrameRateHz { get; set; }

        /// <summary>Mechanical Index safety metric.</summary>
        public double MechanicalIndex { get; set; }

        /// <summary>Thermal Index safety metric.</summary>
        public double ThermalIndex { get; set; }

        /// <summary>Doppler velocity data [scanline][sample].</summary>
        public double[][]? DopplerVelocityData { get; set; }

        /// <summary>Doppler power data [scanline][sample].</summary>
        public double[][]? DopplerPowerData { get; set; }

        /// <summary>Doppler color overlay image.</summary>
        public WriteableBitmap? DopplerOverlay { get; set; }

        /// <summary>Spectral Doppler data buffer.</summary>
        public double[][]? SpectralData { get; set; }
    }
}
