using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Services;

public class ConfigService : IConfigService
{
    private readonly AuraScanDbContext _db;

    public ConfigService(AuraScanDbContext db) => _db = db;

    public async Task<string?> GetAsync(string key)
    {
        var config = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
        return config?.Value;
    }

    public async Task SetAsync(string key, string value, string? category = null, string? description = null)
    {
        var existing = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.Key == key);
        if (existing != null)
        {
            existing.Value = value;
            existing.Category = category ?? existing.Category;
            existing.Description = description ?? existing.Description;
            existing.UpdatedUtc = DateTime.UtcNow;
        }
        else
        {
            _db.SystemConfigs.Add(new SystemConfigEntity
            {
                Key = key,
                Value = value,
                Category = category,
                Description = description
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<SystemConfigEntity>> GetByCategoryAsync(string category) =>
        await _db.SystemConfigs.Where(c => c.Category == category).ToListAsync();

    public async Task<List<DicomNodeEntity>> GetDicomNodesAsync() =>
        await _db.DicomNodes.OrderBy(d => d.Name).ToListAsync();

    public async Task<DicomNodeEntity> SaveDicomNodeAsync(DicomNodeEntity node)
    {
        if (node.Id == 0)
            _db.DicomNodes.Add(node);
        else
            _db.DicomNodes.Update(node);
        node.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return node;
    }

    public async Task<bool> DeleteDicomNodeAsync(int id)
    {
        var entity = await _db.DicomNodes.FindAsync(id);
        if (entity == null) return false;
        _db.DicomNodes.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }
}
