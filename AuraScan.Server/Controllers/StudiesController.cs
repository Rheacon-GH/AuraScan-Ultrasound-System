using AuraScan.Server.Data.Entities;
using AuraScan.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuraScan.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StudiesController : ControllerBase
{
    private readonly IStudyService _studyService;
    private readonly IAuditService _audit;

    public StudiesController(IStudyService studyService, IAuditService audit)
    {
        _studyService = studyService;
        _audit = audit;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StudyEntity>> GetById(int id)
    {
        var study = await _studyService.GetByIdAsync(id);
        return study == null ? NotFound() : Ok(study);
    }

    [HttpGet("by-uid/{uid}")]
    public async Task<ActionResult<StudyEntity>> GetByUid(string uid)
    {
        var study = await _studyService.GetByUidAsync(uid);
        return study == null ? NotFound() : Ok(study);
    }

    [HttpGet("by-patient/{patientId:int}")]
    public async Task<ActionResult<List<StudyEntity>>> GetByPatient(int patientId)
    {
        return Ok(await _studyService.GetByPatientAsync(patientId));
    }

    [HttpPost]
    public async Task<ActionResult<StudyEntity>> Create([FromBody] StudyEntity study)
    {
        var result = await _studyService.CreateAsync(study);
        await _audit.LogAsync("StudyCreated", "Study", result.Id);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _studyService.DeleteAsync(id);
        if (!deleted) return NotFound();
        await _audit.LogAsync("StudyDeleted", "Study", id);
        return NoContent();
    }
}
