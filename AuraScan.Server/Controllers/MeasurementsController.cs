using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using AuraScan.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class MeasurementsController : ControllerBase
{
    private readonly AuraScanDbContext _db;
    private readonly IAuditService _audit;

    public MeasurementsController(AuraScanDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("by-image/{imageId:int}")]
    public async Task<ActionResult<List<MeasurementEntity>>> GetByImage(int imageId)
    {
        return Ok(await _db.Measurements.Where(m => m.ImageId == imageId).ToListAsync());
    }

    [HttpPost]
    public async Task<ActionResult<MeasurementEntity>> Create([FromBody] MeasurementEntity measurement)
    {
        _db.Measurements.Add(measurement);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MeasurementSaved", "Measurement", measurement.Id);
        return Ok(measurement);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.Measurements.FindAsync(id);
        if (entity == null) return NotFound();
        _db.Measurements.Remove(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("MeasurementDeleted", "Measurement", id);
        return NoContent();
    }
}
