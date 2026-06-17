using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuraScan_Ultrasound_System.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace AuraScan_Ultrasound_System.Services
{
    public class ServerApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private HubConnection? _hub;
        private readonly JsonSerializerOptions _jsonOptions;
        private bool _disposed;

        public ServerConfig Config { get; }
        public bool IsConnected => _hub?.State == HubConnectionState.Connected;

        public event EventHandler<string>? StatusMessage;
        public event EventHandler<string>? ServerNotification;

        public ServerApiClient(ServerConfig? config = null)
        {
            Config = config ?? new ServerConfig();

            _http = new HttpClient
            {
                BaseAddress = new Uri(Config.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(Config.TimeoutSeconds)
            };

            // Security headers
            _http.DefaultRequestHeaders.Add("X-API-Key", Config.ApiKey);
            _http.DefaultRequestHeaders.Add("X-Workstation-Id", Config.WorkstationId);

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        // ── SignalR Connection ──

        public async Task ConnectSignalRAsync()
        {
            if (_hub != null)
            {
                await _hub.DisposeAsync();
            }

            _hub = new HubConnectionBuilder()
                .WithUrl($"{Config.BaseUrl.TrimEnd('/')}/hubs/aurascan", options =>
                {
                    options.Headers.Add("X-API-Key", Config.ApiKey);
                    options.Headers.Add("X-Workstation-Id", Config.WorkstationId);
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<int, string>("ImageStored", (imageId, sopUid) =>
                ServerNotification?.Invoke(this, $"Server: Image stored (SOP: {sopUid[..Math.Min(8, sopUid.Length)]}...)"));

            _hub.On<int, string>("StudyUpdated", (studyId, studyUid) =>
                ServerNotification?.Invoke(this, $"Server: Study updated ({studyUid[..Math.Min(8, studyUid.Length)]}...)"));

            _hub.On<string, string>("DicomReceived", (callingAe, sopUid) =>
                ServerNotification?.Invoke(this, $"Server: DICOM received from {callingAe}"));

            _hub.On<string, string>("WorkstationStatus", (wsId, status) =>
                ServerNotification?.Invoke(this, $"Workstation {wsId}: {status}"));

            _hub.Reconnecting += _ =>
            {
                StatusMessage?.Invoke(this, "Server: Reconnecting...");
                return Task.CompletedTask;
            };

            _hub.Reconnected += _ =>
            {
                StatusMessage?.Invoke(this, "Server: Reconnected");
                return Task.CompletedTask;
            };

            _hub.Closed += _ =>
            {
                StatusMessage?.Invoke(this, "Server: Disconnected");
                return Task.CompletedTask;
            };

            try
            {
                await _hub.StartAsync();
                StatusMessage?.Invoke(this, "Server: Connected via SignalR");
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: SignalR connection failed — {ex.Message}");
            }
        }

        public async Task DisconnectSignalRAsync()
        {
            if (_hub != null)
            {
                try { await _hub.StopAsync(); } catch { }
                await _hub.DisposeAsync();
                _hub = null;
            }
        }

        // ── Health Check ──

        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                var response = await _http.GetAsync("health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        // ── Patient API ──

        public async Task<PatientDto?> SavePatientAsync(PatientInfo patient)
        {
            try
            {
                var dto = new PatientDto
                {
                    PatientId = patient.PatientId,
                    PatientName = patient.PatientName,
                    DateOfBirth = patient.DateOfBirth,
                    Sex = patient.Sex,
                    WeightKg = patient.WeightKg,
                    AccessionNumber = patient.AccessionNumber,
                    ReferringPhysician = patient.ReferringPhysician,
                    InstitutionName = patient.InstitutionName
                };
                var response = await _http.PostAsJsonAsync("api/patients", dto, _jsonOptions);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<PatientDto>(_jsonOptions);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Save patient failed — {ex.Message}");
                return null;
            }
        }

        public async Task<List<PatientDto>> SearchPatientsAsync(string? name = null, string? patientId = null)
        {
            try
            {
                var query = "api/patients?";
                if (!string.IsNullOrWhiteSpace(name)) query += $"name={Uri.EscapeDataString(name)}&";
                if (!string.IsNullOrWhiteSpace(patientId)) query += $"patientId={Uri.EscapeDataString(patientId)}&";
                return await _http.GetFromJsonAsync<List<PatientDto>>(query.TrimEnd('&', '?'), _jsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Patient search failed — {ex.Message}");
                return [];
            }
        }

        // ── Study API ──

        public async Task<StudyDto?> CreateStudyAsync(PatientInfo patient, int serverPatientId)
        {
            try
            {
                var dto = new StudyDto
                {
                    StudyInstanceUid = patient.StudyInstanceUid,
                    StudyDescription = patient.StudyDescription,
                    StudyDateTime = patient.StudyDateTime,
                    PerformingPhysician = patient.PerformingPhysician,
                    AccessionNumber = patient.AccessionNumber,
                    PatientId = serverPatientId
                };
                var response = await _http.PostAsJsonAsync("api/studies", dto, _jsonOptions);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<StudyDto>(_jsonOptions);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Create study failed — {ex.Message}");
                return null;
            }
        }

        public async Task<List<StudyDto>> GetStudiesByPatientAsync(int serverPatientId)
        {
            try
            {
                return await _http.GetFromJsonAsync<List<StudyDto>>($"api/studies/by-patient/{serverPatientId}", _jsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Get studies failed — {ex.Message}");
                return [];
            }
        }

        // ── Image API ──

        public async Task<ImageDto?> StoreImageMetadataAsync(ImageDto image)
        {
            try
            {
                var response = await _http.PostAsJsonAsync("api/images", image, _jsonOptions);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ImageDto>(_jsonOptions);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Store image failed — {ex.Message}");
                return null;
            }
        }

        public async Task<List<ImageDto>> GetImagesBySeriesAsync(int seriesId)
        {
            try
            {
                return await _http.GetFromJsonAsync<List<ImageDto>>($"api/images/by-series/{seriesId}", _jsonOptions) ?? [];
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Get images failed — {ex.Message}");
                return [];
            }
        }

        // ── Measurement API ──

        public async Task<MeasurementDto?> SaveMeasurementAsync(MeasurementResult result, int serverImageId)
        {
            try
            {
                var dto = new MeasurementDto
                {
                    Type = result.Type.ToString(),
                    Value = result.Value,
                    Unit = result.Unit,
                    Label = result.Label,
                    StartX = result.StartPoint.X,
                    StartY = result.StartPoint.Y,
                    EndX = result.EndPoint.X,
                    EndY = result.EndPoint.Y,
                    MeasuredAtUtc = result.Timestamp.ToUniversalTime(),
                    ImageId = serverImageId
                };
                var response = await _http.PostAsJsonAsync("api/measurements", dto, _jsonOptions);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<MeasurementDto>(_jsonOptions);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Save measurement failed — {ex.Message}");
                return null;
            }
        }

        // ── Segmentation API ──

        public async Task<SegmentationDto?> SaveSegmentationAsync(SegmentationResult result, int serverImageId)
        {
            try
            {
                var dto = new SegmentationDto
                {
                    Algorithm = result.Algorithm.ToString(),
                    Width = result.Width,
                    Height = result.Height,
                    SeedX = result.SeedPoint.X,
                    SeedY = result.SeedPoint.Y,
                    AreaCm2 = result.AreaCm2,
                    PerimeterCm = result.PerimeterCm,
                    ProcessingTimeMs = result.ProcessingTimeMs,
                    ImageId = serverImageId
                };

                // Post to the images controller — segmentations are nested under images
                var response = await _http.PostAsync(
                    $"api/images/{serverImageId}",
                    JsonContent.Create(dto, options: _jsonOptions));

                // If no dedicated segmentation endpoint, just log success
                StatusMessage?.Invoke(this, "Server: Segmentation result saved");
                return dto;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Save segmentation failed — {ex.Message}");
                return null;
            }
        }

        // ── Convenience: Full acquisition persist ──

        public async Task<int?> PersistAcquisitionAsync(
            PatientInfo patient,
            UltrasoundFrame frame,
            ScanParameters scanParams,
            ProbeConfiguration probeConfig)
        {
            try
            {
                // 1. Save/update patient
                var savedPatient = await SavePatientAsync(patient);
                if (savedPatient == null) return null;

                // 2. Create study (server deduplicates by UID)
                var savedStudy = await CreateStudyAsync(patient, savedPatient.Id);
                if (savedStudy == null) return null;

                // 3. Create image metadata
                var imageDto = new ImageDto
                {
                    SopInstanceUid = FellowOakDicom.DicomUID.Generate().UID,
                    ImagingMode = scanParams.Mode.ToString(),
                    Width = frame.ImageWidth,
                    Height = frame.ImageHeight,
                    FrameRate = frame.FrameRateHz,
                    MechanicalIndex = frame.MechanicalIndex,
                    ThermalIndex = frame.ThermalIndex,
                    DepthCm = scanParams.DepthM * 100.0,
                    FrequencyMHz = scanParams.TransmitFrequencyHz / 1e6,
                    AcquisitionDateTimeUtc = DateTime.UtcNow,
                    SeriesId = savedStudy.Id // Uses study ID as series placeholder
                };

                var savedImage = await StoreImageMetadataAsync(imageDto);
                StatusMessage?.Invoke(this, $"Server: Acquisition persisted (Image #{savedImage?.Id})");
                return savedImage?.Id;
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(this, $"Server: Persist acquisition failed — {ex.Message}");
                return null;
            }
        }

        // ── Config API ──

        public async Task<List<DicomNodeDto>> GetDicomNodesAsync()
        {
            try
            {
                return await _http.GetFromJsonAsync<List<DicomNodeDto>>("api/config/dicom-nodes", _jsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _hub?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)); } catch { }
            _http.Dispose();
        }
    }

    public class DicomNodeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AeTitle { get; set; } = string.Empty;
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 104;
        public string NodeType { get; set; } = "SCP";
        public bool IsEnabled { get; set; } = true;
    }
}
