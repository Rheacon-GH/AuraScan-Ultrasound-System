using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using AuraScan_Ultrasound_System.Models;
using Microsoft.Extensions.Logging;

namespace AuraScan_Ultrasound_System.Core.Dicom
{
    /// <summary>
    /// Full DICOM services facade providing Storage, Query/Retrieve, Worklist, and Echo.
    /// Uses fo-dicom 5.2.6 for all DICOM network operations.
    /// </summary>
    public sealed class DicomService : IDisposable
    {
        private readonly DicomImageBuilder _imageBuilder = new();
        private IDicomServer? _storageServer;
        private readonly List<DicomFile> _storedImages = [];
        private readonly object _storeLock = new();

        /// <summary>Event raised when an image is received via C-Store.</summary>
        public event EventHandler<DicomFile>? ImageReceived;

        /// <summary>Event raised on DICOM operation error.</summary>
        public event EventHandler<string>? ErrorOccurred;

        // ── Storage SCU (Send images to PACS) ──

        /// <summary>
        /// Store an ultrasound frame to a remote DICOM server (C-Store SCU).
        /// </summary>
        public async Task<bool> StoreImageAsync(UltrasoundFrame frame, PatientInfo patient,
            ProbeConfiguration probe, ScanParameters scanParams, DicomServerConfig server,
            CancellationToken ct = default)
        {
            try
            {
                var dicomFile = _imageBuilder.BuildUltrasoundDicom(frame, patient, probe, scanParams);
                return await StoreFileAsync(dicomFile, server, ct);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Store failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Store a DICOM file to a remote server.
        /// </summary>
        public async Task<bool> StoreFileAsync(DicomFile file, DicomServerConfig server,
            CancellationToken ct = default)
        {
            try
            {
                var client = DicomClientFactory.Create(
                    server.Host, server.Port, false,
                    server.CallingAeTitle, server.CalledAeTitle);

                bool success = false;
                var storeRequest = new DicomCStoreRequest(file);
                storeRequest.OnResponseReceived = (req, response) =>
                {
                    success = response.Status == DicomStatus.Success;
                };

                await client.AddRequestAsync(storeRequest);
                await client.SendAsync(ct);

                return success;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"C-Store SCU failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save a DICOM file locally.
        /// </summary>
        public async Task SaveLocalAsync(UltrasoundFrame frame, PatientInfo patient,
            ProbeConfiguration probe, ScanParameters scanParams, string filePath,
            CancellationToken ct = default)
        {
            var dicomFile = _imageBuilder.BuildUltrasoundDicom(frame, patient, probe, scanParams);
            await dicomFile.SaveAsync(filePath);
        }

        // ── Storage SCP (Receive images) ──

        /// <summary>
        /// Start a DICOM Storage SCP server to receive images.
        /// </summary>
        public void StartStorageServer(int port, string aeTitle)
        {
            _storageServer?.Dispose();
            _storageServer = DicomServerFactory.Create<StorageScp>(port);
            StorageScp.ImageReceived += OnImageReceivedFromScp;
        }

        /// <summary>
        /// Stop the Storage SCP server.
        /// </summary>
        public void StopStorageServer()
        {
            StorageScp.ImageReceived -= OnImageReceivedFromScp;
            _storageServer?.Dispose();
            _storageServer = null;
        }

        private void OnImageReceivedFromScp(object? sender, DicomFile file)
        {
            lock (_storeLock)
            {
                _storedImages.Add(file);
            }
            ImageReceived?.Invoke(this, file);
        }

        // ── Query/Retrieve (C-Find / C-Move) ──

        /// <summary>
        /// Query studies on a remote DICOM server (C-Find SCU at Study level).
        /// </summary>
        public async Task<List<DicomDataset>> QueryStudiesAsync(DicomServerConfig server,
            string? patientId = null, string? patientName = null,
            DateTime? studyDateFrom = null, DateTime? studyDateTo = null,
            string? modality = null, CancellationToken ct = default)
        {
            var results = new List<DicomDataset>();

            try
            {
                var cfind = DicomCFindRequest.CreateStudyQuery(
                    patientId: patientId,
                    patientName: patientName);

                if (!string.IsNullOrEmpty(modality))
                    cfind.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, modality);

                if (studyDateFrom.HasValue || studyDateTo.HasValue)
                {
                    string dateRange = FormatDateRange(studyDateFrom, studyDateTo);
                    cfind.Dataset.AddOrUpdate(DicomTag.StudyDate, dateRange);
                }

                // Request additional return fields
                cfind.Dataset.AddOrUpdate(DicomTag.StudyDescription, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.AccessionNumber, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedSeries, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.NumberOfStudyRelatedInstances, string.Empty);

                cfind.OnResponseReceived = (req, response) =>
                {
                    if (response.Status == DicomStatus.Pending && response.Dataset != null)
                        results.Add(response.Dataset);
                };

                var client = DicomClientFactory.Create(
                    server.Host, server.Port, false,
                    server.CallingAeTitle, server.CalledAeTitle);
                await client.AddRequestAsync(cfind);
                await client.SendAsync(ct);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"C-Find failed: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Query series within a study (C-Find at Series level).
        /// </summary>
        public async Task<List<DicomDataset>> QuerySeriesAsync(DicomServerConfig server,
            string studyInstanceUid, CancellationToken ct = default)
        {
            var results = new List<DicomDataset>();

            try
            {
                var cfind = DicomCFindRequest.CreateSeriesQuery(studyInstanceUid);
                cfind.Dataset.AddOrUpdate(DicomTag.Modality, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.SeriesDescription, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.NumberOfSeriesRelatedInstances, string.Empty);

                cfind.OnResponseReceived = (req, response) =>
                {
                    if (response.Status == DicomStatus.Pending && response.Dataset != null)
                        results.Add(response.Dataset);
                };

                var client = DicomClientFactory.Create(
                    server.Host, server.Port, false,
                    server.CallingAeTitle, server.CalledAeTitle);
                await client.AddRequestAsync(cfind);
                await client.SendAsync(ct);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Series query failed: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Retrieve a study via C-Move to the local Storage SCP.
        /// </summary>
        public async Task<bool> RetrieveStudyAsync(DicomServerConfig server,
            string studyInstanceUid, string destinationAe, CancellationToken ct = default)
        {
            try
            {
                var cmove = new DicomCMoveRequest(destinationAe, studyInstanceUid);
                bool success = false;

                cmove.OnResponseReceived = (req, response) =>
                {
                    if (response.Status == DicomStatus.Success)
                        success = true;
                };

                var client = DicomClientFactory.Create(
                    server.Host, server.Port, false,
                    server.CallingAeTitle, server.CalledAeTitle);
                await client.AddRequestAsync(cmove);
                await client.SendAsync(ct);

                return success;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"C-Move failed: {ex.Message}");
                return false;
            }
        }

        // ── Modality Worklist (MWL) ──

        /// <summary>
        /// Query the Modality Worklist for scheduled procedures (C-Find at Worklist level).
        /// </summary>
        public async Task<List<PatientInfo>> QueryWorklistAsync(DicomServerConfig server,
            DateTime? scheduledDate = null, string? modality = null,
            CancellationToken ct = default)
        {
            var results = new List<PatientInfo>();

            try
            {
                var cfind = DicomCFindRequest.CreateWorklistQuery();

                // Scheduled Procedure Step Sequence
                var spsSeq = new DicomSequence(DicomTag.ScheduledProcedureStepSequence);
                var spsItem = new DicomDataset();

                if (scheduledDate.HasValue)
                    spsItem.AddOrUpdate(DicomTag.ScheduledProcedureStepStartDate, scheduledDate.Value);
                if (!string.IsNullOrEmpty(modality))
                    spsItem.AddOrUpdate(DicomTag.Modality, modality);
                else
                    spsItem.AddOrUpdate(DicomTag.Modality, "US");

                spsItem.AddOrUpdate(DicomTag.ScheduledStationAETitle, string.Empty);
                spsItem.AddOrUpdate(DicomTag.ScheduledProcedureStepDescription, string.Empty);
                spsSeq.Items.Add(spsItem);
                cfind.Dataset.AddOrUpdate(spsSeq);

                // Request return fields
                cfind.Dataset.AddOrUpdate(DicomTag.PatientName, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.PatientID, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.PatientBirthDate, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.PatientSex, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.AccessionNumber, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.ReferringPhysicianName, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.StudyDescription, string.Empty);
                cfind.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, string.Empty);

                cfind.OnResponseReceived = (req, response) =>
                {
                    if (response.Status == DicomStatus.Pending && response.Dataset != null)
                    {
                        results.Add(DatasetToPatientInfo(response.Dataset));
                    }
                };

                var client = DicomClientFactory.Create(
                    server.Host, server.Port, false,
                    server.CallingAeTitle, server.CalledAeTitle);
                await client.AddRequestAsync(cfind);
                await client.SendAsync(ct);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Worklist query failed: {ex.Message}");
            }

            return results;
        }

        // ── DICOM Echo (Verification) ──

        /// <summary>
        /// Send a C-Echo request to verify connectivity with a DICOM server.
        /// </summary>
        public async Task<bool> EchoAsync(DicomServerConfig server, CancellationToken ct = default)
        {
            try
            {
                bool success = false;
                var client = DicomClientFactory.Create(
                    server.Host, server.Port, false,
                    server.CallingAeTitle, server.CalledAeTitle);

                var echoRequest = new DicomCEchoRequest();
                echoRequest.OnResponseReceived = (req, response) =>
                {
                    success = response.Status == DicomStatus.Success;
                };

                await client.AddRequestAsync(echoRequest);
                await client.SendAsync(ct);
                return success;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"C-Echo failed: {ex.Message}");
                return false;
            }
        }

        private static PatientInfo DatasetToPatientInfo(DicomDataset ds)
        {
            var info = new PatientInfo
            {
                PatientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty),
                PatientId = ds.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty),
                Sex = ds.GetSingleValueOrDefault(DicomTag.PatientSex, string.Empty),
                AccessionNumber = ds.GetSingleValueOrDefault(DicomTag.AccessionNumber, string.Empty),
                ReferringPhysician = ds.GetSingleValueOrDefault(DicomTag.ReferringPhysicianName, string.Empty),
                StudyDescription = ds.GetSingleValueOrDefault(DicomTag.StudyDescription, string.Empty),
                StudyInstanceUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty)
            };

            if (ds.TryGetSingleValue(DicomTag.PatientBirthDate, out DateTime dob))
                info.DateOfBirth = dob;

            return info;
        }

        private static string FormatDateRange(DateTime? from, DateTime? to)
        {
            string fromStr = from?.ToString("yyyyMMdd") ?? "";
            string toStr = to?.ToString("yyyyMMdd") ?? "";
            if (!string.IsNullOrEmpty(fromStr) && !string.IsNullOrEmpty(toStr))
                return $"{fromStr}-{toStr}";
            if (!string.IsNullOrEmpty(fromStr))
                return $"{fromStr}-";
            if (!string.IsNullOrEmpty(toStr))
                return $"-{toStr}";
            return "";
        }

        public void Dispose()
        {
            StopStorageServer();
        }
    }

    /// <summary>
    /// DICOM Storage SCP implementation for receiving images.
    /// </summary>
    internal class StorageScp : FellowOakDicom.Network.DicomService, IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
    {
        internal static event EventHandler<DicomFile>? ImageReceived;

        public StorageScp(INetworkStream stream, Encoding fallbackEncoding,
            ILogger logger, DicomServiceDependencies dependencies)
            : base(stream, fallbackEncoding, logger, dependencies)
        {
        }

        public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            foreach (var pc in association.PresentationContexts)
                pc.SetResult(DicomPresentationContextResult.Accept);
            return SendAssociationAcceptAsync(association);
        }

        public Task OnReceiveAssociationReleaseRequestAsync()
            => SendAssociationReleaseResponseAsync();

        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }

        public void OnConnectionClosed(Exception? exception) { }

        public Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
        {
            var file = new DicomFile(request.Dataset);
            ImageReceived?.Invoke(this, file);
            return Task.FromResult(new DicomCStoreResponse(request, DicomStatus.Success));
        }

        public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
            => Task.CompletedTask;

        public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
            => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
    }
}
