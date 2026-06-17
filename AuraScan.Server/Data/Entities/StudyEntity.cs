using System.ComponentModel.DataAnnotations;

namespace AuraScan.Server.Data.Entities;

public class StudyEntity
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string StudyInstanceUid { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? StudyDescription { get; set; }

    public DateTime StudyDateTime { get; set; }

    [MaxLength(256)]
    public string? PerformingPhysician { get; set; }

    [MaxLength(64)]
    public string? AccessionNumber { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public int PatientId { get; set; }
    public PatientEntity Patient { get; set; } = null!;

    public ICollection<SeriesEntity> Series { get; set; } = new List<SeriesEntity>();
}
