using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// Low-level data transport abstraction for probe communication.
    /// Implementations handle the physical link (serial, TCP, etc.) while
    /// the probe driver handles protocol-level interpretation.
    /// </summary>
    public interface IProbeTransport : IDisposable
    {
        /// <summary>Whether the transport link is currently open.</summary>
        bool IsOpen { get; }

        /// <summary>Raised when the transport link drops unexpectedly.</summary>
        event EventHandler<string>? LinkLost;

        /// <summary>Open the transport link using the given configuration.</summary>
        Task<bool> OpenAsync(HardwareConnectionConfig config, CancellationToken ct = default);

        /// <summary>Close the transport link gracefully.</summary>
        Task CloseAsync(CancellationToken ct = default);

        /// <summary>
        /// Send a command packet to the probe.
        /// Commands are fixed-format binary packets per the probe protocol.
        /// </summary>
        Task SendCommandAsync(byte[] commandData, CancellationToken ct = default);

        /// <summary>
        /// Read an exact number of bytes from the probe.
        /// Blocks until all bytes are received or the cancellation token fires.
        /// </summary>
        Task<byte[]> ReadBytesAsync(int count, CancellationToken ct = default);

        /// <summary>
        /// Read a complete RF data frame from the high-speed data channel.
        /// Returns raw bytes representing interleaved scanline data.
        /// </summary>
        Task<byte[]> ReadFrameAsync(CancellationToken ct = default);

        /// <summary>Send a heartbeat/keep-alive ping and verify the response.</summary>
        Task<bool> PingAsync(CancellationToken ct = default);
    }
}
