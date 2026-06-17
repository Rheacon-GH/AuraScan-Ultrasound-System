using AuraScan.Server.Data.Entities;

namespace AuraScan.Server.Services;

public interface IImageService
{
    Task<ImageEntity?> GetByIdAsync(int id);
    Task<ImageEntity?> GetBySopUidAsync(string sopInstanceUid);
    Task<List<ImageEntity>> GetBySeriesAsync(int seriesId);
    Task<ImageEntity> CreateAsync(ImageEntity image);
    Task<bool> DeleteAsync(int id);
}
