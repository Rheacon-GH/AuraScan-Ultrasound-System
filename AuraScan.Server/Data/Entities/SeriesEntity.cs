namespace AuraScan.Server.Data.Entities;

public class SeriesEntity
{
    public int Id { get; set; }
    public string SeriesInstanceUid { get; set; } = string.Empty;
    public string? SeriesDescription { get; set; }
    public string? Modality { get; set; } = "US";
    public int SeriesNumber { get; set; }
    public string? BodyPartExamined { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int StudyId { get; set; }
    public StudyEntity Study { get; set; } = null!;

    public ICollection<ImageEntity> Images { get; set; } = new List<ImageEntity>();
}
