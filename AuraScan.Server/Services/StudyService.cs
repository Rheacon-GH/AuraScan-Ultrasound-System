using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Services;

public class StudyService : IStudyService
{
    private readonly AuraScanDbContext _db;

    public StudyService(AuraScanDbContext db) => _db = db;

    public async Task<StudyEntity?> GetByIdAsync(int id) =>
        await _db.Studies.Include(s => s.Series).FirstOrDefaultAsync(s => s.Id == id);

    public async Task<StudyEntity?> GetByUidAsync(string studyInstanceUid) =>
        await _db.Studies.Include(s => s.Series).FirstOrDefaultAsync(s => s.StudyInstanceUid == studyInstanceUid);

    public async Task<List<StudyEntity>> GetByPatientAsync(int patientId) =>
        await _db.Studies.Where(s => s.PatientId == patientId)
            .Include(s => s.Series)
            .OrderByDescending(s => s.StudyDateTime)
            .ToListAsync();

    public async Task<StudyEntity> CreateAsync(StudyEntity study)
    {
        _db.Studies.Add(study);
        await _db.SaveChangesAsync();
        return study;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.Studies.FindAsync(id);
        if (entity == null) return false;
        _db.Studies.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }
}
