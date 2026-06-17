using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AuraScan_Ultrasound_System.Core.Hardware;
using AuraScan_Ultrasound_System.Core.Imaging;
using AuraScan_Ultrasound_System.Core.Measurements;
using AuraScan_Ultrasound_System.Core.Segmentation;
using AuraScan_Ultrasound_System.Core.Dicom;
using AuraScan_Ultrasound_System.Models;
using AuraScan_Ultrasound_System.Services;

namespace AuraScan_Ultrasound_System.ViewModels
{
    /// <summary>
    /// Primary view model orchestrating all imaging engines, probe control,
    /// measurements, segmentation, and DICOM services.
    /// </summary>
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        // ── Core components ──
        private IUltrasoundProbe _probe;
        private readonly ProbeManager _probeManager;
        private readonly BModeEngine _bmodeEngine;
        private readonly MModeEngine _mmodeEngine;
        private readonly ColorDopplerEngine _colorDopplerEngine;
        private readonly SpectralDopplerEngine _spectralDopplerEngine;
        private readonly VolumeEngine _volumeEngine;
        private readonly SegmentationEngine _segmentationEngine;
        private readonly MeasurementTool _measurementTool;
        private readonly DicomService _dicomService;
        private readonly ServerApiClient _serverClient;

        private CancellationTokenSource? _scanCts;
        private readonly Dispatcher _dispatcher;

        // ── Observable properties ──

        [ObservableProperty] private WriteableBitmap? _displayImage;
        [ObservableProperty] private ImagingMode _currentMode = ImagingMode.BMode;
        [ObservableProperty] private AcquisitionState _acquisitionState = AcquisitionState.Idle;
        [ObservableProperty] private CineState _cineState = CineState.Stopped;

        [ObservableProperty] private double _depth = 15.0;
        [ObservableProperty] private double _gain = 50.0;
        [ObservableProperty] private double _dynamicRange = 60.0;
        [ObservableProperty] private double _transmitPower = 0.8;
        [ObservableProperty] private int _persistence = 2;
        [ObservableProperty] private double _sectorAngle = 70.0;
        [ObservableProperty] private bool _harmonicImaging;
        [ObservableProperty] private double _transmitFrequency = 3.5;

        [ObservableProperty] private double _tgc1 = 0.7;
        [ObservableProperty] private double _tgc2 = 0.6;
        [ObservableProperty] private double _tgc3 = 0.5;
        [ObservableProperty] private double _tgc4 = 0.5;
        [ObservableProperty] private double _tgc5 = 0.5;
        [ObservableProperty] private double _tgc6 = 0.5;
        [ObservableProperty] private double _tgc7 = 0.6;
        [ObservableProperty] private double _tgc8 = 0.7;

        [ObservableProperty] private double _dopplerPrf = 4000.0;
        [ObservableProperty] private double _dopplerAngle = 60.0;
        [ObservableProperty] private double _velocityScale = 0.5;

        [ObservableProperty] private double _frameRate;
        [ObservableProperty] private double _mechanicalIndex;
        [ObservableProperty] private double _thermalIndex;

        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private string _probeInfo = "Philips C5-1 | Convex | 1-5 MHz";
        [ObservableProperty] private TissuePreset _currentPreset = TissuePreset.Abdomen;
        [ObservableProperty] private MeasurementType? _activeMeasurement;
        [ObservableProperty] private SegmentationAlgorithm _selectedSegmentation = SegmentationAlgorithm.RegionGrowing;

        [ObservableProperty] private PatientInfo _patientInfo = new();
        [ObservableProperty] private DicomServerConfig _dicomConfig = new();

        // ── Probe connection properties ──
        [ObservableProperty] private ProbeConnectionType _selectedConnectionType = ProbeConnectionType.Simulator;
        [ObservableProperty] private ProbeConnectionState _currentConnectionState = ProbeConnectionState.Disconnected;
        [ObservableProperty] private string _serialPortName = "";
        [ObservableProperty] private string _probeIpAddress = "192.168.1.100";
        [ObservableProperty] private bool _isProbeConnected;
        [ObservableProperty] private string _connectionStatusText = "Disconnected";
        [ObservableProperty] private List<string> _availableSerialPorts = [];
        [ObservableProperty] private List<DiscoveredProbe> _discoveredProbes = [];

