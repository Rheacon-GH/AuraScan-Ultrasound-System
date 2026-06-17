using System.IO;
using System.IO.Ports;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// USB/serial transport for ultrasound probe communication.
    /// Uses high-speed serial (up to 12 Mbaud) for probe command/data channels.
    /// The probe presents as a virtual COM port when connected via USB.
    /// </summary>
    public sealed class UsbSerialTransport : IProbeTransport
    {
        private SerialPort? _port;
        private int _readTimeoutMs = 2000;
        private readonly byte[] _readBuffer;
        private bool _disposed;

        // Protocol frame markers
        private const byte FrameSyncByte1 = 0xAA;
        private const byte FrameSyncByte2 = 0x55;
        private const int FrameHeaderSize = 12; // sync(2) + type(2) + length(4) + seqNum(4)

        public bool IsOpen => _port?.IsOpen == true;

        public event EventHandler<string>? LinkLost;

        public UsbSerialTransport()
        {
            _readBuffer = new byte[4 * 1024 * 1024]; // 4 MB buffer for RF frame data
        }

        public Task<bool> OpenAsync(HardwareConnectionConfig config, CancellationToken ct = default)
        {
            try
            {
                _readTimeoutMs = config.ReadTimeoutMs;

                _port = new SerialPort
                {
                    PortName = config.SerialPortName,
                    BaudRate = config.BaudRate,
                    DataBits = config.DataBits,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    ReadTimeout = config.ReadTimeoutMs,
                    WriteTimeout = config.WriteTimeoutMs,
                    ReadBufferSize = config.ReceiveBufferSize,
                    WriteBufferSize = 64 * 1024, // 64 KB write buffer
                    DtrEnable = true,
                    RtsEnable = true
                };

                _port.ErrorReceived += OnSerialError;
                _port.Open();

                // Flush any stale data in the receive buffer
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                LinkLost?.Invoke(this, $"USB/Serial open failed on {config.SerialPortName}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task CloseAsync(CancellationToken ct = default)
        {
            if (_port?.IsOpen == true)
            {
                try
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                    _port.Close();
                }
                catch { /* swallow close errors */ }
            }
            return Task.CompletedTask;
        }

        public async Task SendCommandAsync(byte[] commandData, CancellationToken ct = default)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("Serial port is not open.");

            // Wrap command in a protocol frame: [sync1][sync2][type=CMD][length][data]
            var frame = BuildFrame(0x0001, commandData);
            await _port.BaseStream.WriteAsync(frame, ct);
            await _port.BaseStream.FlushAsync(ct);
        }

        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct = default)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("Serial port is not open.");

            var buffer = new byte[count];
            int totalRead = 0;

            while (totalRead < count)
            {
                ct.ThrowIfCancellationRequested();

                int bytesRead = await _port.BaseStream.ReadAsync(
                    buffer.AsMemory(totalRead, count - totalRead), ct);

                if (bytesRead == 0)
                {
                    LinkLost?.Invoke(this, "Serial connection lost: zero-byte read.");
                    throw new IOException("Serial port returned zero bytes — connection lost.");
                }

                totalRead += bytesRead;
            }

            return buffer;
        }

        public async Task<byte[]> ReadFrameAsync(CancellationToken ct = default)
        {
            if (_port?.IsOpen != true)
                throw new InvalidOperationException("Serial port is not open.");

            // 1. Synchronize: find the frame sync pattern (0xAA 0x55)
            await SynchronizeFrameAsync(ct);

            // 2. Read the rest of the header (type + length + seqNum = 10 bytes)
            var headerRest = await ReadBytesAsync(FrameHeaderSize - 2, ct);

            // Parse payload length (bytes 4-7 of full header → bytes 2-5 of headerRest)
            int payloadLength = BitConverter.ToInt32(headerRest, 2);

            if (payloadLength <= 0 || payloadLength > _readBuffer.Length)
                throw new InvalidDataException($"Invalid frame payload length: {payloadLength}");

            // 3. Read the payload
            var payload = await ReadBytesAsync(payloadLength, ct);

            return payload;
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                if (_port?.IsOpen != true)
                    return false;

                // Send a heartbeat command (type 0x0000 = PING, empty payload)
                var pingFrame = BuildFrame(0x0000, []);
                await _port.BaseStream.WriteAsync(pingFrame, ct);
                await _port.BaseStream.FlushAsync(ct);

                // Read 4-byte ACK response within timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_readTimeoutMs);
                var ack = await ReadBytesAsync(4, timeoutCts.Token);

                return ack[0] == FrameSyncByte1 && ack[1] == FrameSyncByte2 && ack[2] == 0x00 && ack[3] == 0x01;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Enumerate available serial ports on the system.
        /// </summary>
        public static string[] GetAvailablePorts() => SerialPort.GetPortNames();

        private async Task SynchronizeFrameAsync(CancellationToken ct)
        {
            bool foundFirst = false;
            int maxScanBytes = 64 * 1024; // scan up to 64 KB before giving up
            int scanned = 0;

            while (scanned < maxScanBytes)
            {
                ct.ThrowIfCancellationRequested();
                var b = await ReadBytesAsync(1, ct);
                scanned++;

                if (!foundFirst)
                {
                    if (b[0] == FrameSyncByte1)
                        foundFirst = true;
                }
                else
                {
                    if (b[0] == FrameSyncByte2)
                        return; // sync found

                    // False positive — restart scan
                    foundFirst = b[0] == FrameSyncByte1;
                }
            }

            throw new IOException("Failed to synchronize: frame sync pattern not found within scan window.");
        }

        private static byte[] BuildFrame(ushort commandType, byte[] payload)
        {
            var frame = new byte[FrameHeaderSize + payload.Length];
            frame[0] = FrameSyncByte1;
            frame[1] = FrameSyncByte2;
            BitConverter.TryWriteBytes(frame.AsSpan(2, 2), commandType);
            BitConverter.TryWriteBytes(frame.AsSpan(4, 4), payload.Length);
            BitConverter.TryWriteBytes(frame.AsSpan(8, 4), 0); // sequence number placeholder
            if (payload.Length > 0)
                Buffer.BlockCopy(payload, 0, frame, FrameHeaderSize, payload.Length);
            return frame;
        }

        private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
        {
            LinkLost?.Invoke(this, $"Serial error: {e.EventType}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_port?.IsOpen == true)
            {
                try { _port.Close(); } catch { }
            }
            _port?.Dispose();
        }
    }
}
