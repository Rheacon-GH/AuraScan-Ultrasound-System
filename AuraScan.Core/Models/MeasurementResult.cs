namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Result of an ultrasound measurement operation.
    /// </summary>
    public class MeasurementResult
    {
        public MeasurementType Type { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public (double X, double Y) StartPoint { get; set; }
        public (double X, double Y) EndPoint { get; set; }
        public string Label { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
