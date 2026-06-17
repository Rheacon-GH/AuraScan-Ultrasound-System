using System.ComponentModel.DataAnnotations;

namespace AuraScan.Server.Data.Entities;

public class PatientEntity
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string PatientId { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string PatientName { get; set; } = string.Empty;

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(16)]
    public string? Sex { get; set; }

    [Range(0, 500)]
    public double? WeightKg { get; set; }

    [MaxLength(64)]
    public string? AccessionNumber { get; set; }

    [MaxLength(256)]
    public string? ReferringPhysician { get; set; }

    [MaxLength(256)]
    public string? InstitutionName { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<StudyEntity> Studies { get; set; } = new List<StudyEntity>();
}
