# AuraScan Ultrasound System — User Guide

**Version 1.0** | Developed by **Rheacon Systems**

---

## Table of Contents

1. [Overview](#1-overview)
2. [System Requirements](#2-system-requirements)
3. [License Activation](#3-license-activation)
4. [Getting Started](#4-getting-started)
5. [Main Interface](#5-main-interface)
6. [Imaging Modes](#6-imaging-modes)
7. [Scan Controls](#7-scan-controls)
8. [Image Parameters](#8-image-parameters)
9. [Tissue Presets](#9-tissue-presets)
10. [Measurements](#10-measurements)
11. [Segmentation](#11-segmentation)
12. [DICOM Operations](#12-dicom-operations)
13. [Cine Loop](#13-cine-loop)
14. [Probe Connection](#14-probe-connection)
15. [Battery & Portable Power](#15-battery--portable-power)
16. [Server Integration](#16-server-integration)
17. [Security & Compliance](#17-security--compliance)
18. [License Management (Admin)](#18-license-management-admin)
19. [Troubleshooting](#19-troubleshooting)
20. [File Locations](#20-file-locations)
21. [Server REST API Reference](#21-server-rest-api-reference)
22. [SignalR Real-Time Events](#22-signalr-real-time-events)
23. [DICOM SCP Service](#23-dicom-scp-service)
24. [Server Data Model](#24-server-data-model)
25. [Configuration Reference](#25-configuration-reference)
26. [Deployment Guide](#26-deployment-guide)
27. [Glossary](#27-glossary)

---

## 1. Overview

AuraScan Ultrasound System is a medical diagnostic imaging workstation built on .NET 8 and WPF. It provides real-time ultrasound acquisition, advanced imaging modes, DICOM interoperability, measurement tools, image segmentation, and portable battery support — all within a dark-themed, kiosk-style clinical interface.

**Hardware Target:** Philips C5-1 Convex (Curvilinear) Array Probe — 1–5 MHz bandwidth.

### Key Features

- Six imaging modes (B-Mode, M-Mode, Color Doppler, Power Doppler, Spectral Doppler, 3D Volume)
- Real-time beamforming, scan conversion, and signal processing
- Clinical measurement tools (distance, area, volume, velocity)
- ITK-SNAP–inspired segmentation (Region Growing, Level Set, Watershed)
- Full DICOM support (Store, Query, Worklist, Echo)
- Dedicated server with REST API, SignalR, SQLite database, and DICOM SCP
- Azure Blob–backed license activation with seat tracking
- Portable battery status monitoring
- HIPAA-compliant inactivity auto-lock
- Animated splash screen with branded startup sequence

---

## 2. System Requirements

| Component        | Requirement                                    |
|------------------|------------------------------------------------|
| OS               | Windows 10/11 (x64)                            |
| Runtime          | .NET 8.0 Desktop Runtime                       |
| Display          | 1440×900 minimum (1920×1080 recommended)       |
| RAM              | 8 GB minimum, 16 GB recommended                |
| Probe            | Philips C5-1 (USB/Serial or Ethernet), or Simulator |
| Network          | Required for server, DICOM, and license activation |

---

## 3. License Activation

AuraScan requires a valid license key on first launch. The license system uses Azure Blob Storage to manage activation records with seat tracking.

### First-Launch Activation

1. Start the application. The **Activate AuraScan License** window appears.
2. Your **Machine ID** is displayed automatically (read-only, derived from the Windows registry).
3. Enter your **License Key** (e.g., `AURA-DEMO-1234-RHCN-5678`).
4. Optionally enter a **Customer** name.
5. Select a **License Type** (Trial, Standard, or Enterprise).
6. Click **✔ Activate**.

### Activation Modes

| Mode    | When                                       | Behavior                                                                                         |
|---------|--------------------------------------------|--------------------------------------------------------------------------------------------------|
| Online  | `AURASCAN_LICENSE_STORAGE_CONN` env var set | Validates against Azure Blob `pending/{key}.json`, enforces seat limits, writes `activated/{machineId}.json` |
| Offline | Env var not set                            | Stores a local-only license at `%LOCALAPPDATA%\AuraScan\license.json`                            |

### Seat Tracking

Each license key supports a maximum number of device activations (e.g., 5 seats). When activating online:

- The system checks how many machines have already activated under that key.
- If seats remain, the machine is added and the `pending/` blob is updated with the new activation entry.
- The `activated/{machineId}.json` blob and local license both show seat status (e.g., `"2/5 seats used"`).
- If all seats are used, activation is rejected with a message showing the limit.

### Re-Activation

If a machine is already in the activation list, the system recognizes it and re-issues the local license without consuming an additional seat.

---

## 4. Getting Started

### Startup Sequence

1. **License Check** — If no valid local license exists, the activation window appears (see [License Activation](#3-license-activation)).
2. **Splash Screen** — An animated AuraScan splash screen displays for approximately 60 seconds with phased status messages:
   - DICOM framework initialization
   - Signal processing and imaging engine loading
   - Beamformer calibration and scan conversion
   - Hardware detection and probe setup
   - Segmentation and measurement tool loading
   - Security configuration and server connection
   - DICOM network verification and patient database sync
   - Audit logging and UI preparation
3. **Main Window** — The fullscreen kiosk-style workstation interface opens.

### Quick Start

1. Select a **Tissue Preset** from the toolbar (e.g., Abdomen).
2. Click **▶ Scan** to begin acquisition.
3. Use the right panel sliders to adjust Gain, Depth, Frequency, etc.
4. Click **⏸ Freeze** to pause the image for review.
5. Use measurement or segmentation tools on the frozen image.
6. Click **💾 Store** in the DICOM panel to archive the image.

---

## 5. Main Interface

The interface is divided into four horizontal zones:

### Top Bar

| Element             | Description                                                       |
|---------------------|-------------------------------------------------------------------|
| AuraScan Logo       | Brand identifier (left)                                           |
| Patient Info        | Patient name and ID                                               |
| Probe Info          | Connected probe model, type, and frequency range                  |
| Safety Indices      | MI (Mechanical Index) and TI (Thermal Index) — real-time values   |
| Battery Indicator   | Battery icon, percentage, and remaining time (visible when on battery) |
| ✕ Close Button      | Exit the application                                              |

### Mode Toolbar

Imaging mode buttons (B, M, Color, Power, PW, 3D), scan controls (Scan, Freeze, Stop), tissue presets, frame rate display, and current mode indicator.

### Main Content Area

- **Left**: Ultrasound image display (640×480 rendered)
- **Right**: Scrollable control panel with probe connection, image parameters, TGC, Doppler, measurements, segmentation, and DICOM tools

### Status Bar

Acquisition state indicator (color-coded dot), status messages, and a summary strip showing version, mode, depth, frequency, and frame rate.

---

## 6. Imaging Modes

| Button  | Mode                | Description                                                    |
|---------|---------------------|----------------------------------------------------------------|
| **B**   | B-Mode              | Standard 2D grayscale brightness-mode imaging                  |
| **M**   | M-Mode              | Motion mode — time-based display of structures along one scan line |
| **Color** | Color Flow Doppler | Color-encoded blood flow velocity overlay on B-Mode            |
| **Power** | Power Doppler     | Amplitude-based flow detection (direction-insensitive)         |
| **PW**  | Spectral Doppler    | Pulsed-wave spectral display with FFT velocity analysis        |
| **3D**  | 3D Volume           | Volume rendering from acquired sweep data                      |

Switch modes by clicking the corresponding button in the toolbar. If scanning is active, the system automatically stops, switches modes, and restarts acquisition.

---

## 7. Scan Controls

| Button       | Action                                        |
|--------------|-----------------------------------------------|
| **▶ Scan**   | Start real-time acquisition                   |
| **⏸ Freeze** | Pause/resume the live image                   |
| **⏹ Stop**   | Stop acquisition completely                   |

The status bar indicator changes color based on state:

| Color  | State    |
|--------|----------|
| Green  | Scanning |
| Yellow | Frozen   |
| Gray   | Idle     |

---

## 8. Image Parameters

All parameters are adjustable in real time via the right-side control panel. Changes take effect on the next acquired frame.

| Parameter         | Range           | Description                                      |
|-------------------|-----------------|--------------------------------------------------|
| Gain              | 0–100 dB        | Overall receiver amplification                   |
| Depth             | 2–30 cm         | Imaging depth (also adjustable via +/− buttons)  |
| Dynamic Range     | 30–90 dB        | Grayscale compression range                      |
| Frequency         | 1.0–5.0 MHz     | Transmit center frequency                        |
| Persistence       | 0–7             | Temporal frame averaging (higher = smoother)     |
| Sector Angle      | 30°–90°         | Convex sector width                              |
| Harmonic Imaging  | On/Off          | Tissue harmonic mode toggle                      |

### TGC (Time Gain Compensation)

Eight slider zones (near-field to far-field) allow depth-dependent gain equalization. Each slider ranges from 0.0 to 1.0.

### Doppler Controls

| Parameter        | Range             | Description                              |
|------------------|-------------------|------------------------------------------|
| PRF              | 1000–10000 Hz     | Pulse repetition frequency               |
| Angle Correction | 0°–80°            | Doppler angle correction for velocity    |

---

## 9. Tissue Presets

Presets automatically configure depth, frequency, gain, and dynamic range for specific clinical applications.

| Preset      | Depth | Frequency | Gain | Dynamic Range |
|-------------|-------|-----------|------|---------------|
| Abdomen     | 20 cm | 3.5 MHz   | 50   | 60 dB         |
| OB/GYN      | 18 cm | 3.5 MHz   | 55   | 55 dB         |
| Vascular    | 6 cm  | 5.0 MHz   | 45   | 65 dB         |
| Cardiac     | 16 cm | 2.5 MHz   | 50   | 55 dB         |
| Small Parts | 5 cm  | 5.0 MHz   | 50   | 60 dB         |
| Renal       | 15 cm | 3.5 MHz   | 50   | 60 dB         |

Select a preset from the **Preset** dropdown in the mode toolbar.

---

## 10. Measurements

Available when the image is frozen. Select a measurement type, then interact with the image to place points.

| Tool        | Description                                           |
|-------------|-------------------------------------------------------|
| 📏 **Dist** | Distance measurement between two points               |
| ⭕ **Area** | Area measurement (region outline)                     |
| 📦 **Vol**  | Volume estimation (ellipsoid or multi-plane)          |
| 🔄 **Vel**  | Velocity measurement (Doppler spectral trace)         |

### Controls

- **Undo** — Remove the last measurement
- **Clear** — Remove all measurements

Measurements can be saved to the AuraScan Server via the **Save Measurements to Server** command (requires active server connection and a stored image).

---

## 11. Segmentation

ITK-SNAP–inspired segmentation algorithms for automated region detection. Available when the image is frozen.

| Algorithm                  | Description                                                         |
|----------------------------|---------------------------------------------------------------------|
| Region Growing             | Seed-based expansion using intensity similarity                     |
| Level Set (Active Contour) | Edge-driven evolving contour for complex boundaries                 |
| Watershed                  | Gradient-based watershed segmentation                               |

### How to Use

1. Freeze the image.
2. Select an algorithm from the **Segmentation** dropdown.
3. Click on the image to set the seed point.
4. The segmented region is computed with area measurement (cm²) and displayed in the status bar.
5. Results are automatically persisted to the server if connected.

---

## 12. DICOM Operations

| Button       | Operation                                                     |
|--------------|---------------------------------------------------------------|
| 💾 **Store** | Send the current frame to a configured DICOM server (C-STORE) |
| 📁 **Save**  | Save the current frame as a local `.dcm` file                 |
| 🔍 **Echo**  | Test DICOM connectivity (C-ECHO)                              |
| 📋 **MWL**   | Query the Modality Worklist for scheduled procedures          |

DICOM server configuration (AE Title, Host, Port) is managed via the server settings. The workstation also hosts a DICOM SCP via the AuraScan Server for receiving images.

---

## 13. Cine Loop

Record and replay sequences of acquired frames.

| Action            | Description                                   |
|-------------------|-----------------------------------------------|
| Record            | Begin capturing frames to the cine buffer     |
| Play              | Play back recorded frames at ~30 fps          |
| Stop              | Stop recording or playback                    |
| Step Forward (▶)  | Advance one frame                             |
| Step Back (◀)     | Go back one frame                             |

Maximum cine buffer: **300 frames**.

---

## 14. Probe Connection

### Connection Sources

| Source           | Description                                               |
|------------------|-----------------------------------------------------------|
| **Simulator**    | Built-in convex probe simulator for testing                |
| **USB / Serial** | Physical probe via serial port (select port from dropdown) |
| **Ethernet / TCP** | Network-connected probe via IP address                  |

### Connection Workflow

1. Select the connection source from the **Probe Source** dropdown.
2. For USB: select the serial port and click **↻** to refresh available ports.
3. For Ethernet: enter the probe's IP address.
4. Click **🔌 Connect**. The status indicator shows connection state:
   - 🟢 Green — Connected
   - 🟡 Yellow — Connecting / Reconnecting
   - 🔴 Red — Error
   - ⚪ Gray — Disconnected
5. Click **🔍 Discover** to auto-scan for available probes.
6. Click **⏏ Disconnect** to disconnect.

The default connection is the built-in **Simulator**, which generates realistic convex probe data for demonstration and testing.

---

## 15. Battery & Portable Power

When the ultrasound system is running on battery power (not connected to AC), a battery indicator appears in the top bar.

### Battery Indicator

| Element          | Description                                         |
|------------------|-----------------------------------------------------|
| Battery Icon     | Graphical bar showing charge level (proportional fill) |
| Percentage       | Numeric display (e.g., `72%`)                       |
| Color            | Green (>50%), Yellow (21–50%), Orange (11–20%), Red (≤10%) |
| ⚡ Charging Icon | Visible when plugged in and charging                |
| Time Remaining   | Estimated runtime (e.g., `2h 15m remaining`)        |

### Battery Warnings

| Level     | Action                                                            |
|-----------|-------------------------------------------------------------------|
| ≤ 20%     | Status bar warning: "⚠ Low battery — Connect AC power"           |
| ≤ 10%     | Status bar critical alert: "⚠ CRITICAL BATTERY — Connect AC power immediately" |

The battery indicator is **hidden** when the system is on AC power. Polling occurs every 30 seconds.

---

## 16. Server Integration

AuraScan includes a dedicated server project (`AuraScan.Server`) for persistent data storage, real-time notifications, and DICOM network services.

### Server Components

| Component       | Technology        | Description                                     |
|-----------------|-------------------|-------------------------------------------------|
| REST API        | ASP.NET Core      | Patient, study, image, measurement, and config CRUD |
| SignalR Hub     | ASP.NET SignalR   | Real-time workstation notifications              |
| Database        | SQLite (EF Core)  | Persistent storage for all clinical data         |
| DICOM SCP       | fo-dicom           | Hosted DICOM server for receiving images          |
| Swagger UI      | OpenAPI            | Interactive API documentation at `/swagger`       |

### Starting the Server

```bash
cd AuraScan.Server
dotnet run
```

The server starts at `https://localhost:5001` (or configured URL). The workstation auto-connects to the server on startup.

### API Authentication

All API requests require the `X-API-Key` header. The key is configured in `appsettings.json` under `Security:ApiKey`. The workstation automatically includes this header.

### Health Check

```
GET /health
```

Returns `{ "Status": "Healthy", "Timestamp": "..." }`.

---

## 17. Security & Compliance

AuraScan includes security features aligned with medical imaging regulatory practices.

### Features

| Feature                  | Description                                                          |
|--------------------------|----------------------------------------------------------------------|
| API Key Authentication   | All server endpoints require `X-API-Key` header                      |
| CORS Restriction         | Configurable allowed origins (`Security:AllowedOrigins`)             |
| HTTPS Support            | Optional HTTPS redirection (`Security:RequireHttps`)                 |
| Inactivity Auto-Lock     | Screen locks after 15 minutes of inactivity (HIPAA §164.312(a)(2)(iii)) |
| Audit Logging            | All operations logged with user/workstation identity and client IP    |
| Workstation Identity     | `X-Workstation-Id` header sent with every request                    |

### Screen Lock

- The screen auto-locks after **15 minutes** of inactivity (when not actively scanning).
- Click **Unlock** to resume. Any mouse/keyboard/touch input resets the inactivity timer.

### Audit Trail

The server captures audit entries including:
- User and workstation identity (from authentication claims)
- Operation type and timestamp
- Client IP address
- Detailed context (study ID, patient ID, etc.)

---

## 18. License Management (Admin)

### Creating License Keys

Use the **LicenseUploader** tool to generate and upload pending license records to Azure Blob Storage.

```bash
cd Tools/LicenseUploader
dotnet run -- "<AzureConnectionString>" "<LicenseKey>" "<Customer>" "<LicenseType>" "<ExpiryDate>" "<MaxActivations>"
```

**Example:**

```bash
dotnet run -- "DefaultEndpointsProtocol=https;..." "AURA-PROD-0001-CORP-9999" "Hospital ABC" "Enterprise" "2027-12-31" "10"
```

This creates `pending/AURA-PROD-0001-CORP-9999.json` in the `aurascan-licenses` container with:
- Customer name
- License type
- Expiry date
- Maximum activations (seat limit)
- Empty activations array

### Azure Blob Container Structure

```
aurascan-licenses/
├── pending/
│   └── {LicenseKey}.json          ← Master license record (includes Activations[])
└── activated/
	└── {MachineId}.json           ← Per-machine activation record with seat status
```

### Pending Record Schema

```json
{
  "LicenseKey": "AURA-PROD-0001-CORP-9999",
  "Customer": "Hospital ABC",
  "LicenseType": "Enterprise",
  "IssuedAt": "2026-06-17T00:00:00Z",
  "ExpiresAt": "2027-12-31T00:00:00Z",
  "MaxActivations": 10,
  "Activations": [
	{
	  "MachineId": "a31b993b-...",
	  "MachineName": "US-WORKSTATION-01",
	  "ActivatedAt": "2026-06-17T05:15:00Z"
	}
  ]
}
```

### Activated Record Schema

```json
{
  "LicenseKey": "AURA-PROD-0001-CORP-9999",
  "MachineId": "a31b993b-...",
  "Customer": "Hospital ABC",
  "LicenseType": "Enterprise",
  "IssuedAt": "2026-06-17T00:00:00Z",
  "ExpiresAt": "2027-12-31T00:00:00Z",
  "MaxActivations": 10,
  "SeatStatus": "1/10 seats used"
}
```

### Environment Variable

Set the Azure Blob Storage connection string as a User or System environment variable:

| Variable                         | Value                                        |
|----------------------------------|----------------------------------------------|
| `AURASCAN_LICENSE_STORAGE_CONN`  | Azure Storage account connection string      |

**Important:** Restart Visual Studio (or the application host) after setting the environment variable so the process inherits it.

---

## 19. Troubleshooting

### License Activation

| Symptom                                  | Cause                                             | Fix                                                                                          |
|------------------------------------------|---------------------------------------------------|----------------------------------------------------------------------------------------------|
| "Activation stored locally (offline)"    | `AURASCAN_LICENSE_STORAGE_CONN` env var not set    | Set the env var and restart Visual Studio / the app                                          |
| "Activation key not found on server"     | Blob not at `pending/{key}.json`                   | Upload with correct path using LicenseUploader or Azure Portal                               |
| "Activation limit reached"              | All seats consumed                                 | Increase `MaxActivations` in the pending blob or deactivate a machine                        |
| Activation succeeded but shows "Local"   | App ran in offline mode despite env var being set   | Delete `%LOCALAPPDATA%\AuraScan\license.json` and restart the app after ensuring env var is set |

### Activation Logs

All activation attempts are logged to:

```
%LOCALAPPDATA%\AuraScan\license-activation.log
```

Each entry includes timestamp, machine ID, key (masked), online/offline mode, success/failure, and the reason message.

### General Issues

| Symptom                            | Fix                                                              |
|------------------------------------|------------------------------------------------------------------|
| App closes immediately after splash | Check for startup exceptions in Output window (Debug pane)       |
| Server connection failed           | Ensure the server is running (`dotnet run` in AuraScan.Server)   |
| DICOM store fails                  | Verify DICOM server config (AE Title, Host, Port) and run Echo   |
| Probe connection error             | Check USB cable / network, refresh serial ports, verify IP       |
| Battery indicator not showing      | Indicator only appears when running on battery (not on AC)       |
| Screen is locked                   | Click Unlock — auto-lock triggers after 15 min inactivity        |

---

## 20. File Locations

| File / Path                                          | Purpose                                        |
|------------------------------------------------------|------------------------------------------------|
| `%LOCALAPPDATA%\AuraScan\license.json`               | Local license activation record                |
| `%LOCALAPPDATA%\AuraScan\license-activation.log`     | Activation attempt audit log                   |
| `AuraScan.Server\AuraScan.db`                        | SQLite database (patients, studies, images)     |
| `AuraScan.Server\appsettings.json`                   | Server configuration (API key, CORS, DICOM SCP)|
| `Tools\LicenseUploader\`                             | License key upload tool                        |

---

## 21. Server REST API Reference

All endpoints require the `X-API-Key` header. Base URL: `http://localhost:59675` (default).

### Patients

| Method   | Endpoint                                | Description                        |
|----------|-----------------------------------------|------------------------------------|
| `GET`    | `/api/patients?name=&patientId=&skip=0&take=50` | Search patients by name or ID      |
| `GET`    | `/api/patients/{id}`                    | Get patient by internal ID         |
| `GET`    | `/api/patients/by-patient-id/{patientId}` | Get patient by DICOM Patient ID  |
| `POST`   | `/api/patients`                         | Create or update a patient         |
| `DELETE` | `/api/patients/{id}`                    | Delete a patient                   |

### Studies

| Method   | Endpoint                                | Description                        |
|----------|-----------------------------------------|------------------------------------|
| `GET`    | `/api/studies/{id}`                     | Get study by internal ID           |
| `GET`    | `/api/studies/by-uid/{uid}`             | Get study by Study Instance UID    |
| `GET`    | `/api/studies/by-patient/{patientId}`   | List studies for a patient         |
| `POST`   | `/api/studies`                          | Create a new study                 |
| `DELETE` | `/api/studies/{id}`                     | Delete a study                     |

### Images

| Method   | Endpoint                                | Description                        |
|----------|-----------------------------------------|------------------------------------|
| `GET`    | `/api/images/{id}`                      | Get image by internal ID           |
| `GET`    | `/api/images/by-sop/{sopUid}`           | Get image by SOP Instance UID      |
| `GET`    | `/api/images/by-series/{seriesId}`      | List images in a series            |
| `POST`   | `/api/images`                           | Store a new image record           |
| `DELETE` | `/api/images/{id}`                      | Delete an image                    |

### Measurements

| Method   | Endpoint                                | Description                        |
|----------|-----------------------------------------|------------------------------------|
| `GET`    | `/api/measurements/by-image/{imageId}`  | List measurements for an image     |
| `POST`   | `/api/measurements`                     | Save a new measurement             |
| `DELETE` | `/api/measurements/{id}`                | Delete a measurement               |

### Configuration

| Method   | Endpoint                                | Description                        |
|----------|-----------------------------------------|------------------------------------|
| `GET`    | `/api/config/{key}`                     | Get a system configuration value   |
| `PUT`    | `/api/config/{key}`                     | Set a configuration value (body: `{ "Value": "...", "Category": "...", "Description": "..." }`) |
| `GET`    | `/api/config/category/{category}`       | List configs by category           |
| `GET`    | `/api/config/dicom-nodes`               | List configured DICOM remote nodes |
| `POST`   | `/api/config/dicom-nodes`               | Add or update a DICOM node         |
| `DELETE` | `/api/config/dicom-nodes/{id}`          | Remove a DICOM node                |

### Audit

| Method   | Endpoint                                | Description                        |
|----------|-----------------------------------------|------------------------------------|
| `GET`    | `/api/audit?from=&to=&action=&skip=0&take=100` | Query the audit trail        |

### System

| Method   | Endpoint   | Description                              |
|----------|------------|------------------------------------------|
| `GET`    | `/health`  | Health check — returns `{ "Status": "Healthy", "Timestamp": "..." }` |
| `GET`    | `/`        | Redirects to Swagger UI                  |
| `GET`    | `/swagger` | Interactive OpenAPI documentation        |

---

## 22. SignalR Real-Time Events

The workstation connects to the server's SignalR hub at `/hubs/aurascan` for real-time notifications. The hub uses the `SignalR` CORS policy and supports stateful reconnects.

### Connection

The workstation automatically connects on startup (when `AutoConnect` is enabled). The `X-API-Key` and `X-Workstation-Id` headers are included in the handshake.

### Server → Client Events

| Event               | Parameters                          | Description                                    |
|---------------------|-------------------------------------|------------------------------------------------|
| `Connected`         | `connectionId` (string)             | Sent to the caller on initial connection        |
| `ImageStored`       | `imageId` (int), `sopUid` (string)  | Broadcast when an image is stored via API       |
| `StudyUpdated`      | `studyId` (int), `studyUid` (string)| Broadcast when a study is created or updated    |
| `DicomReceived`     | `callingAe` (string), `sopUid` (string) | Broadcast when the DICOM SCP receives an image |
| `WorkstationStatus` | `workstationId` (string), `status` (string) | Status updates from other connected workstations |

### Client → Server Methods

| Method                    | Parameters                            | Description                                |
|---------------------------|---------------------------------------|--------------------------------------------|
| `NotifyImageStored`       | `imageId` (int), `sopUid` (string)    | Notify other workstations of a stored image |
| `NotifyStudyUpdated`      | `studyId` (int), `studyUid` (string)  | Notify other workstations of a study update |
| `NotifyDicomReceived`     | `callingAe` (string), `sopUid` (string) | Signal DICOM reception to all clients     |
| `NotifyWorkstationStatus` | `workstationId` (string), `status` (string) | Announce workstation status changes    |

### Reconnection

The client uses automatic reconnect. Status events:

- **Reconnecting** — Connection lost; attempting to restore.
- **Reconnected** — Connection restored.
- **Closed** — Connection terminated.

---

## 23. DICOM SCP Service

The AuraScan Server hosts an embedded DICOM SCP (Service Class Provider) that can receive images from external DICOM sources such as ultrasound machines, PACS, or other workstations.

### Configuration

DICOM SCP settings are in `AuraScan.Server/appsettings.json`:

```json
{
  "DicomScp": {
    "Port": 11112,
    "AeTitle": "AURASCAN_SCP",
    "StoragePath": "DicomStorage"
  }
}
```

| Setting       | Default          | Description                                      |
|---------------|------------------|--------------------------------------------------|
| `Port`        | `11112`          | TCP port the DICOM SCP listens on                |
| `AeTitle`     | `AURASCAN_SCP`   | Application Entity title advertised by the SCP   |
| `StoragePath` | `DicomStorage`   | Directory where received DICOM files are saved   |

### Supported Services

| SOP Class       | Description                                                |
|-----------------|------------------------------------------------------------|
| C-STORE         | Accept and store incoming DICOM images                     |
| C-ECHO          | Respond to DICOM verification (connectivity test) requests |

### Behavior

1. The SCP starts automatically when the server launches.
2. When a C-STORE request is received, the DICOM file is saved to `StoragePath/{callingAE}/{sopInstanceUid}.dcm`.
3. The image metadata is recorded in the SQLite database.
4. A `DicomReceived` event is broadcast to all connected workstations via SignalR.
5. Association requests are accepted for all calling AE titles.

### Testing Connectivity

From any DICOM SCU (Service Class User) or the AuraScan workstation:

1. Configure a DICOM node with AE Title `AURASCAN_SCP`, Host `localhost`, Port `11112`.
2. Use the **🔍 Echo** button in the DICOM panel to verify connectivity.
3. A successful C-ECHO returns a confirmation in the status bar.

---

## 24. Server Data Model

The AuraScan Server uses a relational database (SQLite with Entity Framework Core) organized in a DICOM-aligned hierarchy.

### Entity Hierarchy

```
Patient
 └── Study
      └── Series
           └── Image
                └── Measurement
```

### Patient

| Field               | Type       | Constraints         | Description                      |
|---------------------|------------|---------------------|----------------------------------|
| `Id`                | int        | PK, auto-increment  | Internal identifier              |
| `PatientId`         | string     | Required, max 64    | DICOM Patient ID                 |
| `PatientName`       | string     | Required, max 256   | Patient full name                |
| `DateOfBirth`       | DateTime?  |                     | Date of birth                    |
| `Sex`               | string?    | Max 16              | Sex (M, F, O)                    |
| `WeightKg`          | double?    | 0–500               | Body weight in kilograms         |
| `AccessionNumber`   | string?    | Max 64              | Accession number                 |
| `ReferringPhysician`| string?    | Max 256             | Referring physician name         |
| `InstitutionName`   | string?    | Max 256             | Institution / facility name      |
| `CreatedUtc`        | DateTime   |                     | Record creation timestamp        |
| `UpdatedUtc`        | DateTime   |                     | Last update timestamp            |

### Study

| Field                | Type       | Constraints         | Description                      |
|----------------------|------------|---------------------|----------------------------------|
| `Id`                 | int        | PK, auto-increment  | Internal identifier              |
| `StudyInstanceUid`   | string     | Required, max 128   | DICOM Study Instance UID         |
| `StudyDescription`   | string?    | Max 256             | Study description                |
| `StudyDateTime`      | DateTime   |                     | Date/time of the study           |
| `PerformingPhysician`| string?    | Max 256             | Performing physician             |
| `AccessionNumber`    | string?    | Max 64              | Accession number                 |
| `PatientId`          | int        | FK → Patient        | Parent patient                   |
| `CreatedUtc`         | DateTime   |                     | Record creation timestamp        |

### Series

| Field                | Type       | Constraints         | Description                      |
|----------------------|------------|---------------------|----------------------------------|
| `Id`                 | int        | PK, auto-increment  | Internal identifier              |
| `SeriesInstanceUid`  | string     |                     | DICOM Series Instance UID        |
| `SeriesDescription`  | string?    |                     | Series description               |
| `Modality`           | string?    | Default `US`        | DICOM modality code              |
| `SeriesNumber`       | int        |                     | Series number within the study   |
| `BodyPartExamined`   | string?    |                     | Anatomical region                |
| `StudyId`            | int        | FK → Study          | Parent study                     |
| `CreatedUtc`         | DateTime   |                     | Record creation timestamp        |

### Image

| Field                    | Type       | Constraints         | Description                      |
|--------------------------|------------|---------------------|----------------------------------|
| `Id`                     | int        | PK, auto-increment  | Internal identifier              |
| `SopInstanceUid`         | string     | Required, max 128   | DICOM SOP Instance UID           |
| `InstanceNumber`         | int        |                     | Instance number within series    |
| `ImagingMode`            | string     | Required, max 32    | Mode (BMode, MMode, etc.)        |
| `Width`                  | int        | 1–10000             | Image width in pixels            |
| `Height`                 | int        | 1–10000             | Image height in pixels           |
| `FrameRate`              | double     | 0–1000              | Acquisition frame rate (fps)     |
| `MechanicalIndex`        | double     | 0–10                | Mechanical Index (MI)            |
| `ThermalIndex`           | double     | 0–10                | Thermal Index (TI)               |
| `DepthCm`                | double     | 0–100               | Imaging depth in centimeters     |
| `FrequencyMHz`           | double     | 0–100               | Transmit frequency in MHz        |
| `DicomFilePath`          | string?    | Max 512             | Path to stored DICOM file        |
| `ThumbnailPath`          | string?    | Max 512             | Path to thumbnail image          |
| `AcquisitionDateTimeUtc` | DateTime   |                     | Frame acquisition timestamp      |
| `SeriesId`               | int        | FK → Series         | Parent series                    |
| `CreatedUtc`             | DateTime   |                     | Record creation timestamp        |

### Measurement

| Field           | Type       | Constraints         | Description                      |
|-----------------|------------|---------------------|----------------------------------|
| `Id`            | int        | PK, auto-increment  | Internal identifier              |
| `Type`          | string     |                     | Measurement type (Distance, Area, Volume, Velocity) |
| `Value`         | double     |                     | Measured value                   |
| `Unit`          | string     |                     | Unit of measure (cm, cm², mL, cm/s) |
| `Label`         | string?    |                     | Optional label                   |
| `StartX`        | double?    |                     | Start point X coordinate         |
| `StartY`        | double?    |                     | Start point Y coordinate         |
| `EndX`          | double?    |                     | End point X coordinate           |
| `EndY`          | double?    |                     | End point Y coordinate           |
| `MeasuredAtUtc`  | DateTime   |                     | When the measurement was taken   |
| `ImageId`       | int        | FK → Image          | Parent image                     |
| `CreatedUtc`    | DateTime   |                     | Record creation timestamp        |

### DICOM Node

| Field         | Type       | Constraints         | Description                      |
|---------------|------------|---------------------|----------------------------------|
| `Id`          | int        | PK, auto-increment  | Internal identifier              |
| `Name`        | string     | Required, max 128   | Friendly name                    |
| `AeTitle`     | string     | Required, max 16    | DICOM AE Title                   |
| `Host`        | string     | Required, max 256   | Hostname or IP address           |
| `Port`        | int        | 1–65535             | DICOM port                       |
| `NodeType`    | string     | Required, max 16    | `SCP` or `SCU`                   |
| `IsEnabled`   | bool       | Default `true`      | Whether the node is active       |
| `CreatedUtc`  | DateTime   |                     | Record creation timestamp        |
| `UpdatedUtc`  | DateTime   |                     | Last update timestamp            |

---

## 25. Configuration Reference

### Server Configuration (`AuraScan.Server/appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "AuraScanDb": "Data Source=AuraScan.db"
  },
  "DicomScp": {
    "Port": 11112,
    "AeTitle": "AURASCAN_SCP",
    "StoragePath": "DicomStorage"
  },
  "Security": {
    "ApiKey": "AuraScan-2024-SecureKey-Change-In-Production",
    "AllowedOrigins": [ "https://localhost:59674", "http://localhost:59675" ],
    "RequireHttps": false,
    "SessionTimeoutMinutes": 15
  }
}
```

| Key                              | Type     | Default                                              | Description                               |
|----------------------------------|----------|------------------------------------------------------|-------------------------------------------|
| `ConnectionStrings:AuraScanDb`   | string   | `Data Source=AuraScan.db`                            | SQLite database connection string         |
| `DicomScp:Port`                  | int      | `11112`                                              | DICOM SCP listener port                   |
| `DicomScp:AeTitle`               | string   | `AURASCAN_SCP`                                       | DICOM SCP Application Entity title        |
| `DicomScp:StoragePath`           | string   | `DicomStorage`                                       | Directory for received DICOM files        |
| `Security:ApiKey`                | string   | `AuraScan-2024-SecureKey-Change-In-Production`       | API key for authenticating requests       |
| `Security:AllowedOrigins`        | string[] | `["https://localhost:59674", "http://localhost:59675"]` | CORS allowed origins                    |
| `Security:RequireHttps`          | bool     | `false`                                              | Enable HTTPS redirection                  |
| `Security:SessionTimeoutMinutes` | int      | `15`                                                 | Inactivity lock timeout (minutes)         |

### Workstation Configuration (`ServerConfig`)

The workstation connects to the server using these defaults (configurable in code):

| Property         | Default                        | Description                          |
|------------------|--------------------------------|--------------------------------------|
| `BaseUrl`        | `http://localhost:59675`       | Server base URL                      |
| `ApiKey`         | `AuraScan-Dev-Key-12345`       | API key (must match server config)   |
| `WorkstationId`  | `Environment.MachineName`      | Unique workstation identifier        |
| `AutoConnect`    | `true`                         | Connect to server on startup         |
| `TimeoutSeconds` | `10`                           | HTTP request timeout                 |

### Environment Variables

| Variable                         | Purpose                                              |
|----------------------------------|------------------------------------------------------|
| `AURASCAN_LICENSE_STORAGE_CONN`  | Azure Blob Storage connection string for online license activation |

---

## 26. Deployment Guide

### Prerequisites

1. Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building) or the .NET 8.0 Desktop Runtime (for running).
2. Windows 10/11 x64.

### Building the Solution

```bash
dotnet build "AuraScan Ultrasound System.slnx" --configuration Release
```

### Running the Server

```bash
cd AuraScan.Server
dotnet run --configuration Release
```

The server starts at the configured URL (default `http://localhost:59675`) and automatically creates and migrates the SQLite database on first run.

### Running the Workstation

```bash
dotnet run --project "AuraScan Ultrasound System.csproj" --configuration Release
```

Or launch the compiled executable directly from the `bin/Release/net8.0-windows/` directory.

### Publishing a Self-Contained Deployment

```bash
dotnet publish "AuraScan Ultrasound System.csproj" -c Release -r win-x64 --self-contained -o ./publish/workstation
dotnet publish AuraScan.Server/AuraScan.Server.csproj -c Release -r win-x64 --self-contained -o ./publish/server
```

### Deployment Checklist

| Step | Action                                                                                     |
|------|--------------------------------------------------------------------------------------------|
| 1    | Build and publish both the workstation and server in Release mode                          |
| 2    | Set the `AURASCAN_LICENSE_STORAGE_CONN` environment variable (if using online licensing)   |
| 3    | Upload license keys using the `LicenseUploader` tool                                      |
| 4    | Update `appsettings.json` — change `Security:ApiKey` to a production value                |
| 5    | Update `Security:AllowedOrigins` with the production workstation URLs                     |
| 6    | Set `Security:RequireHttps` to `true` for production environments                         |
| 7    | Configure firewall rules for the DICOM SCP port (default `11112`)                         |
| 8    | Start the server, then start the workstation                                              |
| 9    | Activate the license on first launch                                                      |
| 10   | Verify DICOM connectivity using the Echo function                                         |

### Folder Structure After Deployment

```
AuraScan/
├── workstation/
│   ├── AuraScan Ultrasound System.exe
│   └── ...
├── server/
│   ├── AuraScan.Server.exe
│   ├── appsettings.json
│   ├── AuraScan.db              ← Created on first run
│   └── DicomStorage/            ← Received DICOM files
└── Tools/
    └── LicenseUploader/
```

---

## 27. Glossary

| Term                  | Definition                                                                                  |
|-----------------------|---------------------------------------------------------------------------------------------|
| **AE Title**          | Application Entity Title — a unique identifier for a DICOM network node                     |
| **B-Mode**            | Brightness Mode — standard 2D grayscale ultrasound imaging                                  |
| **Beamforming**       | Signal processing technique that focuses ultrasound transmit/receive beams                   |
| **C-ECHO**            | DICOM verification service — a "ping" to test DICOM network connectivity                    |
| **C-STORE**           | DICOM storage service — sends or receives DICOM image objects                                |
| **Cine Loop**         | A recorded sequence of ultrasound frames for playback review                                 |
| **Color Doppler**     | Imaging mode that overlays color-encoded blood flow velocity on B-Mode                       |
| **Convex Probe**      | A curvilinear transducer that produces a sector-shaped (fan) image                           |
| **DICOM**             | Digital Imaging and Communications in Medicine — the international standard for medical images |
| **Dynamic Range**     | The range of echo amplitudes displayed as grayscale levels (in dB)                           |
| **FFT**               | Fast Fourier Transform — used to compute the frequency spectrum in spectral Doppler          |
| **Gain**              | Amplification of the received ultrasound signal (in dB)                                      |
| **Harmonic Imaging**  | A technique that uses harmonic frequencies for improved image quality                        |
| **HIPAA**             | Health Insurance Portability and Accountability Act — U.S. regulations for health data privacy |
| **ITK-SNAP**          | A reference software application for medical image segmentation (design inspiration)          |
| **Level Set**         | A segmentation algorithm using evolving contours driven by image edges                        |
| **M-Mode**            | Motion Mode — displays tissue motion over time along a single scan line                       |
| **MI**                | Mechanical Index — a safety metric indicating the potential for cavitation effects             |
| **Modality Worklist** | A DICOM service that queries scheduled procedures from a hospital information system          |
| **PACS**              | Picture Archiving and Communication System — enterprise medical image storage                 |
| **Persistence**       | Temporal averaging of consecutive frames to reduce noise (frame averaging)                    |
| **Power Doppler**     | Imaging mode that detects flow amplitude without direction information                        |
| **PRF**               | Pulse Repetition Frequency — the rate of ultrasound pulse transmission (in Hz)                |
| **Region Growing**    | A segmentation algorithm that expands from a seed point based on intensity similarity         |
| **Scan Conversion**   | The process of converting raw ultrasound scan-line data to a displayable image                |
| **SCP**               | Service Class Provider — a DICOM node that provides services (e.g., receives images)          |
| **SCU**               | Service Class User — a DICOM node that initiates requests (e.g., sends images)                |
| **SignalR**           | A real-time communication library for ASP.NET Core used for live server notifications         |
| **SOP Instance UID**  | A globally unique identifier assigned to each individual DICOM image object                   |
| **Spectral Doppler**  | Pulsed-wave Doppler displaying a velocity-vs-time spectrum for blood flow analysis            |
| **Swagger**           | An interactive web UI for exploring and testing REST API endpoints (OpenAPI)                   |
| **TGC**               | Time Gain Compensation — depth-dependent gain adjustment using slider zones                   |
| **TI**                | Thermal Index — a safety metric indicating the potential for tissue heating                    |
| **Watershed**         | A segmentation algorithm based on topographic gradient analysis of image intensity             |

---

**© 2026 Rheacon Systems — All Rights Reserved**

*AuraScan Ultrasound System is a registered product of Rheacon Systems. This software is intended for use by qualified medical professionals only.*
