namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Result of a segmentation operation.
    /// </summary>
    public class SegmentationResult
    {
        public SegmentationAlgorithm Algorithm { get; set; }
        public byte[]? Mask { get; set; }
        public List<(double X, double Y)> Contour { get; set; } = [];
        public int Width { get; set; }
        public int Height { get; set; }
        public (int X, int Y) SeedPoint { get; set; }
        public double AreaCm2 { get; set; }
        public double PerimeterCm { get; set; }
        public double ProcessingTimeMs { get; set; }
    }
}
