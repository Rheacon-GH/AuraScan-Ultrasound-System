using AuraScan.Server.Data.Entities;

namespace AuraScan.Server.Services;

public interface IAuditService
{
    Task LogAsync(string action, string? entityType = null, int? entityId = null, string? userId = null, string? workstationId = null, string? details = null);
    Task<List<AuditLogEntity>> GetLogsAsync(DateTime? from, DateTime? to, string? action, int skip = 0, int take = 100);
}
