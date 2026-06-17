using AuraScan.Server.Data.Entities;
using AuraScan.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuraScan.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ConfigController : ControllerBase
{
    private readonly IConfigService _configService;
    private readonly IAuditService _audit;

    public ConfigController(IConfigService configService, IAuditService audit)
    {
        _configService = configService;
        _audit = audit;
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<string>> Get(string key)
    {
        var value = await _configService.GetAsync(key);
        return value == null ? NotFound() : Ok(value);
    }

    [HttpPut("{key}")]
    public async Task<IActionResult> Set(string key, [FromBody] ConfigSetRequest request)
    {
        await _configService.SetAsync(key, request.Value, request.Category, request.Description);
        await _audit.LogAsync("ConfigUpdated", "SystemConfig", details: $"Key={key}");
        return Ok();
    }

    [HttpGet("category/{category}")]
    public async Task<ActionResult<List<SystemConfigEntity>>> GetByCategory(string category)
    {
        return Ok(await _configService.GetByCategoryAsync(category));
    }

    [HttpGet("dicom-nodes")]
    public async Task<ActionResult<List<DicomNodeEntity>>> GetDicomNodes()
    {
        return Ok(await _configService.GetDicomNodesAsync());
    }

    [HttpPost("dicom-nodes")]
    public async Task<ActionResult<DicomNodeEntity>> SaveDicomNode([FromBody] DicomNodeEntity node)
    {
        var result = await _configService.SaveDicomNodeAsync(node);
        await _audit.LogAsync("DicomNodeSaved", "DicomNode", result.Id);
        return Ok(result);
    }

    [HttpDelete("dicom-nodes/{id:int}")]
    public async Task<IActionResult> DeleteDicomNode(int id)
    {
        var deleted = await _configService.DeleteDicomNodeAsync(id);
        if (!deleted) return NotFound();
        await _audit.LogAsync("DicomNodeDeleted", "DicomNode", id);
        return NoContent();
    }
}

public record ConfigSetRequest(string Value, string? Category = null, string? Description = null);
