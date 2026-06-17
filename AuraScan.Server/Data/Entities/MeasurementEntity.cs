namespace AuraScan.Server.Data.Entities;

public class MeasurementEntity
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
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int ImageId { get; set; }
    public ImageEntity Image { get; set; } = null!;
}
