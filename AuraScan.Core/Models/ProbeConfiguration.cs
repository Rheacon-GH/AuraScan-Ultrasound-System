namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Physical and operational specification for an ultrasound transducer.
    /// Default values represent the Philips C5-1 convex (curvilinear) probe.
    /// </summary>
    public class ProbeConfiguration
    {
        public string ModelName { get; set; } = "C5-1";
        public string Manufacturer { get; set; } = "Philips";
        public ProbeType ProbeType { get; set; } = ProbeType.Convex;

        public int ElementCount { get; set; } = 128;
        public double CenterFrequencyHz { get; set; } = 3.0e6;
        public double BandwidthLowHz { get; set; } = 1.0e6;
        public double BandwidthHighHz { get; set; } = 5.0e6;
        public double ElementPitchM { get; set; } = 0.000480;
        public double ConvexRadiusM { get; set; } = 0.060;
        public double FieldOfViewDegrees { get; set; } = 70.0;
        public double MaxDepthM { get; set; } = 0.30;
        public double SpeedOfSoundMps { get; set; } = 1540.0;
        public double SamplingFrequencyHz { get; set; } = 40.0e6;
        public int SamplesPerLine { get; set; } = 4096;
        public int ScanlinesPerFrame { get; set; } = 256;
        public int FocalZones { get; set; } = 2;

        // Derived properties
        public double ElementAngularSpanRad => FieldOfViewDegrees * Math.PI / 180.0 / ElementCount;
        public double StartAngleRad => -FieldOfViewDegrees * Math.PI / 180.0 / 2.0;
        public double WavelengthM => SpeedOfSoundMps / CenterFrequencyHz;
        public double MaxRoundTripTimeS => 2.0 * MaxDepthM / SpeedOfSoundMps;
        public double MaxPrfHz => 1.0 / MaxRoundTripTimeS;
    }
}
