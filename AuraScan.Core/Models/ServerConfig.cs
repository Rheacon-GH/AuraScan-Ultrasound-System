namespace AuraScan_Ultrasound_System.Models
{
    public class ServerConfig
    {
        public string BaseUrl { get; set; } = "http://localhost:59675";
        public string ApiKey { get; set; } = "AuraScan-Dev-Key-12345";
        public string WorkstationId { get; set; } = Environment.MachineName;
        public bool AutoConnect { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 10;
    }
}
