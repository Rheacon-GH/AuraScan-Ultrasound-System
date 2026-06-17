using AuraScan.Server.Data.Entities;
using AuraScan.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuraScan.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly IImageService _imageService;
    private readonly IAuditService _audit;

    public ImagesController(IImageService imageService, IAuditService audit)
    {
        _imageService = imageService;
        _audit = audit;
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ImageEntity>> GetById(int id)
    {
        var image = await _imageService.GetByIdAsync(id);
        return image == null ? NotFound() : Ok(image);
    }

    [HttpGet("by-sop/{sopUid}")]
    public async Task<ActionResult<ImageEntity>> GetBySopUid(string sopUid)
    {
        var image = await _imageService.GetBySopUidAsync(sopUid);
        return image == null ? NotFound() : Ok(image);
    }

    [HttpGet("by-series/{seriesId:int}")]
    public async Task<ActionResult<List<ImageEntity>>> GetBySeries(int seriesId)
    {
        return Ok(await _imageService.GetBySeriesAsync(seriesId));
    }

    [HttpPost]
    public async Task<ActionResult<ImageEntity>> Create([FromBody] ImageEntity image)
    {
        var result = await _imageService.CreateAsync(image);
        await _audit.LogAsync("ImageStored", "Image", result.Id);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _imageService.DeleteAsync(id);
        if (!deleted) return NotFound();
        await _audit.LogAsync("ImageDeleted", "Image", id);
        return NoContent();
    }
}
