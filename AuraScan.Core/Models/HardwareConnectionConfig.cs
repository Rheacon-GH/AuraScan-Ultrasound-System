namespace AuraScan_Ultrasound_System.Models
{
    /// <summary>
    /// Defines how the system connects to an ultrasound probe.
    /// </summary>
    public enum ProbeConnectionType
    {
        /// <summary>Software simulator — no hardware required.</summary>
        Simulator,
        /// <summary>USB/serial connection via COM port.</summary>
        UsbSerial,
        /// <summary>TCP/IP Ethernet connection.</summary>
        Ethernet
    }

    /// <summary>
    /// Connection-level health state of the probe link.
    /// </summary>
    public enum ProbeConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Error,
        Reconnecting
    }

    /// <summary>
    /// Configuration for probe hardware connection — transport type, address, and timing.
    /// </summary>
    public class HardwareConnectionConfig
    {
        /// <summary>Transport type to use for probe communication.</summary>
        public ProbeConnectionType ConnectionType { get; set; } = ProbeConnectionType.Simulator;

        // ── USB / Serial settings ──

        /// <summary>COM port name (e.g., "COM3").</summary>
        public string SerialPortName { get; set; } = "";

        /// <summary>Baud rate for the serial link.</summary>
        public int BaudRate { get; set; } = 12_000_000;

        /// <summary>Data bits per serial frame.</summary>
        public int DataBits { get; set; } = 8;

        /// <summary>USB Vendor ID for auto-discovery (Philips Medical = 0x0471).</summary>
        public int UsbVendorId { get; set; } = 0x0471;

        /// <summary>USB Product ID for the C5-1 probe.</summary>
        public int UsbProductId { get; set; } = 0x0C51;

        // ── Ethernet / TCP settings ──

        /// <summary>IP address or hostname of the probe's Ethernet interface.</summary>
        public string IpAddress { get; set; } = "192.168.1.100";

        /// <summary>TCP port for the probe's command channel.</summary>
        public int CommandPort { get; set; } = 18944;

        /// <summary>TCP port for the probe's high-speed RF data stream.</summary>
        public int DataPort { get; set; } = 18945;

        // ── Common timing settings ──

        /// <summary>Timeout for initial connection handshake (ms).</summary>
        public int ConnectTimeoutMs { get; set; } = 5000;

        /// <summary>Timeout for individual read operations (ms).</summary>
        public int ReadTimeoutMs { get; set; } = 2000;

        /// <summary>Timeout for individual write operations (ms).</summary>
        public int WriteTimeoutMs { get; set; } = 1000;

        /// <summary>Interval between heartbeat/keep-alive pings (ms).</summary>
        public int HeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>Number of consecutive missed heartbeats before declaring a disconnect.</summary>
        public int MaxMissedHeartbeats { get; set; } = 3;

        /// <summary>Whether to attempt automatic reconnection on link loss.</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>Delay between reconnect attempts (ms).</summary>
        public int ReconnectDelayMs { get; set; } = 2000;

        /// <summary>Maximum number of reconnect attempts (0 = unlimited).</summary>
        public int MaxReconnectAttempts { get; set; } = 10;

        /// <summary>Size of the receive buffer in bytes.</summary>
        public int ReceiveBufferSize { get; set; } = 4 * 1024 * 1024; // 4 MB
    }
}
