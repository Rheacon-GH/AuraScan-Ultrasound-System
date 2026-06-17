using System.IO;
using System.Net.Sockets;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// TCP/Ethernet transport for network-connected ultrasound probes.
    /// Uses a dual-socket architecture: a command channel for control packets
    /// and a separate high-speed data channel for RF frame streaming.
    /// </summary>
    public sealed class EthernetTransport : IProbeTransport
    {
        private TcpClient? _commandClient;
        private TcpClient? _dataClient;
        private NetworkStream? _commandStream;
        private NetworkStream? _dataStream;
        private bool _disposed;

        // Protocol constants (shared with UsbSerialTransport)
        private const byte FrameSyncByte1 = 0xAA;
        private const byte FrameSyncByte2 = 0x55;
        private const int FrameHeaderSize = 12;

        public bool IsOpen => _commandClient?.Connected == true;

        public event EventHandler<string>? LinkLost;

        public async Task<bool> OpenAsync(HardwareConnectionConfig config, CancellationToken ct = default)
        {
            try
            {
                // 1. Connect the command channel
                _commandClient = new TcpClient
                {
                    ReceiveBufferSize = 256 * 1024,
                    SendBufferSize = 64 * 1024,
                    NoDelay = true
                };

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(config.ConnectTimeoutMs);

                await _commandClient.ConnectAsync(config.IpAddress, config.CommandPort, connectCts.Token);
                _commandStream = _commandClient.GetStream();
                _commandStream.ReadTimeout = config.ReadTimeoutMs;
                _commandStream.WriteTimeout = config.WriteTimeoutMs;

                // 2. Connect the high-speed data channel
                _dataClient = new TcpClient
                {
                    ReceiveBufferSize = config.ReceiveBufferSize,
                    SendBufferSize = 64 * 1024,
                    NoDelay = true
                };

                await _dataClient.ConnectAsync(config.IpAddress, config.DataPort, connectCts.Token);
                _dataStream = _dataClient.GetStream();
                _dataStream.ReadTimeout = config.ReadTimeoutMs;

                return true;
            }
            catch (Exception ex)
            {
                LinkLost?.Invoke(this, $"Ethernet connection failed ({config.IpAddress}:{config.CommandPort}): {ex.Message}");
                await CloseAsync(ct);
                return false;
            }
        }

        public async Task CloseAsync(CancellationToken ct = default)
        {
            // Send a graceful disconnect command if possible
            if (_commandStream != null && _commandClient?.Connected == true)
            {
                try
                {
                    var disconnectFrame = BuildFrame(0x00FF, []); // DISCONNECT command
                    await _commandStream.WriteAsync(disconnectFrame, ct);
                    await _commandStream.FlushAsync(ct);
                }
                catch { /* best-effort */ }
            }

            _commandStream?.Dispose();
            _commandClient?.Dispose();
            _dataStream?.Dispose();
            _dataClient?.Dispose();

            _commandStream = null;
            _commandClient = null;
            _dataStream = null;
            _dataClient = null;
        }

        public async Task SendCommandAsync(byte[] commandData, CancellationToken ct = default)
        {
            if (_commandStream == null || _commandClient?.Connected != true)
                throw new InvalidOperationException("Command channel is not connected.");

            var frame = BuildFrame(0x0001, commandData);

            try
            {
                await _commandStream.WriteAsync(frame, ct);
                await _commandStream.FlushAsync(ct);
            }
            catch (IOException ex)
            {
                LinkLost?.Invoke(this, $"Command send failed: {ex.Message}");
                throw;
            }
        }

        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct = default)
        {
            if (_commandStream == null || _commandClient?.Connected != true)
                throw new InvalidOperationException("Command channel is not connected.");

            return await ReadExactAsync(_commandStream, count, ct);
        }

        public async Task<byte[]> ReadFrameAsync(CancellationToken ct = default)
        {
            if (_dataStream == null || _dataClient?.Connected != true)
                throw new InvalidOperationException("Data channel is not connected.");

            try
            {
                // 1. Find frame sync on the data channel
                await SynchronizeFrameAsync(_dataStream, ct);

                // 2. Read header remainder (10 bytes after 2-byte sync)
                var headerRest = await ReadExactAsync(_dataStream, FrameHeaderSize - 2, ct);
                int payloadLength = BitConverter.ToInt32(headerRest, 2);

                if (payloadLength <= 0 || payloadLength > 8 * 1024 * 1024)
                    throw new InvalidDataException($"Invalid RF frame payload length: {payloadLength}");

                // 3. Read payload
                return await ReadExactAsync(_dataStream, payloadLength, ct);
            }
            catch (IOException ex)
            {
                LinkLost?.Invoke(this, $"Data stream read failed: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> PingAsync(CancellationToken ct = default)
        {
            try
            {
                if (_commandStream == null || _commandClient?.Connected != true)
                    return false;

                var pingFrame = BuildFrame(0x0000, []);
                await _commandStream.WriteAsync(pingFrame, ct);
                await _commandStream.FlushAsync(ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(2000);
                var ack = await ReadExactAsync(_commandStream, 4, timeoutCts.Token);

                return ack[0] == FrameSyncByte1 && ack[1] == FrameSyncByte2 &&
                       ack[2] == 0x00 && ack[3] == 0x01;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
        {
            var buffer = new byte[count];
            int totalRead = 0;

            while (totalRead < count)
            {
                ct.ThrowIfCancellationRequested();
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);

                if (bytesRead == 0)
                    throw new IOException("Connection closed: zero-byte read from network stream.");

                totalRead += bytesRead;
            }

            return buffer;
        }

        private static async Task SynchronizeFrameAsync(NetworkStream stream, CancellationToken ct)
        {
            bool foundFirst = false;
            int maxScanBytes = 128 * 1024;
            int scanned = 0;
            var singleByte = new byte[1];

            while (scanned < maxScanBytes)
            {
                ct.ThrowIfCancellationRequested();
                int bytesRead = await stream.ReadAsync(singleByte, ct);
                if (bytesRead == 0)
                    throw new IOException("Connection closed during frame sync.");

                scanned++;

                if (!foundFirst)
                {
                    if (singleByte[0] == FrameSyncByte1)
                        foundFirst = true;
                }
                else
                {
                    if (singleByte[0] == FrameSyncByte2)
                        return;
                    foundFirst = singleByte[0] == FrameSyncByte1;
                }
            }

            throw new IOException("Frame sync pattern not found within scan window.");
        }

        private static byte[] BuildFrame(ushort commandType, byte[] payload)
        {
            var frame = new byte[FrameHeaderSize + payload.Length];
            frame[0] = FrameSyncByte1;
            frame[1] = FrameSyncByte2;
            BitConverter.TryWriteBytes(frame.AsSpan(2, 2), commandType);
            BitConverter.TryWriteBytes(frame.AsSpan(4, 4), payload.Length);
            BitConverter.TryWriteBytes(frame.AsSpan(8, 4), 0);
            if (payload.Length > 0)
                Buffer.BlockCopy(payload, 0, frame, FrameHeaderSize, payload.Length);
            return frame;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _commandStream?.Dispose();
            _commandClient?.Dispose();
            _dataStream?.Dispose();
            _dataClient?.Dispose();
        }
    }
}
