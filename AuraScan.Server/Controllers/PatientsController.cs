using AuraScan.Server.Data.Entities;
using AuraScan.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuraScan.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly IPatientService _patientService;
    private readonly IAuditService _audit;

    public PatientsController(IPatientService patientService, IAuditService audit)
    {
        _patientService = patientService;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<List<PatientEntity>>> Search([FromQuery] string? name, [FromQuery] string? patientId, [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var results = await _patientService.SearchAsync(name, patientId, skip, take);
        return Ok(results);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PatientEntity>> GetById(int id)
    {
        var patient = await _patientService.GetByIdAsync(id);
        return patient == null ? NotFound() : Ok(patient);
    }

    [HttpGet("by-patient-id/{patientId}")]
    public async Task<ActionResult<PatientEntity>> GetByPatientId(string patientId)
    {
        var patient = await _patientService.GetByPatientIdAsync(patientId);
        return patient == null ? NotFound() : Ok(patient);
    }

    [HttpPost]
    public async Task<ActionResult<PatientEntity>> CreateOrUpdate([FromBody] PatientEntity patient)
    {
        var result = await _patientService.CreateOrUpdateAsync(patient);
        await _audit.LogAsync("PatientSaved", "Patient", result.Id);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _patientService.DeleteAsync(id);
        if (!deleted) return NotFound();
        await _audit.LogAsync("PatientDeleted", "Patient", id);
        return NoContent();
    }
}
