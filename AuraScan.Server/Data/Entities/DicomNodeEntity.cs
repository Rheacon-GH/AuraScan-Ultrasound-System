using System.ComponentModel.DataAnnotations;

namespace AuraScan.Server.Data.Entities;

public class DicomNodeEntity
{
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(16)]
    public string AeTitle { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 104;

    [Required, MaxLength(16)]
    public string NodeType { get; set; } = "SCP";

    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
