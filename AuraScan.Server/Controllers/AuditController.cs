using AuraScan.Server.Data.Entities;
using AuraScan.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuraScan.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly IAuditService _auditService;

    public AuditController(IAuditService auditService) => _auditService = auditService;

    [HttpGet]
    public async Task<ActionResult<List<AuditLogEntity>>> GetLogs(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? action,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        return Ok(await _auditService.GetLogsAsync(from, to, action, skip, take));
    }
}