        // Server connection
        [ObservableProperty] private ServerConfig _serverSettings = new();
        [ObservableProperty] private bool _isServerConnected;
        [ObservableProperty] private string _serverStatusText = "Server: Not connected";
        private int? _lastServerImageId;

        // Security — inactivity auto-lock
        [ObservableProperty] private bool _isScreenLocked;
        private DispatcherTimer? _inactivityTimer;
        private readonly int _lockTimeoutMinutes = 15;

        // Battery monitoring
        private readonly BatteryMonitorService _batteryMonitor;
        [ObservableProperty] private bool _isOnBattery;
        [ObservableProperty] private bool _hasBattery;
        [ObservableProperty] private int _batteryPercent = 100;
        [ObservableProperty] private bool _isBatteryCharging;
        [ObservableProperty] private string _batteryTimeRemaining = "";
        [ObservableProperty] private string _batteryStatusText = "AC Power";

        // Cine loop
        private readonly List<UltrasoundFrame> _cineBuffer = [];
        private int _cineIndex;
        private const int MaxCineFrames = 300;
        private UltrasoundFrame? _lastFrame;

        public ProbeConfiguration ProbeConfig { get; private set; }
        public MeasurementTool MeasurementTool => _measurementTool;
        public ProbeManager ProbeManager => _probeManager;

        public MainViewModel()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            // Initialize probe manager
            _probeManager = new ProbeManager();
            _probeManager.ConnectionStateChanged += OnProbeConnectionStateChanged;
            _probeManager.StatusMessage += (_, msg) =>
                _dispatcher.BeginInvoke(() => StatusText = msg);

            // Start with simulator by default
            _probe = new ConvexProbeSimulator();
            ProbeConfig = _probe.Configuration;

            // Initialize engines
            _bmodeEngine = new BModeEngine();
            _mmodeEngine = new MModeEngine();
            _colorDopplerEngine = new ColorDopplerEngine();
            _spectralDopplerEngine = new SpectralDopplerEngine();
            _volumeEngine = new VolumeEngine();
            _segmentationEngine = new SegmentationEngine();
            _measurementTool = new MeasurementTool();
            _dicomService = new DicomService();

            _dicomService.ErrorOccurred += (_, msg) =>
                _dispatcher.BeginInvoke(() => StatusText = $"DICOM Error: {msg}");

            // Initialize server client
            _serverClient = new ServerApiClient();
            _serverClient.StatusMessage += (_, msg) =>
                _dispatcher.BeginInvoke(() => ServerStatusText = msg);
            _serverClient.ServerNotification += (_, msg) =>
                _dispatcher.BeginInvoke(() => StatusText = msg);

            // Auto-connect to server on startup
            _ = ConnectToServerAsync();

