namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Runtime scan parameters controlling acquisition and processing.
    /// </summary>
    public class ScanParameters
    {
        public ImagingMode Mode { get; set; } = ImagingMode.BMode;
        public double DepthM { get; set; } = 0.15;
        public double GainDb { get; set; } = 50.0;
        public double DynamicRangeDb { get; set; } = 60.0;
        public double TransmitPower { get; set; } = 0.8;
        public int Persistence { get; set; } = 2;
        public double SectorAngleDeg { get; set; } = 70.0;
        public bool HarmonicImaging { get; set; }
        public double TransmitFrequencyHz { get; set; } = 3.5e6;
        public double[] TgcCurve { get; set; } = [0.7, 0.6, 0.5, 0.5, 0.5, 0.5, 0.6, 0.7];
        public double[] FocalDepthsM { get; set; } = [0.06, 0.09];
        public TissuePreset Preset { get; set; } = TissuePreset.Abdomen;

        // Doppler parameters
        public double DopplerPrfHz { get; set; } = 4000.0;
        public double DopplerAngleCorrectionDeg { get; set; } = 60.0;
        public double DopplerVelocityScaleMps { get; set; } = 0.5;
        public int ColorEnsembleLength { get; set; } = 8;
        public int SpectralFftSize { get; set; } = 128;
        public double DopplerWallFilterHz { get; set; } = 50.0;
        public double NyquistVelocityMps { get; set; } = 0.5;
        public DopplerColorMap ColorMap { get; set; } = DopplerColorMap.RedBlue;

        // Color box region (scanline/sample indices)
        public int ColorBoxStartLine { get; set; } = 0;
        public int ColorBoxEndLine { get; set; } = 256;
        public int ColorBoxStartSample { get; set; } = 0;
        public int ColorBoxEndSample { get; set; } = 4096;

        // Volume parameters
        public int VolumeSliceCount { get; set; } = 60;
    }
}
