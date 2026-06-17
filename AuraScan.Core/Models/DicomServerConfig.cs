namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// DICOM server connection configuration.
    /// </summary>
    public class DicomServerConfig
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 104;
        public string CallingAeTitle { get; set; } = "AURASCAN";
        public string CalledAeTitle { get; set; } = "PACS";
    }
}
