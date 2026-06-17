using AuraScan.Server.Data.Entities;

namespace AuraScan.Server.Services;

public interface IConfigService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value, string? category = null, string? description = null);
    Task<List<SystemConfigEntity>> GetByCategoryAsync(string category);
    Task<List<DicomNodeEntity>> GetDicomNodesAsync();
    Task<DicomNodeEntity> SaveDicomNodeAsync(DicomNodeEntity node);
    Task<bool> DeleteDicomNodeAsync(int id);
}
