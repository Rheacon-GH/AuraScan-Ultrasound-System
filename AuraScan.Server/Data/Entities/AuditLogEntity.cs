namespace AuraScan.Server.Data.Entities;

public class AuditLogEntity
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? UserId { get; set; }
    public string? WorkstationId { get; set; }
    public string? Details { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
