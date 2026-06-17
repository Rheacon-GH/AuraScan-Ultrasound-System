# AuraScan Ultrasound System — Architecture & Developer Guide

**Version 1.0** | Developed by **Rheacon Systems**

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Solution Structure](#2-solution-structure)
3. [Dependency Graph](#3-dependency-graph)
4. [Application Lifecycle](#4-application-lifecycle)
5. [MVVM & UI Architecture](#5-mvvm--ui-architecture)
6. [Signal Processing Pipeline](#6-signal-processing-pipeline)
7. [Imaging Engines](#7-imaging-engines)
8. [Hardware Abstraction Layer](#8-hardware-abstraction-layer)
9. [Measurement & Segmentation](#9-measurement--segmentation)
10. [DICOM Integration](#10-dicom-integration)
11. [Server Architecture](#11-server-architecture)
12. [Authentication & Security](#12-authentication--security)
13. [Data Model & Entity Framework](#13-data-model--entity-framework)
14. [Real-Time Communication (SignalR)](#14-real-time-communication-signalr)
15. [Workstation–Server Data Flow](#15-workstationserver-data-flow)
16. [Licensing System](#16-licensing-system)
17. [Battery Monitoring](#17-battery-monitoring)
18. [Key Abstractions & Interfaces](#18-key-abstractions--interfaces)
19. [Adding a New Imaging Mode](#19-adding-a-new-imaging-mode)
20. [Adding a New Probe Driver](#20-adding-a-new-probe-driver)
21. [Adding a New Measurement Type](#21-adding-a-new-measurement-type)
22. [Build & Test](#22-build--test)
23. [Code Conventions](#23-code-conventions)

---

## 1. Architecture Overview

AuraScan is a three-project .NET 8 solution using a **client–server** architecture:

```
┌──────────────────────────────────────────────────────────────┐
│                   AuraScan Workstation (WPF)                 │
│  ┌─────────────┐  ┌────────────────┐  ┌───────────────────┐ │
│  │   XAML UI    │  │  MainViewModel │  │  AuraScan.Core    │ │
│  │  (Views)     │◄─┤  (MVVM)        │◄─┤  (Engines, HW,   │ │
│  │             │  │               │  │   DICOM, Models)  │ │
│  └─────────────┘  └────────────────┘  └─────────┬─────────┘ │
│                                                  │           │
│                        ServerApiClient ──────────┤           │
│                        (HTTP + SignalR)           │           │
└──────────────────────────────────────────────────┼───────────┘
												   │
												   ▼
┌──────────────────────────────────────────────────────────────┐
│                    AuraScan.Server (ASP.NET Core)            │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌─────────────┐ │
│  │ REST API  │  │ SignalR  │  │ DICOM    │  │  SQLite DB  │ │
│  │Controllers│  │  Hub     │  │  SCP     │  │  (EF Core)  │ │
│  └──────────┘  └──────────┘  └──────────┘  └─────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

**Design principles:**

- **Separation of concerns** — UI project contains only XAML views and startup; all logic lives in `AuraScan.Core`.
- **Interface-driven hardware** — Probe communication uses `IUltrasoundProbe` / `IProbeTransport` abstractions so the simulator and real hardware are interchangeable.
- **Pipeline architecture** — Each imaging mode is an `IImagingEngine` that chains signal processing stages.
- **MVVM** — `MainViewModel` (CommunityToolkit.Mvvm) orchestrates all engines, binds to XAML via `[ObservableProperty]` and `[RelayCommand]`.
- **Offline-capable** — The workstation functions without the server; server features degrade gracefully.

---

## 2. Solution Structure

```
AuraScan Ultrasound System.slnx
│
├── AuraScan Ultrasound System.csproj   ← WPF workstation (entry point)
│   ├── App.xaml / App.xaml.cs          ← Startup, license gate, splash
│   ├── MainWindow.xaml / .cs           ← Kiosk-style main UI
│   ├── SplashScreen.xaml / .cs         ← Animated splash
│   ├── LicenseActivationWindow.xaml / .cs
│   ├── ViewModels\                     ← (empty — VM lives in Core)
│   ├── Converters\Converters.cs        ← WPF value converters
│   └── Docs\                           ← Documentation (this guide)
│
├── AuraScan.Core\AuraScan.Core.csproj  ← Shared class library
│   ├── ViewModels\MainViewModel.cs     ← Primary orchestrating ViewModel (887 lines)
│   ├── Core\
│   │   ├── Imaging\                    ← IImagingEngine implementations
│   │   │   ├── IImagingEngine.cs
│   │   │   ├── BModeEngine.cs
│   │   │   ├── MModeEngine.cs
│   │   │   ├── ColorDopplerEngine.cs
│   │   │   ├── SpectralDopplerEngine.cs
│   │   │   └── VolumeEngine.cs
│   │   ├── SignalProcessing\           ← DSP pipeline components
│   │   │   ├── BeamformerEngine.cs
│   │   │   ├── SignalProcessor.cs
│   │   │   ├── ScanConverter.cs
│   │   │   └── DopplerProcessor.cs
│   │   ├── Hardware\                   ← Probe abstraction layer
│   │   │   ├── IUltrasoundProbe.cs
│   │   │   ├── IProbeTransport.cs
│   │   │   ├── ProbeManager.cs
│   │   │   ├── PhilipsC51Driver.cs
│   │   │   ├── ConvexProbeSimulator.cs
│   │   │   ├── UsbSerialTransport.cs
│   │   │   └── EthernetTransport.cs
│   │   ├── Measurements\MeasurementTool.cs
│   │   ├── Segmentation\SegmentationEngine.cs
│   │   └── Dicom\
│   │       ├── DicomService.cs
│   │       └── DicomImageBuilder.cs
│   ├── Models\                         ← Data transfer & configuration types
│   │   ├── Enums.cs
│   │   ├── ScanParameters.cs
│   │   ├── ProbeConfiguration.cs
│   │   ├── UltrasoundFrame.cs
│   │   ├── PatientInfo.cs
│   │   ├── DicomServerConfig.cs
│   │   ├── HardwareConnectionConfig.cs
│   │   ├── MeasurementResult.cs
│   │   ├── SegmentationResult.cs
│   │   ├── ServerConfig.cs
│   │   └── ServerDtos.cs
│   ├── Services\
│   │   ├── ServerApiClient.cs          ← HTTP + SignalR client (386 lines)
│   │   ├── LicenseService.cs           ← Azure Blob license activation
│   │   └── BatteryMonitorService.cs    ← Win32 P/Invoke battery polling
│   └── Converters\Converters.cs
│
├── AuraScan.Server\AuraScan.Server.csproj  ← ASP.NET Core Web API
│   ├── Program.cs                      ← Host builder, middleware, DI
│   ├── Controllers\                    ← REST endpoints
│   │   ├── PatientsController.cs
│   │   ├── StudiesController.cs
│   │   ├── ImagesController.cs
│   │   ├── MeasurementsController.cs
│   │   ├── ConfigController.cs
│   │   └── AuditController.cs
│   ├── Hubs\AuraScanHub.cs             ← SignalR hub
│   ├── Data\
│   │   ├── AuraScanDbContext.cs         ← EF Core DbContext (SQLite)
│   │   └── Entities\                   ← Database entities
│   ├── Services\                       ← Business logic layer
│   │   ├── IPatientService / PatientService
│   │   ├── IStudyService / StudyService
│   │   ├── IImageService / ImageService
│   │   ├── IAuditService / AuditService
│   │   ├── IConfigService / ConfigService
│   │   └── DicomScpHostedService.cs    ← Hosted DICOM SCP
│   ├── Infrastructure\ApiKeyAuthHandler.cs
│   └── appsettings.json
│
└── Tools\LicenseUploader\              ← CLI tool for Azure Blob upload
	└── LicenseUploader.csproj
```

### Project References

```
AuraScan Ultrasound System ──► AuraScan.Core
AuraScan.Server                (standalone — no project references)
Tools\LicenseUploader          (standalone — no project references)
```

---

## 3. Dependency Graph

### AuraScan Ultrasound System (WPF)

| Package | Version | Purpose |
|---|---|---|
| fo-dicom | 5.2.6 | DICOM framework initialization at startup |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | DI for fo-dicom setup |

### AuraScan.Core (Class Library)

| Package | Version | Purpose |
|---|---|---|
| fo-dicom | 5.2.6 | DICOM file building, C-STORE SCU, C-ECHO, Worklist |
| OpenCvSharp4 + .Windows | 4.13.0 | Segmentation (FloodFill, morphology, watershed, contours) |
| HelixToolkit.Wpf | 3.1.2 | 3D volume rendering |
| MathNet.Numerics | 5.0.0 | FFT (Hilbert transform, spectral Doppler) |
| OxyPlot.Wpf | 2.2.0 | Spectral Doppler waveform display |
| CommunityToolkit.Mvvm | 8.4.2 | Source-generated MVVM (`[ObservableProperty]`, `[RelayCommand]`) |
| WriteableBitmapEx | 1.6.11 | Fast bitmap pixel manipulation for image rendering |
| System.IO.Ports | 8.0.0 | USB/serial probe communication |
| Microsoft.AspNetCore.SignalR.Client | 8.0.11 | Real-time server connection |
| Azure.Storage.Blobs | 12.21.0 | License activation via Azure Blob Storage |

### AuraScan.Server (ASP.NET Core)

| Package | Version | Purpose |
|---|---|---|
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.11 | SQLite database |
| Microsoft.EntityFrameworkCore.Design | 8.0.11 | Migrations tooling |
| fo-dicom | 5.2.6 | DICOM SCP (C-STORE / C-ECHO provider) |
| Swashbuckle.AspNetCore | 6.9.0 | Swagger / OpenAPI documentation |
| Microsoft.AspNetCore.SignalR.Core | 1.1.0 | SignalR hub |

---

## 4. Application Lifecycle

### Startup Flow (`App.xaml.cs`)

```
App.OnStartup()
  │
  ├── 1. ShutdownMode = OnExplicitShutdown
  │
  ├── 2. License Check
  │   ├── LicenseService.LocalLicenseExists()?
  │   │   ├── YES → continue
  │   │   └── NO  → Show LicenseActivationWindow
  │   │       ├── Activated → continue
  │   │       └── Cancelled → Shutdown(1)
  │   └── Exception → Error dialog → Shutdown(1)
  │
  ├── 3. Splash Screen (16 phased status updates, ~60 seconds)
  │   ├── Phase 1:  fo-dicom DI + DicomSetupBuilder
  │   ├── Phase 2-6: Signal processing, imaging, beamformer, scan conversion
  │   ├── Phase 7-8: Hardware abstraction, probe detection
  │   ├── Phase 9-10: Segmentation, measurement calibration
  │   ├── Phase 11-13: Security, server, DICOM network
  │   ├── Phase 14-15: Patient database, audit logging
  │   └── Phase 16: UI preparation → "Ready" → FadeOutAsync()
  │
  ├── 4. MainWindow created and shown
  │   └── MainViewModel constructor runs:
  │       ├── ProbeManager + ConvexProbeSimulator initialized
  │       ├── All 5 imaging engines created
  │       ├── SegmentationEngine, MeasurementTool, DicomService created
  │       ├── ServerApiClient created → auto-connect (health + SignalR)
  │       ├── Inactivity timer started (15 min)
  │       ├── Serial ports refreshed
  │       └── BatteryMonitorService started (30s poll)
  │
  └── 5. ShutdownMode = OnMainWindowClose
```

### Shutdown Flow (`MainViewModel.Dispose`)

All engines, probe, services, and timers are disposed in order.

---

## 5. MVVM & UI Architecture

### CommunityToolkit.Mvvm Source Generators

`MainViewModel` extends `ObservableObject` and uses source-generated patterns:

```csharp
// Source generator creates public property "Depth" with INotifyPropertyChanged
[ObservableProperty] private double _depth = 15.0;

// Source generator creates ICommand property "StartScanCommand"
[RelayCommand]
private async Task StartScanAsync() { ... }
```

**Key rules:**
- Field `_depth` → Property `Depth` (auto-generated, fires `PropertyChanged`)
- Method `StartScanAsync` → Property `StartScanCommand` (auto-generated `IAsyncRelayCommand`)
- Bind in XAML via `{Binding Depth}` or `{Binding StartScanCommand}`

### UI Dispatch Pattern

All UI updates from background threads go through `Dispatcher.BeginInvoke`:

```csharp
_dispatcher.BeginInvoke(() => {
	DisplayImage = frame.RenderedImage;
	FrameRate = frame.FrameRateHz;
});
```

### View–ViewModel Binding

| View | ViewModel | Relationship |
|---|---|---|
| `MainWindow.xaml` | `MainViewModel` | DataContext set in code-behind |
| `LicenseActivationWindow.xaml` | Inline code-behind | Self-contained dialog |
| `SplashScreen.xaml` | Inline code-behind | Animated overlay |

---

## 6. Signal Processing Pipeline

Every imaging mode follows a multi-stage pipeline. The B-Mode pipeline is the canonical example:

```
Raw RF Data [scanline][sample]    ← from IUltrasoundProbe.AcquireFrameAsync()
		│
		▼
┌──────────────────────┐
│   BeamformerEngine    │  Delay-and-sum with convex geometry
│   (convex focusing)  │  Precomputed delay/apodization tables
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   SignalProcessor     │  Hilbert transform → analytic signal magnitude
│   .EnvelopeDetect()  │  Parallel across scanlines
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   SignalProcessor     │  8-zone depth-dependent gain interpolation
│   .ApplyTgc()        │  Slider 0.0–1.0 → 0–40 dB linear gain
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   SignalProcessor     │  Overall gain adjustment (relative to 50 dB center)
│   .ApplyGain()       │
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   SignalProcessor     │  20·log10 normalization → 0–255 byte range
│   .LogCompress()     │  Dynamic range controls compression curve
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   SignalProcessor     │  Weighted average with previous frame
│   .ApplyPersistence()│  Persistence 0–7 (higher = smoother)
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   ScanConverter       │  Polar (angle × depth) → Cartesian (x × y)
│   .Convert()         │  Precomputed LUT: scanlineMap, sampleMap, weightMap
└──────────┬───────────┘
		   ▼
┌──────────────────────┐
│   RenderToBitmap()   │  byte[] → WriteableBitmap (Gray8 or Bgra32)
└──────────────────────┘
```

### Key Algorithms

| Stage | Algorithm | Library |
|---|---|---|
| Beamforming | Delay-and-sum, Hanning apodization, convex arc geometry | Custom |
| Envelope detection | Hilbert transform via FFT | MathNet.Numerics |
| TGC | 8-zone piecewise linear interpolation | Custom |
| Log compression | 20·log10 with dynamic range clamping | Custom |
| Persistence | Exponential moving average | Custom |
| Scan conversion | Polar→Cartesian LUT with bilinear interpolation | Custom |

### BeamformerEngine Internals

The beamformer precomputes two tables (`Initialize()`):

- **Delay table** `[scanline][element]` — Round-trip time difference (in samples) between each element and the focus point, computed from convex arc geometry.
- **Apodization table** `[scanline][element]` — Hanning window based on angular distance from the scanline direction.

These tables are recomputed only when depth, focus, or sector angle changes.

### ScanConverter Internals

The scan converter precomputes three LUTs (`Initialize()`):

- `scanlineMap[y][x]` — Source scanline index for each output pixel
- `sampleMap[y][x]` — Source sample index for each output pixel
- `weightMap[y][x]` — Bilinear interpolation weight

Pixels outside the sector fan are marked with `-1` and rendered as black.

---

## 7. Imaging Engines

All engines implement `IImagingEngine`:

```csharp
public interface IImagingEngine : IDisposable
{
	ImagingMode Mode { get; }
	bool IsRunning { get; }
	double FrameRateHz { get; }
	void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams,
					int displayWidth, int displayHeight);
	UltrasoundFrame ProcessFrame(double[][] rfData, ScanParameters scanParams);
	void Start();
	void Stop();
	event EventHandler<UltrasoundFrame>? FrameReady;
}
```

### Engine Implementations

| Engine | Mode | Pipeline Extension |
|---|---|---|
| `BModeEngine` | BMode | Canonical pipeline (Section 6) |
| `MModeEngine` | MMode | Single scanline → time-scrolling strip display |
| `ColorDopplerEngine` | ColorDoppler, PowerDoppler | B-Mode base + Kasai autocorrelation velocity overlay |
| `SpectralDopplerEngine` | SpectralDoppler | B-Mode base + FFT spectral strip (OxyPlot) |
| `VolumeEngine` | Volume3D | Multi-slice sweep → HelixToolkit 3D rendering |

### ColorDopplerEngine Architecture

```
RF Data ──► BModeEngine.ProcessFrame() ──► B-Mode background
												│
Ensemble Data ──► DopplerProcessor              │
				   .EstimateColorFlow()          │
				   │                             │
				   ▼                             ▼
		  (velocity, power, variance)     CompositeImages()
				   │                             ▲
				   ▼                             │
		  ScanConverter.ConvertDoppler()  CreateColorOverlay()
```

The `DopplerProcessor` uses the **Kasai autocorrelation estimator**:
1. Wall filter (high-pass) removes clutter from stationary tissue.
2. Autocorrelation at lag 1: `R(1) = Σ z[n] · conj(z[n-1])`
3. Mean velocity: `v = (phase / 2π) · V_nyquist · 2`
4. Power: `P = Σ |z[n]|²`

### Mode Switching

`MainViewModel.SwitchMode()` handles seamless transitions:
1. If scanning → stop acquisition
2. Set `CurrentMode`
3. If was scanning → restart acquisition with new mode

The acquisition loop (`StartScanAsync`) dispatches to the correct engine based on `CurrentMode`.

---

## 8. Hardware Abstraction Layer

### Layer Architecture

```
┌─────────────────────────────────────────────────┐
│                 ProbeManager                     │
│  (connection lifecycle, discovery, health)       │
├────────────────────┬────────────────────────────┤
│   IUltrasoundProbe │   IUltrasoundProbe          │
│   ConvexProbe-     │   PhilipsC51Driver          │
│   Simulator        │                             │
│   (no transport)   │   ┌──────────────────────┐  │
│                    │   │   IProbeTransport     │  │
│                    │   │  ┌──────┐ ┌────────┐ │  │
│                    │   │  │ USB  │ │Ethernet│ │  │
│                    │   │  │Serial│ │Transport│ │  │
│                    │   │  └──────┘ └────────┘ │  │
│                    │   └──────────────────────┘  │
└────────────────────┴────────────────────────────┘
```

### `IUltrasoundProbe`

The primary hardware abstraction — defines the contract every probe (real or simulated) must implement:

```csharp
public interface IUltrasoundProbe : IDisposable
{
	ProbeConfiguration Configuration { get; }
	bool IsConnected { get; }
	bool IsAcquiring { get; }

	Task<bool> ConnectAsync(CancellationToken ct);
	Task DisconnectAsync(CancellationToken ct);
	Task StartAcquisitionAsync(ScanParameters parameters, CancellationToken ct);
	Task StopAcquisitionAsync(CancellationToken ct);
	Task UpdateParametersAsync(ScanParameters parameters, CancellationToken ct);
	Task<double[][]> AcquireFrameAsync(CancellationToken ct);
	Task<double[][][]> AcquireDopplerEnsembleAsync(int ensembleLength, CancellationToken ct);
}
```

### `IProbeTransport`

Low-level data link abstraction — separates physical communication (serial/TCP) from probe protocol logic:

```csharp
public interface IProbeTransport : IDisposable
{
	bool IsOpen { get; }
	event EventHandler<string>? LinkLost;

	Task<bool> OpenAsync(HardwareConnectionConfig config, CancellationToken ct);
	Task CloseAsync(CancellationToken ct);
	Task SendCommandAsync(byte[] commandData, CancellationToken ct);
	Task<byte[]> ReadBytesAsync(int count, CancellationToken ct);
	Task<byte[]> ReadFrameAsync(CancellationToken ct);
	Task<bool> PingAsync(CancellationToken ct);
}
```

### `ProbeManager`

Manages the full connection lifecycle:
1. **SetConfig()** — Configure connection type (Simulator / USB / Ethernet)
2. **ConnectAsync()** — Create transport + driver → handshake → return probe
3. **DisconnectAsync()** — Stop acquisition → disconnect → dispose
4. **DiscoverAsync()** — Scan serial ports (USB VID/PID match) + network broadcast

### `PhilipsC51Driver`

Real hardware driver using a binary command protocol:

| Command ID | Name | Description |
|---|---|---|
| `0x01` | Identify | Handshake — reads probe identity |
| `0x10` | SetMode | Configure imaging mode |
| `0x11` | SetDepth | Set imaging depth |
| `0x12` | SetFrequency | Set transmit frequency |
| `0x13` | SetGain | Set receiver gain |
| `0x14` | SetTgc | Set TGC curve (8 zones) |
| `0x15` | SetFocus | Set focal depths |
| `0x16` | SetDoppler | Configure Doppler PRF, angle, wall filter |
| `0x17` | SetPower | Set transmit power |
| `0x20` | StartAcquisition | Begin continuous frame output |
| `0x21` | StopAcquisition | Stop frame output |
| `0x22` | SingleFrame | Acquire one RF frame |
| `0x23` | DopplerEnsemble | Acquire multi-fire ensemble |

### `ConvexProbeSimulator`

Software simulator that generates realistic convex probe data:
- Produces `double[256][4096]` RF frames (256 scanlines × 4096 samples)
- Synthesizes anatomical features, speckle noise, and Doppler flow patterns
- No transport layer required — data is computed in-process

### Connection Types (`HardwareConnectionConfig`)

| Type | Transport | Settings |
|---|---|---|
| `Simulator` | None | No configuration required |
| `UsbSerial` | `UsbSerialTransport` | COM port, 12 Mbaud, USB VID `0x0471` / PID `0x0C51` |
| `Ethernet` | `EthernetTransport` | IP address, command port `18944`, data port `18945` |

---

## 9. Measurement & Segmentation

### MeasurementTool

Provides calibrated clinical measurements using pixel-to-physical coordinate mapping:

| Method | Input | Output |
|---|---|---|
| `MeasureDistance` | Two points + calibration | Distance in cm |
| `MeasureEllipseArea` | Center + axes + calibration | Area in cm² |
| `MeasureTraceArea` | Point list + calibration | Area in cm² (Shoelace formula) |
| `MeasureVolume` | Three diameters + calibration | Volume in mL (prolate ellipsoid) |
| `MeasureVelocity` | Doppler trace + calibration | Velocity in cm/s |

**Calibration** is computed from `ScanParameters` and `ProbeConfiguration`:
```
pixelsPerCmX = displayWidth / (2 · totalRadius · sin(FOV/2)) × 100
pixelsPerCmY = displayHeight / sectorHeight × 100
```

### SegmentationEngine

Implements three ITK-SNAP–inspired algorithms using **OpenCvSharp** (OpenCV 4.13):

| Algorithm | OpenCV Function | Post-Processing |
|---|---|---|
| Region Growing | `Cv2.FloodFill` | Morphological closing (ellipse 5×5) |
| Level Set | Iterative distance transform + contour evolution | Gaussian blur + threshold |
| Watershed | `Cv2.Watershed` | Marker-based with distance transform |

All algorithms:
1. Accept `byte[]` image data + seed point
2. Return `SegmentationResult` with mask, contour, pixel area
3. Call `CalibrateMeasurements()` to convert pixel area to cm²
4. Auto-persist to server via `ServerApiClient.SaveSegmentationAsync()`

---

## 10. DICOM Integration

### Client-Side (`AuraScan.Core`)

**DicomService** — Facade for all DICOM network operations using fo-dicom 5.2.6:

| Operation | Method | fo-dicom API |
|---|---|---|
| C-STORE SCU | `StoreImageAsync()` | `DicomClientFactory.Create()` → `DicomCStoreRequest` |
| C-ECHO SCU | `EchoAsync()` | `DicomClientFactory.Create()` → `DicomCEchoRequest` |
| Worklist | `QueryWorklistAsync()` | `DicomCFindRequest.CreateWorklistQuery()` |
| Local save | `SaveLocalAsync()` | `dicomFile.SaveAsync()` |

**DicomImageBuilder** — Constructs compliant DICOM US (Ultrasound) objects:
- Sets standard DICOM tags (Patient, Study, Series, Image level)
- Embeds pixel data from `UltrasoundFrame.RenderedImage`
- Generates UIDs via `DicomUIDGenerator`

### Server-Side (`AuraScan.Server`)

**DicomScpHostedService** — ASP.NET Core `IHostedService` that runs an embedded DICOM SCP:

```csharp
public class StorageScpProvider : DicomService,
	IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
```

On C-STORE reception:
1. Save `.dcm` file to `DicomStorage/{callingAE}/{sopInstanceUid}.dcm`
2. Extract metadata → create database entities (Patient → Study → Series → Image)
3. Broadcast `DicomReceived` event via SignalR hub

---

## 11. Server Architecture

### ASP.NET Core Pipeline (`Program.cs`)

```
WebApplication.CreateBuilder()
  │
  ├── Services Registration
  │   ├── DbContext (SQLite)
  │   ├── Scoped services (Patient, Study, Image, Audit, Config)
  │   ├── DicomScpHostedService (background DICOM SCP)
  │   ├── ApiKey authentication scheme
  │   ├── Controllers + SignalR + Swagger
  │   └── CORS (default + "SignalR" policy)
  │
  ├── Build → Auto-migrate database (db.Database.MigrateAsync())
  │
  └── Middleware Pipeline
	  ├── HTTPS redirection (if Security:RequireHttps)
	  ├── Swagger UI (Development only)
	  ├── CORS
	  ├── Authentication → Authorization
	  ├── MapControllers()
	  ├── MapHub<AuraScanHub>("/hubs/aurascan")
	  ├── GET "/" → Redirect to /swagger
	  └── GET "/health" → { Status, Timestamp }
```

### Controller → Service Pattern

```
HTTP Request
  │
  ├── ApiKeyAuthHandler (authenticate X-API-Key header)
  │
  ├── Controller (thin — validation + routing)
  │   │
  │   ├── Service (business logic + EF Core queries)
  │   │   └── AuraScanDbContext (data access)
  │   │
  │   └── AuditService.LogAsync() (auto-populated user/workstation identity)
  │
  └── HTTP Response
```

### Service Interfaces

| Interface | Implementation | Responsibility |
|---|---|---|
| `IPatientService` | `PatientService` | Patient CRUD, search by name/ID |
| `IStudyService` | `StudyService` | Study CRUD, lookup by UID |
| `IImageService` | `ImageService` | Image CRUD, lookup by SOP UID |
| `IAuditService` | `AuditService` | Audit trail logging and querying |
| `IConfigService` | `ConfigService` | System config key-value + DICOM node management |

All services are registered as **Scoped** (one instance per HTTP request).

---

## 12. Authentication & Security

### API Key Authentication (`ApiKeyAuthHandler`)

Custom `AuthenticationHandler<ApiKeyAuthOptions>` that:
1. Bypasses `/health` and `/swagger` endpoints (returns `NoResult`)
2. Reads `X-API-Key` header → compares against `Security:ApiKey` config
3. Reads `X-Workstation-Id` header for identity tracking
4. Creates `ClaimsPrincipal` with claims:
   - `Name` = "ApiKeyClient"
   - `WorkstationId` = header value
   - `Role` = "Workstation"

### Audit Trail

`AuditService.LogAsync()` auto-populates from the authenticated request:
- `UserId` from `ClaimTypes.Name`
- `WorkstationId` from custom "WorkstationId" claim
- `ClientIP` from `HttpContext.Connection.RemoteIpAddress`

### Inactivity Auto-Lock (HIPAA)

`MainViewModel` runs a `DispatcherTimer` (15-minute interval):
- If not actively scanning → `IsScreenLocked = true`
- Any user input calls `ResetInactivityTimer()` to restart the countdown
- Lock overlay covers the UI until `UnlockScreen()` is called

---

## 13. Data Model & Entity Framework

### Entity Hierarchy

```
PatientEntity                    ← DICOM Patient level
  └── StudyEntity                ← DICOM Study level
	   └── SeriesEntity          ← DICOM Series level
			└── ImageEntity      ← DICOM Image level
				 ├── MeasurementEntity
				 └── SegmentationResultEntity

AuditLogEntity                   ← Cross-cutting audit trail
DicomNodeEntity                  ← DICOM network node configuration
SystemConfigEntity               ← Key-value system settings
```

### Relationships & Cascade Behavior

All parent-child relationships use `DeleteBehavior.Cascade`:

```
Patient ──(1:N)──► Study ──(1:N)──► Series ──(1:N)──► Image
														├──(1:N)──► Measurement
														└──(1:N)──► Segmentation
```

### Indexes

| Entity | Indexed Column(s) | Unique |
|---|---|---|
| `PatientEntity` | `PatientId` | Yes |
| `StudyEntity` | `StudyInstanceUid` | Yes |
| `SeriesEntity` | `SeriesInstanceUid` | Yes |
| `ImageEntity` | `SopInstanceUid` | Yes |
| `AuditLogEntity` | `TimestampUtc`, `Action` | No |
| `DicomNodeEntity` | `AeTitle` | No |
| `SystemConfigEntity` | `Key` | Yes |

### DbContext

```csharp
public class AuraScanDbContext : DbContext
{
	public DbSet<PatientEntity> Patients { get; }
	public DbSet<StudyEntity> Studies { get; }
	public DbSet<SeriesEntity> Series { get; }
	public DbSet<ImageEntity> Images { get; }
	public DbSet<MeasurementEntity> Measurements { get; }
	public DbSet<SegmentationResultEntity> Segmentations { get; }
	public DbSet<AuditLogEntity> AuditLogs { get; }
	public DbSet<DicomNodeEntity> DicomNodes { get; }
	public DbSet<SystemConfigEntity> SystemConfigs { get; }
}
```

Database auto-migrates on server startup (`db.Database.MigrateAsync()` in `Program.cs`).

---

## 14. Real-Time Communication (SignalR)

### Hub: `AuraScanHub`

Endpoint: `/hubs/aurascan` (requires `SignalR` CORS policy, supports stateful reconnects).

| Hub Method | Broadcast Target | Payload |
|---|---|---|
| `NotifyImageStored` | `Clients.Others` | `imageId`, `sopUid` |
| `NotifyStudyUpdated` | `Clients.Others` | `studyId`, `studyUid` |
| `NotifyDicomReceived` | `Clients.All` | `callingAe`, `sopUid` |
| `NotifyWorkstationStatus` | `Clients.Others` | `workstationId`, `status` |
| `OnConnectedAsync` | `Clients.Caller` | `connectionId` |

### Client Connection (`ServerApiClient`)

```csharp
_hub = new HubConnectionBuilder()
	.WithUrl($"{baseUrl}/hubs/aurascan", options => {
		options.Headers.Add("X-API-Key", apiKey);
		options.Headers.Add("X-Workstation-Id", workstationId);
	})
	.WithAutomaticReconnect()
	.Build();
```

Client subscribes to all server events and surfaces them via `ServerNotification` event, which the `MainViewModel` displays in the status bar.

---

## 15. Workstation–Server Data Flow

### Image Persistence Flow

```
MainViewModel.DicomStoreAsync()
  │
  ├── 1. DicomService.StoreImageAsync()     ← C-STORE to external PACS
  │
  └── 2. ServerApiClient.PersistAcquisitionAsync()
		  │
		  ├── SavePatientAsync()    → POST /api/patients
		  ├── CreateStudyAsync()    → POST /api/studies
		  ├── CreateSeriesAsync()   → POST /api/studies/{id}/series (via internal logic)
		  └── SaveImageAsync()     → POST /api/images
			  │
			  └── Returns imageId (stored as _lastServerImageId)
```

### Measurement Persistence Flow

```
MainViewModel.SaveMeasurementsToServerAsync()
  │
  └── For each measurement:
	  └── ServerApiClient.SaveMeasurementAsync()
			  → POST /api/measurements { ImageId, Type, Value, Unit, ... }
```

### Error Handling Pattern

`ServerApiClient` methods follow a consistent pattern:
```csharp
try {
	var response = await _http.PostAsJsonAsync("api/...", dto, _jsonOptions);
	response.EnsureSuccessStatusCode();
	return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
} catch (Exception ex) {
	StatusMessage?.Invoke(this, $"Server: {operation} failed — {ex.Message}");
	return null;  // Caller handles gracefully
}
```

---

## 16. Licensing System

### Architecture

```
┌──────────────────┐        ┌────────────────────────────┐
│  LicenseUploader  │───────►│  Azure Blob Storage         │
│  (CLI Tool)       │  PUT   │  aurascan-licenses/         │
└──────────────────┘        │  ├── pending/{key}.json     │
							 │  └── activated/{machine}.json│
┌──────────────────┐        └──────────────┬───────────────┘
│  LicenseService   │◄─────────────────────┘
│  (Workstation)    │  GET/PUT (online mode)
│                   │
│  Fallback: local  │  %LOCALAPPDATA%\AuraScan\license.json
└──────────────────┘
```

### `LicenseService` Modes

| Mode | Condition | Behavior |
|---|---|---|
| Online | `AURASCAN_LICENSE_STORAGE_CONN` env var set | Validate against Azure, enforce seats, write activation blob |
| Offline | Env var not set | Store local-only license at `%LOCALAPPDATA%\AuraScan\license.json` |

### Activation Flow (Online)

1. Read `pending/{key}.json` from Azure Blob
2. Check `MaxActivations` vs current `Activations.Count`
3. If machine already in list → re-issue (no seat consumed)
4. If seats available → add machine to `Activations[]`, upload updated blob
5. Write `activated/{machineId}.json` blob
6. Write local `license.json`
7. Log to `license-activation.log`

### Machine ID

Derived from Windows Registry (`HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`), providing a stable per-machine identifier.

---

## 17. Battery Monitoring

### `BatteryMonitorService`

Uses Win32 P/Invoke (`GetSystemPowerStatus`) to poll system battery state:

```csharp
[DllImport("kernel32.dll")]
private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
```

- Polls every 30 seconds via `System.Timers.Timer`
- Raises `StatusChanged` event with `BatteryStatus` DTO
- `MainViewModel` subscribes → updates UI properties via dispatcher

### Battery Status Model

```
PowerSource:  AC | Battery | Unknown
ChargeStatus: Discharging | Charging | Full | NoBattery | Unknown
Percent:      0–100
Runtime:      TimeSpan? (estimated remaining)
```

### UI Warning Thresholds

| Level | PercentRemaining | StatusText |
|---|---|---|
| Low | ≤ 20% | `⚠ Low battery — Connect AC power` |
| Critical | ≤ 10% | `⚠ CRITICAL BATTERY — Connect AC power immediately` |

---

## 18. Key Abstractions & Interfaces

| Interface | Location | Purpose |
|---|---|---|
| `IImagingEngine` | `Core\Imaging\` | Contract for all imaging mode processors |
| `IUltrasoundProbe` | `Core\Hardware\` | Contract for probe hardware (real or simulated) |
| `IProbeTransport` | `Core\Hardware\` | Contract for physical data link (serial / TCP) |
| `IPatientService` | `Server\Services\` | Patient data operations |
| `IStudyService` | `Server\Services\` | Study data operations |
| `IImageService` | `Server\Services\` | Image data operations |
| `IAuditService` | `Server\Services\` | Audit trail logging |
| `IConfigService` | `Server\Services\` | System configuration & DICOM node management |

---

## 19. Adding a New Imaging Mode

**Example:** Adding an Elastography mode.

### Step 1: Define the enum value

In `AuraScan.Core\Models\Enums.cs`:
```csharp
public enum ImagingMode
{
	BMode,
	MMode,
	ColorDoppler,
	PowerDoppler,
	SpectralDoppler,
	Volume3D,
	Elastography    // ← add
}
```

### Step 2: Create the engine

Create `AuraScan.Core\Core\Imaging\ElastographyEngine.cs` implementing `IImagingEngine`:
```csharp
public sealed class ElastographyEngine : IImagingEngine
{
	public ImagingMode Mode => ImagingMode.Elastography;
	// Implement Initialize(), ProcessFrame(), Start(), Stop(), Dispose()
}
```

### Step 3: Wire into MainViewModel

1. Add a private field: `private readonly ElastographyEngine _elastographyEngine;`
2. Instantiate in the constructor
3. Add a `[RelayCommand]` method: `SetElastography() => SwitchMode(ImagingMode.Elastography);`
4. Add a case in the acquisition loop (`StartScanAsync`) to dispatch to the new engine
5. Add initialization in `InitializeEngines()`

### Step 4: Add the UI button

In `MainWindow.xaml`, add a mode button in the toolbar bound to `SetElastographyCommand`.

---

## 20. Adding a New Probe Driver

**Example:** Adding support for an Acme L12-5 linear probe.

### Step 1: Create the driver

Create `AuraScan.Core\Core\Hardware\AcmeL125Driver.cs` implementing `IUltrasoundProbe`:
```csharp
public sealed class AcmeL125Driver : IUltrasoundProbe
{
	private readonly IProbeTransport _transport;
	public ProbeConfiguration Configuration { get; }
	// Set linear probe specs: ElementCount, pitch, FOV, etc.
	// Implement ConnectAsync, AcquireFrameAsync, etc.
}
```

### Step 2: Add a transport (if needed)

If the probe uses a new communication protocol, implement `IProbeTransport`. Otherwise, reuse `UsbSerialTransport` or `EthernetTransport`.

### Step 3: Register in ProbeManager

Add a case in `ProbeManager.ConnectAsync()` to create the new driver based on hardware identification (USB VID/PID or network discovery).

### Step 4: Update discovery

Add the new probe's USB VID/PID or network signature to `ProbeManager.DiscoverAsync()`.

---

## 21. Adding a New Measurement Type

**Example:** Adding a Heart Rate measurement.

### Step 1: Add the enum value

In `AuraScan.Core\Models\Enums.cs`:
```csharp
public enum MeasurementType
{
	Distance,
	Area,
	EllipseArea,
	TraceArea,
	Volume,
	Velocity,
	HeartRate    // ← add
}
```

### Step 2: Add the measurement method

In `AuraScan.Core\Core\Measurements\MeasurementTool.cs`:
```csharp
public MeasurementResult MeasureHeartRate(List<Point> peaks, double timeScaleSec)
{
	// Calculate BPM from R-R intervals
	var result = new MeasurementResult
	{
		Type = MeasurementType.HeartRate,
		Value = bpm,
		Unit = "BPM",
		Label = $"HR: {bpm:F0} BPM"
	};
	_measurements.Add(result);
	return result;
}
```

### Step 3: Wire into MainViewModel

Add a `[RelayCommand]` to set `ActiveMeasurement = MeasurementType.HeartRate` and handle the image interaction logic in the appropriate click handler.

---

## 22. Build & Test

### Build

```powershell
# Full solution
dotnet build "AuraScan Ultrasound System.slnx" -c Release

# Individual projects
dotnet build "AuraScan Ultrasound System.csproj" -c Release
dotnet build AuraScan.Core\AuraScan.Core.csproj -c Release
dotnet build AuraScan.Server\AuraScan.Server.csproj -c Release
```

### Run

```powershell
# Server (start first)
cd AuraScan.Server
dotnet run

# Workstation
dotnet run --project "AuraScan Ultrasound System.csproj"
```

### Publish (self-contained)

```powershell
dotnet publish "AuraScan Ultrasound System.csproj" -c Release -r win-x64 --self-contained
dotnet publish AuraScan.Server\AuraScan.Server.csproj -c Release -r win-x64 --self-contained
```

### Unsafe Code

Both `AuraScan Ultrasound System` and `AuraScan.Core` enable `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for direct pixel buffer manipulation in `WriteableBitmap` rendering.

---

## 23. Code Conventions

| Convention | Rule |
|---|---|
| **Target framework** | `net8.0-windows` (workstation + core), `net8.0` (server) |
| **Namespace** | `AuraScan_Ultrasound_System` (workstation/core), `AuraScan.Server` (server) |
| **Nullable** | Enabled project-wide |
| **MVVM** | CommunityToolkit.Mvvm source generators — no manual `INotifyPropertyChanged` |
| **Async** | All I/O operations are async with `CancellationToken` support |
| **Parallelism** | `Parallel.For` for CPU-bound DSP (beamforming, envelope, TGC, compression) |
| **Disposal** | All engines, probes, transports, and services implement `IDisposable` |
| **Error events** | Hardware/DICOM errors surface via `EventHandler<string>` events, not exceptions |
| **UI thread** | All property updates from background threads use `Dispatcher.BeginInvoke()` |
| **File-scoped namespaces** | Server project uses file-scoped; Core project uses block-scoped |
| **Records** | Used for DTOs and configuration (e.g., `ConfigSetRequest`, `ScpState`) |

---

**© 2026 Rheacon Systems — All Rights Reserved**

*AuraScan Ultrasound System is a registered product of Rheacon Systems. For internal development use only.*
