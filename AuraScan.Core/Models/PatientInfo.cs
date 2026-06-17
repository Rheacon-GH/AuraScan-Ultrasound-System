namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Patient demographic and study information for DICOM header population.
    /// </summary>
    public class PatientInfo
    {
        /// <summary>Patient ID (DICOM tag 0010,0020).</summary>
        public string PatientId { get; set; } = string.Empty;

        /// <summary>Patient name (DICOM tag 0010,0010).</summary>
        public string PatientName { get; set; } = string.Empty;

        /// <summary>Patient date of birth.</summary>
        public DateTime? DateOfBirth { get; set; }

        /// <summary>Patient sex (M/F/O).</summary>
        public string Sex { get; set; } = string.Empty;

        /// <summary>Patient weight in kg.</summary>
        public double? WeightKg { get; set; }

        /// <summary>Accession number for the study.</summary>
        public string AccessionNumber { get; set; } = string.Empty;

        /// <summary>Referring physician name.</summary>
        public string ReferringPhysician { get; set; } = string.Empty;

        /// <summary>Study description.</summary>
        public string StudyDescription { get; set; } = string.Empty;

        /// <summary>Performing physician.</summary>
        public string PerformingPhysician { get; set; } = string.Empty;

        /// <summary>Institution name.</summary>
        public string InstitutionName { get; set; } = string.Empty;

        /// <summary>Study instance UID (auto-generated if empty).</summary>
        public string StudyInstanceUid { get; set; } = string.Empty;

        /// <summary>Series instance UID (auto-generated if empty).</summary>
        public string SeriesInstanceUid { get; set; } = string.Empty;

        /// <summary>Study date/time.</summary>
        public DateTime StudyDateTime { get; set; } = DateTime.Now;
    }
}