            // Start inactivity auto-lock timer (HIPAA §164.312(a)(2)(iii))
            _inactivityTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMinutes(_lockTimeoutMinutes)
            };
            _inactivityTimer.Tick += (_, _) =>
            {
                if (AcquisitionState != AcquisitionState.Scanning)
                {
                    IsScreenLocked = true;
                    StatusText = $"Screen locked — inactive for {_lockTimeoutMinutes} min";
                }
            };
            _inactivityTimer.Start();

            // Refresh available serial ports
            RefreshSerialPorts();

            // Initialize battery monitor
            _batteryMonitor = new BatteryMonitorService(pollIntervalSeconds: 30);
            _batteryMonitor.StatusChanged += OnBatteryStatusChanged;
            UpdateBatteryProperties(_batteryMonitor.CurrentStatus);
        }

        private ScanParameters BuildScanParameters()
        {
            double freqHz = TransmitFrequency * 1e6;
            // Nyquist velocity: V_nyquist = (PRF * c) / (4 * f)
            double nyquist = (DopplerPrf * 1540.0) / (4.0 * freqHz);

            return new ScanParameters
            {
                Mode = CurrentMode,
                DepthM = Depth / 100.0,
                GainDb = Gain,
                DynamicRangeDb = DynamicRange,
                TransmitPower = TransmitPower,
                Persistence = Persistence,
                SectorAngleDeg = SectorAngle,
                HarmonicImaging = HarmonicImaging,
                TransmitFrequencyHz = freqHz,
                TgcCurve = [Tgc1, Tgc2, Tgc3, Tgc4, Tgc5, Tgc6, Tgc7, Tgc8],
                FocalDepthsM = [Depth / 100.0 * 0.4, Depth / 100.0 * 0.6],
                Preset = CurrentPreset,
                DopplerPrfHz = DopplerPrf,
                DopplerAngleCorrectionDeg = DopplerAngle,
                DopplerVelocityScaleMps = VelocityScale,
                NyquistVelocityMps = nyquist
            };
        }

        // ── Scan control commands ──

        [RelayCommand]
        private async Task StartScanAsync()
        {
            if (AcquisitionState == AcquisitionState.Scanning) return;

            _scanCts = new CancellationTokenSource();
            var ct = _scanCts.Token;
            var scanParams = BuildScanParameters();

            if (!_probe.IsConnected)
                await _probe.ConnectAsync(ct);

            // Initialize engines with display dimensions
            int displayWidth = 640;
            int displayHeight = 480;
            InitializeEngines(scanParams, displayWidth, displayHeight);

            await _probe.StartAcquisitionAsync(scanParams, ct);
            AcquisitionState = AcquisitionState.Scanning;
            StatusText = $"Scanning — {CurrentMode}";

            // Acquisition loop
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // Rebuild each frame so live slider/control changes take effect
                        var scanParams = BuildScanParameters();

                        var rfData = await _probe.AcquireFrameAsync(ct);
                        UltrasoundFrame? frame = null;

                        // For Doppler modes, acquire ensemble
                        if (CurrentMode is ImagingMode.ColorDoppler or ImagingMode.PowerDoppler)
                        {
                            var ensemble = await _probe.AcquireDopplerEnsembleAsync(
                                scanParams.ColorEnsembleLength, ct);
                            _colorDopplerEngine.CurrentEnsemble = ensemble;
                            frame = _colorDopplerEngine.ProcessFrame(rfData, scanParams);
                        }
                        else if (CurrentMode == ImagingMode.SpectralDoppler)
                        {
                            var ensemble = await _probe.AcquireDopplerEnsembleAsync(
                                scanParams.SpectralFftSize, ct);
                            _spectralDopplerEngine.CurrentEnsemble = ensemble;
                            frame = _spectralDopplerEngine.ProcessFrame(rfData, scanParams);
                        }
                        else if (CurrentMode == ImagingMode.MMode)
                        {
                            frame = _mmodeEngine.ProcessFrame(rfData, scanParams);
                        }
                        else if (CurrentMode == ImagingMode.Volume3D)
                        {
                            frame = _volumeEngine.ProcessFrame(rfData, scanParams);
                        }
                        else
                        {
                            frame = _bmodeEngine.ProcessFrame(rfData, scanParams);
                        }

                        if (frame?.RenderedImage != null)
                        {
                            _dispatcher.BeginInvoke(() =>
                            {
                                DisplayImage = frame.RenderedImage;
                                FrameRate = frame.FrameRateHz;
                                MechanicalIndex = frame.MechanicalIndex;
                                ThermalIndex = frame.ThermalIndex;
                                _lastFrame = frame;

                                // Add to cine buffer
                                if (CineState == CineState.Recording && _cineBuffer.Count < MaxCineFrames)
                                    _cineBuffer.Add(frame);
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _dispatcher.BeginInvoke(() => StatusText = $"Error: {ex.Message}");
                    }
                }
            }, ct);
        }

        [RelayCommand]
        private async Task StopScanAsync()
        {
            _scanCts?.Cancel();
            await _probe.StopAcquisitionAsync();
            AcquisitionState = AcquisitionState.Idle;
            StatusText = "Stopped";
        }

        [RelayCommand]
        private void Freeze()
        {
            if (AcquisitionState == AcquisitionState.Scanning)
            {
                _scanCts?.Cancel();
                AcquisitionState = AcquisitionState.Frozen;
                StatusText = "Frozen";
            }
            else if (AcquisitionState == AcquisitionState.Frozen)
            {
                _ = StartScanAsync();
            }
        }

        // ── Mode switching commands ──

        [RelayCommand]
        private void SetBMode() => SwitchMode(ImagingMode.BMode);

        [RelayCommand]
        private void SetMMode() => SwitchMode(ImagingMode.MMode);

        [RelayCommand]
        private void SetColorDoppler() => SwitchMode(ImagingMode.ColorDoppler);

        [RelayCommand]
        private void SetPowerDoppler() => SwitchMode(ImagingMode.PowerDoppler);

        [RelayCommand]
        private void SetSpectralDoppler() => SwitchMode(ImagingMode.SpectralDoppler);

        [RelayCommand]
        private void SetVolume3D() => SwitchMode(ImagingMode.Volume3D);

        private async void SwitchMode(ImagingMode mode)
        {
            bool wasScanning = AcquisitionState == AcquisitionState.Scanning;
            if (wasScanning)
                await StopScanAsync();

            CurrentMode = mode;
            StatusText = $"Mode: {mode}";

            if (wasScanning)
                await StartScanAsync();
        }

        // ── Preset commands ──

        [RelayCommand]
        private void SetPreset(string preset)
        {
            if (Enum.TryParse<TissuePreset>(preset, out var p))
            {
                CurrentPreset = p;
                ApplyPreset(p);
            }
        }

        private void ApplyPreset(TissuePreset preset)
        {
            switch (preset)
            {
                case TissuePreset.Abdomen:
                    Depth = 20.0; TransmitFrequency = 3.5; Gain = 50; DynamicRange = 60;
                    break;
                case TissuePreset.ObGyn:
                    Depth = 18.0; TransmitFrequency = 3.5; Gain = 55; DynamicRange = 55;
                    break;
                case TissuePreset.Vascular:
                    Depth = 6.0; TransmitFrequency = 5.0; Gain = 45; DynamicRange = 65;
                    break;
                case TissuePreset.Cardiac:
                    Depth = 16.0; TransmitFrequency = 2.5; Gain = 50; DynamicRange = 55;
                    break;
                case TissuePreset.SmallParts:
                    Depth = 5.0; TransmitFrequency = 5.0; Gain = 50; DynamicRange = 60;
                    break;
                case TissuePreset.Renal:
                    Depth = 15.0; TransmitFrequency = 3.5; Gain = 50; DynamicRange = 60;
                    break;
            }
            StatusText = $"Preset: {preset}";
        }

        // ── Cine commands ──

        [RelayCommand]
        private void CineRecord()
        {
            _cineBuffer.Clear();
            _cineIndex = 0;
            CineState = CineState.Recording;
            StatusText = "Recording cine loop...";
        }

        [RelayCommand]
        private void CinePlay()
        {
            if (_cineBuffer.Count == 0) return;
            CineState = CineState.Playing;
            StatusText = $"Playing cine ({_cineBuffer.Count} frames)";

            _ = Task.Run(async () =>
            {
                while (CineState == CineState.Playing)
                {
                    var frame = _cineBuffer[_cineIndex];
                    _dispatcher.BeginInvoke(() => DisplayImage = frame.RenderedImage);

                    _cineIndex = (_cineIndex + 1) % _cineBuffer.Count;
                    await Task.Delay(33); // ~30 fps playback
                }
            });
        }

        [RelayCommand]
        private void CineStop()
        {
            CineState = CineState.Stopped;
            StatusText = "Cine stopped";
        }

        [RelayCommand]
        private void CineStepForward()
        {
            if (_cineBuffer.Count == 0) return;
            CineState = CineState.Paused;
            _cineIndex = (_cineIndex + 1) % _cineBuffer.Count;
            DisplayImage = _cineBuffer[_cineIndex].RenderedImage;
        }

        [RelayCommand]
        private void CineStepBack()
        {
            if (_cineBuffer.Count == 0) return;
            CineState = CineState.Paused;
            _cineIndex = (_cineIndex - 1 + _cineBuffer.Count) % _cineBuffer.Count;
            DisplayImage = _cineBuffer[_cineIndex].RenderedImage;
        }

        // ── Measurement commands ──

        [RelayCommand]
        private void SetMeasurementMode(string type)
        {
            if (Enum.TryParse<MeasurementType>(type, out var m))
                ActiveMeasurement = m;
        }

        [RelayCommand]
        private void ClearMeasurements()
        {
            _measurementTool.ClearAll();
            ActiveMeasurement = null;
        }

        [RelayCommand]
        private void UndoMeasurement() => _measurementTool.Undo();

        [RelayCommand]
        private async Task SaveMeasurementsToServerAsync()
        {
            if (!IsServerConnected || !_lastServerImageId.HasValue)
            {
                StatusText = "No server connection or no stored image to attach measurements to";
                return;
            }

            var measurements = _measurementTool.Measurements;
            if (measurements.Count == 0) { StatusText = "No measurements to save"; return; }

            int saved = 0;
            foreach (var m in measurements)
            {
                var result = await _serverClient.SaveMeasurementAsync(m, _lastServerImageId.Value);
                if (result != null) saved++;
            }
            StatusText = $"Saved {saved}/{measurements.Count} measurements to server";
        }

        // ── Segmentation commands ──

        [RelayCommand]
        private void RunSegmentation(Point seedPoint)
        {
            if (AcquisitionState != AcquisitionState.Frozen || DisplayImage == null) return;

            // Get current B-Mode image data from the last displayed frame
            var lastFrame = _lastFrame;
            if (lastFrame?.BmodeImageData == null) return;

            int width = lastFrame.ImageWidth;
            int height = lastFrame.ImageHeight;
            int seedX = (int)seedPoint.X;
            int seedY = (int)seedPoint.Y;

            SegmentationResult result;
            switch (SelectedSegmentation)
            {
                case SegmentationAlgorithm.RegionGrowing:
                    result = _segmentationEngine.RegionGrowing(
                        lastFrame.BmodeImageData, width, height, seedX, seedY);
                    break;
                case SegmentationAlgorithm.LevelSet:
                    result = _segmentationEngine.LevelSetSegmentation(
                        lastFrame.BmodeImageData, width, height, seedX, seedY);
                    break;
                case SegmentationAlgorithm.Watershed:
                    result = _segmentationEngine.WatershedSegmentation(
                        lastFrame.BmodeImageData, width, height, seedX, seedY);
                    break;
                default:
                    return;
            }

            // Calibrate with physical dimensions
            var (pxCmX, pxCmY) = MeasurementTool.GetCalibration(
                BuildScanParameters(), ProbeConfig, width, height);
            _segmentationEngine.CalibrateMeasurements(result, pxCmX, pxCmY);

            StatusText = $"Segmented: {result.Algorithm} — Area: {result.AreaCm2:F2} cm² ({result.ProcessingTimeMs:F0} ms)";

            // Persist segmentation result to server
            if (IsServerConnected && _lastServerImageId.HasValue)
            {
                _ = _serverClient.SaveSegmentationAsync(result, _lastServerImageId.Value);
            }
        }

        // ── DICOM commands ──

        [RelayCommand]
        private async Task DicomStoreAsync()
        {
            var lastFrame = _lastFrame;
            if (lastFrame == null) { StatusText = "No frame to store"; return; }

            StatusText = "Storing to DICOM...";
            bool success = await _dicomService.StoreImageAsync(
                lastFrame, PatientInfo, ProbeConfig, BuildScanParameters(), DicomConfig);
            StatusText = success ? "DICOM store successful" : "DICOM store failed";

            // Also persist to server
            if (success && IsServerConnected)
            {
                var scanParams = BuildScanParameters();
                _lastServerImageId = await _serverClient.PersistAcquisitionAsync(
                    PatientInfo, lastFrame, scanParams, ProbeConfig);
            }
        }

        [RelayCommand]
        private async Task DicomSaveLocalAsync()
        {
            var lastFrame = _lastFrame;
            if (lastFrame == null) { StatusText = "No frame to save"; return; }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "DICOM Files (*.dcm)|*.dcm",
                DefaultExt = ".dcm",
                FileName = $"US_{PatientInfo.PatientId}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                await _dicomService.SaveLocalAsync(lastFrame, PatientInfo, ProbeConfig,
                    BuildScanParameters(), dialog.FileName);
                StatusText = $"Saved: {dialog.FileName}";
            }
        }

        [RelayCommand]
        private async Task DicomEchoAsync()
        {
            StatusText = "Testing DICOM connection...";
            bool success = await _dicomService.EchoAsync(DicomConfig);
            StatusText = success ? "DICOM Echo: Success" : "DICOM Echo: Failed";
        }

        [RelayCommand]
        private async Task DicomQueryWorklistAsync()
        {
            StatusText = "Querying worklist...";
            var patients = await _dicomService.QueryWorklistAsync(DicomConfig, DateTime.Today, "US");
            StatusText = $"Worklist: {patients.Count} procedures found";
        }

        // ── Depth control ──

        [RelayCommand]
        private void IncreaseDepth()
        {
            Depth = Math.Min(Depth + 1.0, ProbeConfig.MaxDepthM * 100.0);
        }

        [RelayCommand]
        private void DecreaseDepth()
        {
            Depth = Math.Max(Depth - 1.0, 2.0);
        }

        // ── Probe connection commands ──

        [RelayCommand]
        private async Task ConnectProbeAsync()
        {
            // Stop any active scan first
            if (AcquisitionState == AcquisitionState.Scanning)
                await StopScanAsync();

            // Build connection config from UI properties
            var config = new HardwareConnectionConfig
            {
                ConnectionType = SelectedConnectionType,
                SerialPortName = SerialPortName,
                IpAddress = ProbeIpAddress
            };

            _probeManager.SetConfig(config);

            StatusText = $"Connecting ({SelectedConnectionType})...";
            var probe = await _probeManager.ConnectAsync();

            if (probe != null)
            {
                _probe = probe;
                ProbeConfig = probe.Configuration;
                OnPropertyChanged(nameof(ProbeConfig));

                var cfg = probe.Configuration;
                ProbeInfo = $"{cfg.Manufacturer} {cfg.ModelName} | {cfg.ProbeType} | " +
                            $"{cfg.BandwidthLowHz / 1e6:F0}-{cfg.BandwidthHighHz / 1e6:F0} MHz";

                IsProbeConnected = true;
                StatusText = $"Connected — {cfg.Manufacturer} {cfg.ModelName} ({SelectedConnectionType})";
            }
            else
            {
                IsProbeConnected = false;
                StatusText = "Connection failed.";
            }
        }

        [RelayCommand]
        private async Task DisconnectProbeAsync()
        {
            if (AcquisitionState == AcquisitionState.Scanning)
                await StopScanAsync();

            await _probeManager.DisconnectAsync();
            IsProbeConnected = false;
            StatusText = "Probe disconnected.";
        }

        [RelayCommand]
        private async Task DiscoverProbesAsync()
        {
            StatusText = "Discovering probes...";
            var probes = await _probeManager.DiscoverAsync();
            DiscoveredProbes = probes;
            StatusText = $"Found {probes.Count} probe source(s).";
        }

        [RelayCommand]
        private void RefreshSerialPorts()
        {
            try
            {
                AvailableSerialPorts = [.. UsbSerialTransport.GetAvailablePorts()];
            }
            catch
            {
                AvailableSerialPorts = [];
            }
        }

        [RelayCommand]
        private void SelectDiscoveredProbe(DiscoveredProbe probe)
        {
            if (probe == null) return;

            SelectedConnectionType = probe.ConnectionType;
            switch (probe.ConnectionType)
            {
                case ProbeConnectionType.UsbSerial:
                    SerialPortName = probe.Address;
                    break;
                case ProbeConnectionType.Ethernet:
                    ProbeIpAddress = probe.Address;
                    break;
            }
            StatusText = $"Selected: {probe.DisplayName}";
        }

        private void OnProbeConnectionStateChanged(object? sender, ProbeConnectionState state)
        {
            _dispatcher.BeginInvoke(() =>
            {
                CurrentConnectionState = state;
                IsProbeConnected = state == ProbeConnectionState.Connected;
                ConnectionStatusText = state switch
                {
                    ProbeConnectionState.Disconnected => "Disconnected",
                    ProbeConnectionState.Connecting => "Connecting...",
                    ProbeConnectionState.Connected => "Connected",
                    ProbeConnectionState.Reconnecting => "Reconnecting...",
                    ProbeConnectionState.Error => "Connection Error",
                    _ => state.ToString()
                };
            });
        }

        // ── Helpers ──

        private async Task ConnectToServerAsync()
        {
            try
            {
                var healthy = await _serverClient.CheckHealthAsync();
                if (healthy)
                {
                    await _serverClient.ConnectSignalRAsync();
                    _dispatcher.BeginInvoke(() =>
                    {
                        IsServerConnected = true;
                        ServerStatusText = $"Server: Connected ({ServerSettings.BaseUrl})";
                    });
                }
                else
                {
                    _dispatcher.BeginInvoke(() =>
                    {
                        IsServerConnected = false;
                        ServerStatusText = "Server: Offline";
                    });
                }
            }
            catch
            {
                _dispatcher.BeginInvoke(() =>
                {
                    IsServerConnected = false;
                    ServerStatusText = "Server: Connection failed";
                });
            }
        }

        [RelayCommand]
        private async Task ReconnectServerAsync()
        {
            ServerStatusText = "Server: Connecting...";
            await _serverClient.DisconnectSignalRAsync();
            await ConnectToServerAsync();
        }

        [RelayCommand]
        private async Task DisconnectServerAsync()
        {
            await _serverClient.DisconnectSignalRAsync();
            IsServerConnected = false;
            ServerStatusText = "Server: Disconnected";
        }

        [RelayCommand]
        private async Task ServerHealthCheckAsync()
        {
            var healthy = await _serverClient.CheckHealthAsync();
            ServerStatusText = healthy
                ? $"Server: Healthy ({ServerSettings.BaseUrl})"
                : "Server: Unreachable";
        }

        private void InitializeEngines(ScanParameters scanParams, int width, int height)
        {
            _bmodeEngine.Initialize(ProbeConfig, scanParams, width, height);
            _mmodeEngine.Initialize(ProbeConfig, scanParams, width, height);
            _colorDopplerEngine.Initialize(ProbeConfig, scanParams, width, height);
            _spectralDopplerEngine.Initialize(ProbeConfig, scanParams, width, height);
            _volumeEngine.Initialize(ProbeConfig, scanParams, width, height);
        }

        /// <summary>
        /// Call from UI on any user interaction (mouse move, key press, touch)
        /// to reset the inactivity auto-lock countdown.
        /// </summary>
        public void ResetInactivityTimer()
        {
            if (_inactivityTimer != null)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
            }
        }

        [RelayCommand]
        private void UnlockScreen()
        {
            IsScreenLocked = false;
            ResetInactivityTimer();
            StatusText = "Screen unlocked";
        }

        private void OnBatteryStatusChanged(object? sender, BatteryStatus status)
        {
            _dispatcher.BeginInvoke(() => UpdateBatteryProperties(status));
        }

        private void UpdateBatteryProperties(BatteryStatus status)
        {
            HasBattery = status.HasBattery;
            IsOnBattery = status.IsOnBattery;
            BatteryPercent = status.PercentRemaining;
            IsBatteryCharging = status.ChargeStatus == BatteryChargeStatus.Charging;

            if (!status.HasBattery)
            {
                BatteryStatusText = "No Battery";
                BatteryTimeRemaining = "";
            }
            else if (status.ChargeStatus == BatteryChargeStatus.Charging)
            {
                BatteryStatusText = $"Charging — {status.PercentRemaining}%";
                BatteryTimeRemaining = "";
            }
            else if (status.ChargeStatus == BatteryChargeStatus.Full)
            {
                BatteryStatusText = "Fully Charged";
                BatteryTimeRemaining = "";
            }
            else if (status.PowerSource == PowerSource.AC)
            {
                BatteryStatusText = $"AC Power — {status.PercentRemaining}%";
                BatteryTimeRemaining = "";
            }
            else
            {
                BatteryStatusText = $"Battery — {status.PercentRemaining}%";
                if (status.EstimatedRuntime.HasValue)
                {
                    var rt = status.EstimatedRuntime.Value;
                    BatteryTimeRemaining = rt.TotalHours >= 1
                        ? $"{(int)rt.TotalHours}h {rt.Minutes}m remaining"
                        : $"{rt.Minutes}m remaining";
                }
                else
                {
                    BatteryTimeRemaining = "Estimating...";
                }

                // Low battery warning
                if (status.PercentRemaining <= 10)
                    StatusText = $"⚠ CRITICAL BATTERY: {status.PercentRemaining}% — Connect AC power immediately";
                else if (status.PercentRemaining <= 20)
                    StatusText = $"⚠ Low battery: {status.PercentRemaining}% — Connect AC power";
            }
        }

        public void Dispose()
        {
            _inactivityTimer?.Stop();
            _batteryMonitor.Dispose();
            try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { }
            _scanCts?.Dispose();
            _probe.Dispose();
            _probeManager.Dispose();
            _bmodeEngine.Dispose();
            _mmodeEngine.Dispose();
            _colorDopplerEngine.Dispose();
            _spectralDopplerEngine.Dispose();
            _volumeEngine.Dispose();
            _segmentationEngine.Dispose();
            _dicomService.Dispose();
            _serverClient.Dispose();
        }
    }
}
