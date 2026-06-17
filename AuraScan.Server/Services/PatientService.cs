using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuraScan.Server.Services;

public class PatientService : IPatientService
{
    private readonly AuraScanDbContext _db;

    public PatientService(AuraScanDbContext db) => _db = db;

    public async Task<PatientEntity?> GetByIdAsync(int id) =>
        await _db.Patients.Include(p => p.Studies).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<PatientEntity?> GetByPatientIdAsync(string patientId) =>
        await _db.Patients.Include(p => p.Studies).FirstOrDefaultAsync(p => p.PatientId == patientId);

    public async Task<List<PatientEntity>> SearchAsync(string? name, string? patientId, int skip = 0, int take = 50)
    {
        var query = _db.Patients.AsQueryable();
        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(p => p.PatientName.Contains(name));
        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(p => p.PatientId.Contains(patientId));
        return await query.OrderByDescending(p => p.UpdatedUtc).Skip(skip).Take(take).ToListAsync();
    }

    public async Task<PatientEntity> CreateOrUpdateAsync(PatientEntity patient)
    {
        var existing = await _db.Patients.FirstOrDefaultAsync(p => p.PatientId == patient.PatientId);
        if (existing != null)
        {
            existing.PatientName = patient.PatientName;
            existing.DateOfBirth = patient.DateOfBirth;
            existing.Sex = patient.Sex;
            existing.WeightKg = patient.WeightKg;
            existing.AccessionNumber = patient.AccessionNumber;
            existing.ReferringPhysician = patient.ReferringPhysician;
            existing.InstitutionName = patient.InstitutionName;
            existing.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return existing;
        }

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();
        return patient;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.Patients.FindAsync(id);
        if (entity == null) return false;
        _db.Patients.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }
}
