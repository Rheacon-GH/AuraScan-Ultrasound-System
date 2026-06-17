using System.ComponentModel.DataAnnotations;

namespace AuraScan.Server.Data.Entities;

public class ImageEntity
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string SopInstanceUid { get; set; } = string.Empty;

    public int InstanceNumber { get; set; }

    [Required, MaxLength(32)]
    public string ImagingMode { get; set; } = "BMode";

    [Range(1, 10000)]
    public int Width { get; set; }

    [Range(1, 10000)]
    public int Height { get; set; }

    [Range(0, 1000)]
    public double FrameRate { get; set; }

    [Range(0, 10)]
    public double MechanicalIndex { get; set; }

    [Range(0, 10)]
    public double ThermalIndex { get; set; }

    [Range(0, 100)]
    public double DepthCm { get; set; }

    [Range(0, 100)]
    public double FrequencyMHz { get; set; }

    [MaxLength(512)]
    public string? DicomFilePath { get; set; }

    [MaxLength(512)]
    public string? ThumbnailPath { get; set; }

    public DateTime AcquisitionDateTimeUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int SeriesId { get; set; }
    public SeriesEntity Series { get; set; } = null!;

    public ICollection<MeasurementEntity> Measurements { get; set; } = new List<MeasurementEntity>();
    public ICollection<SegmentationResultEntity> Segmentations { get; set; } = new List<SegmentationResultEntity>();
}
