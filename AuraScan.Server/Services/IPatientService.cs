using AuraScan.Server.Data.Entities;

namespace AuraScan.Server.Services;

public interface IPatientService
{
    Task<PatientEntity?> GetByIdAsync(int id);
    Task<PatientEntity?> GetByPatientIdAsync(string patientId);
    Task<List<PatientEntity>> SearchAsync(string? name, string? patientId, int skip = 0, int take = 50);
    Task<PatientEntity> CreateOrUpdateAsync(PatientEntity patient);
    Task<bool> DeleteAsync(int id);
}
