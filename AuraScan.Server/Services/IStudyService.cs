using AuraScan.Server.Data.Entities;

namespace AuraScan.Server.Services;

public interface IStudyService
{
    Task<StudyEntity?> GetByIdAsync(int id);
    Task<StudyEntity?> GetByUidAsync(string studyInstanceUid);
    Task<List<StudyEntity>> GetByPatientAsync(int patientId);
    Task<StudyEntity> CreateAsync(StudyEntity study);
    Task<bool> DeleteAsync(int id);
}
