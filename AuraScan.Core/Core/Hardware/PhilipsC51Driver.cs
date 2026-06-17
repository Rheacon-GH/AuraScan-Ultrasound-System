using System.Diagnostics;
using System.IO;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// Real hardware driver for the Philips C5-1 convex ultrasound probe.
    /// Communicates with the probe via an <see cref="IProbeTransport"/> (USB or Ethernet)
    /// using a binary command/response protocol to control acquisition and read RF data.
    /// </summary>
    public sealed class PhilipsC51Driver : IUltrasoundProbe
    {
        private readonly IProbeTransport _transport;
        private readonly HardwareConnectionConfig _config;
        private readonly Stopwatch _fpsTimer = new();
        private ScanParameters _currentParams = new();
        private long _frameCounter;
        private int _frameCount;
        private double _frameRateHz;
        private CancellationTokenSource? _heartbeatCts;
        private bool _disposed;

        // ── Protocol command IDs ──
        private static class ProbeCommand
        {
            public const byte Identify         = 0x01;
            public const byte SetMode          = 0x10;
            public const byte SetDepth         = 0x11;
            public const byte SetFrequency     = 0x12;
            public const byte SetGain          = 0x13;
            public const byte SetTgc           = 0x14;
            public const byte SetFocus         = 0x15;
            public const byte SetDoppler       = 0x16;
            public const byte SetPower         = 0x17;
            public const byte StartAcquisition = 0x20;
            public const byte StopAcquisition  = 0x21;
            public const byte SingleFrame      = 0x22;
            public const byte DopplerEnsemble  = 0x23;
        }

        public ProbeConfiguration Configuration { get; }
        public bool IsConnected { get; private set; }
        public bool IsAcquiring { get; private set; }

        public event EventHandler<UltrasoundFrame>? FrameAcquired;
        public event EventHandler<string>? ErrorOccurred;

        public PhilipsC51Driver(IProbeTransport transport, HardwareConnectionConfig config)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _transport.LinkLost += OnLinkLost;

            // C5-1 probe specifications
            Configuration = new ProbeConfiguration
            {
                ModelName = "C5-1",
                Manufacturer = "Philips",
                ProbeType = ProbeType.Convex,
                ElementCount = 128,
                CenterFrequencyHz = 3.0e6,
                BandwidthLowHz = 1.0e6,
                BandwidthHighHz = 5.0e6,
                ElementPitchM = 0.000480,
                ConvexRadiusM = 0.060,
                FieldOfViewDegrees = 70.0,
                MaxDepthM = 0.30,
                SpeedOfSoundMps = 1540.0,
                SamplingFrequencyHz = 40.0e6,
                SamplesPerLine = 4096,
                ScanlinesPerFrame = 256,
                FocalZones = 2
            };
        }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                bool opened = await _transport.OpenAsync(_config, ct);
                if (!opened)
                {
                    ErrorOccurred?.Invoke(this, "Transport failed to open.");
                    return false;
                }

                // Handshake: send IDENTIFY, expect probe info response
                var identifyCmd = BuildCommand(ProbeCommand.Identify);
                await _transport.SendCommandAsync(identifyCmd, ct);
                var response = await _transport.ReadBytesAsync(32, ct);

                if (!ValidateIdentifyResponse(response))
                {
                    ErrorOccurred?.Invoke(this, "Probe identification failed — unexpected response.");
                    await _transport.CloseAsync(ct);
                    return false;
                }

                IsConnected = true;
                StartHeartbeat();

                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            StopHeartbeat();

            if (IsAcquiring)
                await StopAcquisitionAsync(ct);

            await _transport.CloseAsync(ct);
            IsConnected = false;
        }

        public async Task StartAcquisitionAsync(ScanParameters parameters, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Probe is not connected.");

            _currentParams = parameters;
            _frameCounter = 0;
            _frameCount = 0;
            _fpsTimer.Restart();

            // Configure all acquisition parameters on the probe
            await SendAllParametersAsync(parameters, ct);

            // Issue the START command
            var startCmd = BuildCommand(ProbeCommand.StartAcquisition);
            await _transport.SendCommandAsync(startCmd, ct);

            IsAcquiring = true;
        }

        public async Task StopAcquisitionAsync(CancellationToken ct = default)
        {
            if (!IsAcquiring) return;

            try
            {
                var stopCmd = BuildCommand(ProbeCommand.StopAcquisition);
                await _transport.SendCommandAsync(stopCmd, ct);
            }
            catch { /* best-effort stop */ }

            IsAcquiring = false;
            _fpsTimer.Stop();
        }

        public async Task UpdateParametersAsync(ScanParameters parameters, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Probe is not connected.");

            _currentParams = parameters;
            await SendAllParametersAsync(parameters, ct);
        }

        public async Task<double[][]> AcquireFrameAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Probe is not connected.");

            // Request a single frame from the probe
            var frameCmd = BuildCommand(ProbeCommand.SingleFrame);
            await _transport.SendCommandAsync(frameCmd, ct);

            // Read the raw RF data frame
            var rawData = await _transport.ReadFrameAsync(ct);

            // Parse binary RF data into double[][] format
            var rfData = ParseRfFrame(rawData);

            // Update frame rate tracking
            _frameCounter++;
            _frameCount++;
            double elapsed = _fpsTimer.Elapsed.TotalSeconds;
            if (elapsed > 0.5)
            {
                _frameRateHz = _frameCount / elapsed;
                _frameCount = 0;
                _fpsTimer.Restart();
            }

            return rfData;
        }

        public async Task<double[][][]> AcquireDopplerEnsembleAsync(int ensembleLength, CancellationToken ct = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Probe is not connected.");

            // Request a Doppler ensemble from the probe
            var payload = new byte[4];
            BitConverter.TryWriteBytes(payload, ensembleLength);
            var ensembleCmd = BuildCommand(ProbeCommand.DopplerEnsemble, payload);
            await _transport.SendCommandAsync(ensembleCmd, ct);

            // Read each frame in the ensemble
            var ensemble = new double[ensembleLength][][];
            for (int i = 0; i < ensembleLength; i++)
            {
                ct.ThrowIfCancellationRequested();
                var rawData = await _transport.ReadFrameAsync(ct);
                ensemble[i] = ParseRfFrame(rawData);
            }

            return ensemble;
        }

        // ── Protocol helpers ──

        private async Task SendAllParametersAsync(ScanParameters p, CancellationToken ct)
        {
            // SET_MODE: 1 byte mode enum
            await _transport.SendCommandAsync(BuildCommand(ProbeCommand.SetMode, [(byte)p.Mode]), ct);

            // SET_DEPTH: 8-byte double (meters)
            await _transport.SendCommandAsync(
                BuildCommand(ProbeCommand.SetDepth, BitConverter.GetBytes(p.DepthM)), ct);

            // SET_FREQUENCY: 8-byte double (Hz)
            await _transport.SendCommandAsync(
                BuildCommand(ProbeCommand.SetFrequency, BitConverter.GetBytes(p.TransmitFrequencyHz)), ct);

            // SET_GAIN: 8-byte double (dB)
            await _transport.SendCommandAsync(
                BuildCommand(ProbeCommand.SetGain, BitConverter.GetBytes(p.GainDb)), ct);

            // SET_TGC: 8 × 8-byte doubles = 64 bytes
            var tgcPayload = new byte[p.TgcCurve.Length * 8];
            for (int i = 0; i < p.TgcCurve.Length; i++)
                BitConverter.TryWriteBytes(tgcPayload.AsSpan(i * 8, 8), p.TgcCurve[i]);
            await _transport.SendCommandAsync(BuildCommand(ProbeCommand.SetTgc, tgcPayload), ct);

            // SET_FOCUS: N × 8-byte doubles
            var focusPayload = new byte[p.FocalDepthsM.Length * 8];
            for (int i = 0; i < p.FocalDepthsM.Length; i++)
                BitConverter.TryWriteBytes(focusPayload.AsSpan(i * 8, 8), p.FocalDepthsM[i]);
            await _transport.SendCommandAsync(BuildCommand(ProbeCommand.SetFocus, focusPayload), ct);

            // SET_POWER: 8-byte double (0.0 – 1.0)
            await _transport.SendCommandAsync(
                BuildCommand(ProbeCommand.SetPower, BitConverter.GetBytes(p.TransmitPower)), ct);

            // SET_DOPPLER: PRF(8) + WallFilter(8) + EnsembleLen(4) + FftSize(4) + AngleCorr(8) = 32 bytes
            var dopplerPayload = new byte[32];
            BitConverter.TryWriteBytes(dopplerPayload.AsSpan(0, 8), p.DopplerPrfHz);
            BitConverter.TryWriteBytes(dopplerPayload.AsSpan(8, 8), p.DopplerWallFilterHz);
            BitConverter.TryWriteBytes(dopplerPayload.AsSpan(16, 4), p.ColorEnsembleLength);
            BitConverter.TryWriteBytes(dopplerPayload.AsSpan(20, 4), p.SpectralFftSize);
            BitConverter.TryWriteBytes(dopplerPayload.AsSpan(24, 8), p.DopplerAngleCorrectionDeg);
            await _transport.SendCommandAsync(BuildCommand(ProbeCommand.SetDoppler, dopplerPayload), ct);
        }

        /// <summary>
        /// Parse raw binary RF data into the double[scanline][sample] format.
        /// Probe transmits 16-bit signed integers at 40 MHz sampling.
        /// Frame layout: [scanlineCount(4)][samplesPerLine(4)][data: int16 × scanlines × samples]
        /// </summary>
        private double[][] ParseRfFrame(byte[] rawData)
        {
            if (rawData.Length < 8)
                throw new InvalidDataException("RF frame too small — missing header.");

            int scanlineCount = BitConverter.ToInt32(rawData, 0);
            int samplesPerLine = BitConverter.ToInt32(rawData, 4);

            int expectedDataBytes = scanlineCount * samplesPerLine * 2; // 16-bit samples
            if (rawData.Length < 8 + expectedDataBytes)
                throw new InvalidDataException(
                    $"RF frame truncated: expected {8 + expectedDataBytes} bytes, got {rawData.Length}.");

            var rfData = new double[scanlineCount][];
            int offset = 8;

            for (int line = 0; line < scanlineCount; line++)
            {
                rfData[line] = new double[samplesPerLine];
                for (int s = 0; s < samplesPerLine; s++)
                {
                    short raw = BitConverter.ToInt16(rawData, offset);
                    rfData[line][s] = raw / 32768.0; // Normalize to [-1.0, 1.0]
                    offset += 2;
                }
            }

            return rfData;
        }

        private static byte[] BuildCommand(byte commandId, byte[]? payload = null)
        {
            payload ??= [];
            var cmd = new byte[1 + payload.Length];
            cmd[0] = commandId;
            if (payload.Length > 0)
                Buffer.BlockCopy(payload, 0, cmd, 1, payload.Length);
            return cmd;
        }

        private static bool ValidateIdentifyResponse(byte[] response)
        {
            // Expected: "PHILIPS_C51\0" at offset 0, protocol version at offset 16
            if (response.Length < 20) return false;

            // Check probe signature
            string signature = System.Text.Encoding.ASCII.GetString(response, 0, 11);
            return signature == "PHILIPS_C51";
        }

        // ── Heartbeat monitoring ──

        private void StartHeartbeat()
        {
            _heartbeatCts = new CancellationTokenSource();
            var ct = _heartbeatCts.Token;

            _ = Task.Run(async () =>
            {
                int missedCount = 0;
                while (!ct.IsCancellationRequested && IsConnected)
                {
                    try
                    {
                        await Task.Delay(_config.HeartbeatIntervalMs, ct);
                        bool alive = await _transport.PingAsync(ct);

                        if (alive)
                        {
                            missedCount = 0;
                        }
                        else
                        {
                            missedCount++;
                            if (missedCount >= _config.MaxMissedHeartbeats)
                            {
                                OnLinkLost(this, $"Heartbeat lost ({missedCount} consecutive misses).");
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        missedCount++;
                        ErrorOccurred?.Invoke(this, $"Heartbeat error: {ex.Message}");
                        if (missedCount >= _config.MaxMissedHeartbeats)
                        {
                            OnLinkLost(this, "Heartbeat lost due to repeated errors.");
                            break;
                        }
                    }
                }
            }, ct);
        }

        private void StopHeartbeat()
        {
            _heartbeatCts?.Cancel();
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;
        }

        private async void OnLinkLost(object? sender, string reason)
        {
            IsAcquiring = false;
            IsConnected = false;
            ErrorOccurred?.Invoke(this, $"Link lost: {reason}");

            if (_config.AutoReconnect)
            {
                ErrorOccurred?.Invoke(this, "Attempting auto-reconnect...");

                for (int attempt = 1;
                     _config.MaxReconnectAttempts == 0 || attempt <= _config.MaxReconnectAttempts;
                     attempt++)
                {
                    try
                    {
                        await Task.Delay(_config.ReconnectDelayMs);
                        bool reconnected = await ConnectAsync();
                        if (reconnected)
                        {
                            ErrorOccurred?.Invoke(this, $"Reconnected on attempt {attempt}.");
                            return;
                        }
                    }
                    catch { /* retry */ }
                }

                ErrorOccurred?.Invoke(this, "Auto-reconnect exhausted all attempts.");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopHeartbeat();
            IsAcquiring = false;
            IsConnected = false;
            _transport.Dispose();
        }
    }
}
