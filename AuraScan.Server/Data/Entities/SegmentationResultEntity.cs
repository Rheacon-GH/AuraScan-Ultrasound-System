namespace AuraScan.Server.Data.Entities;

public class SegmentationResultEntity
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
    public string? MaskFilePath { get; set; }
    public string? ContourFilePath { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int ImageId { get; set; }
    public ImageEntity Image { get; set; } = null!;
}
