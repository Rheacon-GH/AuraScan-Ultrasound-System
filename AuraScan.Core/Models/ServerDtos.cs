namespace AuraScan_Ultrasound_System.Models
{
    public class PatientDto
    {
        public int Id { get; set; }
        public string PatientId { get; set; } = string.Empty;
        public string PatientName { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string? Sex { get; set; }
        public double? WeightKg { get; set; }
        public string? AccessionNumber { get; set; }
        public string? ReferringPhysician { get; set; }
        public string? InstitutionName { get; set; }
    }

    public class StudyDto
    {
        public int Id { get; set; }
        public string StudyInstanceUid { get; set; } = string.Empty;
        public string? StudyDescription { get; set; }
        public DateTime StudyDateTime { get; set; }
        public string? PerformingPhysician { get; set; }
        public string? AccessionNumber { get; set; }
        public int PatientId { get; set; }
    }

    public class SeriesDto
    {
        public int Id { get; set; }
        public string SeriesInstanceUid { get; set; } = string.Empty;
        public string? SeriesDescription { get; set; }
        public string? Modality { get; set; } = "US";
        public int SeriesNumber { get; set; }
        public string? BodyPartExamined { get; set; }
        public int StudyId { get; set; }
    }

    public class ImageDto
    {
        public int Id { get; set; }
        public string SopInstanceUid { get; set; } = string.Empty;
        public int InstanceNumber { get; set; }
        public string ImagingMode { get; set; } = "BMode";
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public double MechanicalIndex { get; set; }
        public double ThermalIndex { get; set; }
        public double DepthCm { get; set; }
        public double FrequencyMHz { get; set; }
        public string? DicomFilePath { get; set; }
        public DateTime AcquisitionDateTimeUtc { get; set; }
        public int SeriesId { get; set; }
    }

    public class MeasurementDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string? Label { get; set; }
        public double? StartX { get; set; }
        public double? StartY { get; set; }
        public double? EndX { get; set; }
        public double? EndY { get; set; }
        public DateTime MeasuredAtUtc { get; set; }
        public int ImageId { get; set; }
    }

    public class SegmentationDto
    {
        public int Id { get; set; }
        public string Algorithm { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double? SeedX { get; set; }
        public double? SeedY { get; set; }
        public double AreaCm2 { get; set; }
        public double PerimeterCm { get; set; }
        public double ProcessingTimeMs { get; set; }
        public int ImageId { get; set; }
    }
}
