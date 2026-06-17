using System.IO.Ports;
using System.Net.Sockets;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// Manages probe discovery, creation, connection lifecycle, and health monitoring.
    /// Acts as the single entry point for obtaining a ready-to-use <see cref="IUltrasoundProbe"/>.
    /// </summary>
    public sealed class ProbeManager : IDisposable
    {
        private IUltrasoundProbe? _activeProbe;
        private IProbeTransport? _activeTransport;
        private HardwareConnectionConfig _config = new();
        private bool _disposed;

        /// <summary>Raised when the connection state changes.</summary>
        public event EventHandler<ProbeConnectionState>? ConnectionStateChanged;

        /// <summary>Raised on errors or informational status messages.</summary>
        public event EventHandler<string>? StatusMessage;

        /// <summary>The currently active probe instance, or null.</summary>
        public IUltrasoundProbe? ActiveProbe => _activeProbe;

        /// <summary>Current connection state.</summary>
        public ProbeConnectionState ConnectionState { get; private set; } = ProbeConnectionState.Disconnected;

        /// <summary>Current configuration.</summary>
        public HardwareConnectionConfig Config => _config;

        /// <summary>
        /// Update the connection configuration. Must be disconnected first.
        /// </summary>
        public void SetConfig(HardwareConnectionConfig config)
        {
            if (ConnectionState != ProbeConnectionState.Disconnected)
                throw new InvalidOperationException("Disconnect before changing configuration.");
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Create and connect a probe using the current configuration.
        /// Returns the probe instance on success, or null on failure.
        /// </summary>
        public async Task<IUltrasoundProbe?> ConnectAsync(CancellationToken ct = default)
        {
            // Clean up any previous connection
            await DisconnectAsync(ct);

            SetState(ProbeConnectionState.Connecting);

            try
            {
                switch (_config.ConnectionType)
                {
                    case ProbeConnectionType.Simulator:
                        return ConnectSimulator();

                    case ProbeConnectionType.UsbSerial:
                        return await ConnectUsbSerialAsync(ct);

                    case ProbeConnectionType.Ethernet:
                        return await ConnectEthernetAsync(ct);

                    default:
                        StatusMessage?.Invoke(this, $"Unknown connection type: {_config.ConnectionType}");
                        SetState(ProbeConnectionState.Error);
                        return null;
                }
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Connection failed: {ex.Message}");
                SetState(ProbeConnectionState.Error);
                return null;
            }
        }

        /// <summary>
        /// Disconnect and dispose the current probe and transport.
        /// </summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            if (_activeProbe != null)
            {
                try
                {
                    if (_activeProbe.IsAcquiring)
                        await _activeProbe.StopAcquisitionAsync(ct);

                    if (_activeProbe.IsConnected)
                        await _activeProbe.DisconnectAsync(ct);
                }
                catch { /* best-effort */ }

                _activeProbe.Dispose();
                _activeProbe = null;
            }

            if (_activeTransport != null)
            {
                try { await _activeTransport.CloseAsync(ct); } catch { }
                _activeTransport.Dispose();
                _activeTransport = null;
            }

            SetState(ProbeConnectionState.Disconnected);
        }

        // ── Discovery ──

        /// <summary>
        /// Discover available probe connections on the system.
        /// Returns a list of discovered endpoints with their connection type.
        /// </summary>
        public async Task<List<DiscoveredProbe>> DiscoverAsync(CancellationToken ct = default)
        {
            var results = new List<DiscoveredProbe>();

            // 1. Serial/USB ports
            StatusMessage?.Invoke(this, "Scanning serial ports...");
            try
            {
                string[] ports = SerialPort.GetPortNames();
                foreach (string portName in ports)
                {
                    ct.ThrowIfCancellationRequested();
                    var probed = await ProbeSerialPortAsync(portName, ct);
                    if (probed != null)
                        results.Add(probed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage?.Invoke(this, $"Serial scan error: {ex.Message}");
            }

            // 2. Network scan on common subnets
            StatusMessage?.Invoke(this, "Scanning network...");
            try
            {
                var networkProbes = await ScanNetworkAsync(ct);
                results.AddRange(networkProbes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                StatusMessage?.Invoke(this, $"Network scan error: {ex.Message}");
            }

            // 3. Simulator is always available
            results.Add(new DiscoveredProbe
            {
                ConnectionType = ProbeConnectionType.Simulator,
                DisplayName = "Philips C5-1 Simulator",
                Description = "Software phantom simulator — no hardware required",
                Address = "localhost"
            });

            StatusMessage?.Invoke(this, $"Discovery complete: {results.Count} source(s) found.");
            return results;
        }

        // ── Private connection methods ──

        private IUltrasoundProbe ConnectSimulator()
        {
            var simulator = new ConvexProbeSimulator();
            simulator.ConnectAsync().Wait(); // Simulator connection is instant
            _activeProbe = simulator;

            StatusMessage?.Invoke(this, "Simulator connected.");
            SetState(ProbeConnectionState.Connected);
            return simulator;
        }

        private async Task<IUltrasoundProbe?> ConnectUsbSerialAsync(CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_config.SerialPortName))
            {
                StatusMessage?.Invoke(this, "No serial port specified.");
                SetState(ProbeConnectionState.Error);
                return null;
            }

            StatusMessage?.Invoke(this, $"Opening USB/Serial on {_config.SerialPortName}...");

            var transport = new UsbSerialTransport();
            var driver = new PhilipsC51Driver(transport, _config);

            driver.ErrorOccurred += OnProbeError;

            bool connected = await driver.ConnectAsync(ct);
            if (!connected)
            {
                transport.Dispose();
                SetState(ProbeConnectionState.Error);
                return null;
            }

            _activeTransport = transport;
            _activeProbe = driver;

            StatusMessage?.Invoke(this, $"Connected via USB/Serial on {_config.SerialPortName}.");
            SetState(ProbeConnectionState.Connected);
            return driver;
        }

        private async Task<IUltrasoundProbe?> ConnectEthernetAsync(CancellationToken ct)
        {
            StatusMessage?.Invoke(this, $"Connecting via Ethernet to {_config.IpAddress}:{_config.CommandPort}...");

            var transport = new EthernetTransport();
            var driver = new PhilipsC51Driver(transport, _config);

            driver.ErrorOccurred += OnProbeError;

            bool connected = await driver.ConnectAsync(ct);
            if (!connected)
            {
                transport.Dispose();
                SetState(ProbeConnectionState.Error);
                return null;
            }

            _activeTransport = transport;
            _activeProbe = driver;

            StatusMessage?.Invoke(this, $"Connected via Ethernet to {_config.IpAddress}.");
            SetState(ProbeConnectionState.Connected);
            return driver;
        }

        // ── Discovery helpers ──

        private async Task<DiscoveredProbe?> ProbeSerialPortAsync(string portName, CancellationToken ct)
        {
            try
            {
                using var testPort = new SerialPort
                {
                    PortName = portName,
                    BaudRate = _config.BaudRate,
                    DataBits = 8,
                    ReadTimeout = 1000,
                    WriteTimeout = 500
                };

                testPort.Open();

                // Send a lightweight identify probe
                byte[] identifyCmd = [0xAA, 0x55, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
                await testPort.BaseStream.WriteAsync(identifyCmd, ct);

                // Try to read a response within timeout
                var response = new byte[32];
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(1000);

                int read = await testPort.BaseStream.ReadAsync(response, timeoutCts.Token);
                testPort.Close();

                if (read >= 11)
                {
                    string signature = System.Text.Encoding.ASCII.GetString(response, 0, Math.Min(11, read));
                    if (signature.StartsWith("PHILIPS_C51"))
                    {
                        return new DiscoveredProbe
                        {
                            ConnectionType = ProbeConnectionType.UsbSerial,
                            DisplayName = $"Philips C5-1 on {portName}",
                            Description = $"USB/Serial probe detected on {portName}",
                            Address = portName
                        };
                    }
                }
            }
            catch
            {
                // Port busy, access denied, or no probe — skip
            }

            return null;
        }

        private async Task<List<DiscoveredProbe>> ScanNetworkAsync(CancellationToken ct)
        {
            var found = new List<DiscoveredProbe>();

            // Scan a small set of common probe IP addresses
            string[] candidates =
            [
                _config.IpAddress, // user-configured address first
                "192.168.1.100",
                "192.168.1.101",
                "10.0.0.100",
                "10.0.0.101"
            ];

            var scanTasks = candidates.Distinct().Select(async ip =>
            {
                try
                {
                    using var client = new TcpClient();
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(1500);

                    await client.ConnectAsync(ip, _config.CommandPort, timeoutCts.Token);

                    // Port is open — send identify
                    var stream = client.GetStream();
                    stream.ReadTimeout = 1000;
                    stream.WriteTimeout = 500;

                    byte[] identifyCmd = [0xAA, 0x55, 0x00, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
                    await stream.WriteAsync(identifyCmd, timeoutCts.Token);

                    var response = new byte[32];
                    int read = await stream.ReadAsync(response, timeoutCts.Token);

                    if (read >= 11)
                    {
                        string signature = System.Text.Encoding.ASCII.GetString(response, 0, Math.Min(11, read));
                        if (signature.StartsWith("PHILIPS_C51"))
                        {
                            return new DiscoveredProbe
                            {
                                ConnectionType = ProbeConnectionType.Ethernet,
                                DisplayName = $"Philips C5-1 at {ip}",
                                Description = $"Ethernet probe at {ip}:{_config.CommandPort}",
                                Address = ip
                            };
                        }
                    }
                }
                catch { /* not reachable or no probe */ }

                return null;
            });

            var results = await Task.WhenAll(scanTasks);
            found.AddRange(results.Where(r => r != null)!);
            return found;
        }

        private void OnProbeError(object? sender, string message)
        {
            StatusMessage?.Invoke(this, message);

            // If the message indicates a link-loss, update state
            if (message.Contains("Link lost") || message.Contains("reconnect", StringComparison.OrdinalIgnoreCase))
            {
                SetState(message.Contains("Reconnected")
                    ? ProbeConnectionState.Connected
                    : ProbeConnectionState.Reconnecting);
            }
        }

        private void SetState(ProbeConnectionState state)
        {
            if (ConnectionState != state)
            {
                ConnectionState = state;
                ConnectionStateChanged?.Invoke(this, state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _activeProbe?.Dispose();
            _activeTransport?.Dispose();
        }
    }

    /// <summary>
    /// Represents a discovered probe endpoint during device scanning.
    /// </summary>
    public class DiscoveredProbe
    {
        public ProbeConnectionType ConnectionType { get; init; }
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public string Address { get; init; } = "";
    }
}
