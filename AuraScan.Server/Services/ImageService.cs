using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Services;

public class ImageService : IImageService
{
    private readonly AuraScanDbContext _db;

    public ImageService(AuraScanDbContext db) => _db = db;

    public async Task<ImageEntity?> GetByIdAsync(int id) =>
        await _db.Images
            .Include(i => i.Measurements)
            .Include(i => i.Segmentations)
            .FirstOrDefaultAsync(i => i.Id == id);

    public async Task<ImageEntity?> GetBySopUidAsync(string sopInstanceUid) =>
        await _db.Images.FirstOrDefaultAsync(i => i.SopInstanceUid == sopInstanceUid);

    public async Task<List<ImageEntity>> GetBySeriesAsync(int seriesId) =>
        await _db.Images.Where(i => i.SeriesId == seriesId)
            .OrderBy(i => i.InstanceNumber)
            .ToListAsync();

    public async Task<ImageEntity> CreateAsync(ImageEntity image)
    {
        _db.Images.Add(image);
        await _db.SaveChangesAsync();
        return image;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.Images.FindAsync(id);
        if (entity == null) return false;
        _db.Images.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }
}
