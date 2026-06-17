using System.Security.Claims;
using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Services;

public class AuditService : IAuditService
{
    private readonly AuraScanDbContext _db;
    private readonly IHttpContextAccessor _httpContext;

    public AuditService(AuraScanDbContext db, IHttpContextAccessor httpContext)
    {
        _db = db;
        _httpContext = httpContext;
    }

    public async Task LogAsync(string action, string? entityType = null, int? entityId = null,
        string? userId = null, string? workstationId = null, string? details = null)
    {
        // Auto-populate identity from authenticated request if not explicitly provided
        var principal = _httpContext.HttpContext?.User;
        userId ??= principal?.FindFirst(ClaimTypes.Name)?.Value;
        workstationId ??= principal?.FindFirst("WorkstationId")?.Value;

        var clientIp = _httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (clientIp != null && details != null)
            details = $"{details} | IP={clientIp}";
        else if (clientIp != null)
            details = $"IP={clientIp}";

        _db.AuditLogs.Add(new AuditLogEntity
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId,
            WorkstationId = workstationId,
            Details = details,
            TimestampUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<AuditLogEntity>> GetLogsAsync(DateTime? from, DateTime? to, string? action,
        int skip = 0, int take = 100)
    {
        var query = _db.AuditLogs.AsQueryable();
        if (from.HasValue) query = query.Where(a => a.TimestampUtc >= from.Value);
        if (to.HasValue) query = query.Where(a => a.TimestampUtc <= to.Value);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        return await query.OrderByDescending(a => a.TimestampUtc).Skip(skip).Take(take).ToListAsync();
    }
}
