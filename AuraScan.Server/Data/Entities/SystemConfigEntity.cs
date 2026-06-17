namespace AuraScan.Server.Data.Entities;

public class SystemConfigEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
