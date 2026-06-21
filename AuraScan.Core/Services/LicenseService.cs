using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Win32;

namespace AuraScan.Core.Services
{
    public class ActivationEntry
    {
        public string? MachineId { get; set; }
        public string? MachineName { get; set; }
        public DateTime ActivatedAt { get; set; }
    }

    public class LicenseRecord
    {
        public string? LicenseKey { get; set; }
        public string? MachineId { get; set; }
        public string? Customer { get; set; }
        public string? LicenseType { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int MaxActivations { get; set; }
        public List<ActivationEntry>? Activations { get; set; }
        public string? SeatStatus { get; set; }
    }

    /// <summary>
    /// Minimal license manager that uses an Azure Blob container to host license activation records.
    /// Container layout expected:
    /// - pending/{licenseKey}.json  -- issued activation record that the client redeems
    /// - activated/{machineId}.json -- created by client when activation succeeds
    ///
    /// Connection string should be provided via environment variable AURASCAN_LICENSE_STORAGE_CONN.
    /// Container name: aurascan-licenses
    /// </summary>
    public class LicenseService
    {
        private const string EnvConn = "AURASCAN_LICENSE_STORAGE_CONN";
        private const string ContainerName = "aurascan-licenses";
        private readonly BlobContainerClient? _container;
        private readonly string _localLicensePath;
        private readonly bool _offlineMode;

        public LicenseService()
        {
            var conn = Environment.GetEnvironmentVariable(EnvConn);

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuraScan");
            Directory.CreateDirectory(appData);
            _localLicensePath = Path.Combine(appData, "license.json");

            if (string.IsNullOrWhiteSpace(conn))
            {
                // No Azure connection string configured: operate in offline/local-only mode.
                // Activation will be persisted locally and can be reconciled with the server later.
                _offlineMode = true;
                _container = null;
                return;
            }

            _offlineMode = false;
            var client = new BlobServiceClient(conn);
            _container = client.GetBlobContainerClient(ContainerName);
        }

        public bool LocalLicenseExists()
        {
            try
            {
                if (!File.Exists(_localLicensePath)) return false;
                var json = File.ReadAllText(_localLicensePath);
                var rec = JsonSerializer.Deserialize<LicenseRecord>(json);
                if (rec == null) return false;
                if (rec.ExpiresAt.HasValue && rec.ExpiresAt.Value < DateTime.UtcNow) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public LicenseRecord GetLocalLicense()
        {
            if (!File.Exists(_localLicensePath)) return null;
            var json = File.ReadAllText(_localLicensePath);
            return JsonSerializer.Deserialize<LicenseRecord>(json);
        }

        public async Task<(bool Success, string Message)> ActivateWithKeyAsync(string licenseKey)
        {
            if (string.IsNullOrWhiteSpace(licenseKey)) return (false, "License key is empty.");

            var attemptTs = DateTime.UtcNow;
            var machineId = GetMachineId();

            try
            {
                // If running in offline mode, create local activation record only
                if (_offlineMode || _container == null)
                {
                    var activatedRecOffline = new LicenseRecord
                    {
                        LicenseKey = licenseKey,
                        MachineId = machineId,
                        Customer = "",
                        LicenseType = "Local",
                        IssuedAt = DateTime.UtcNow,
                        ExpiresAt = null
                    };
                    var activatedJsonOffline = JsonSerializer.Serialize(activatedRecOffline, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_localLicensePath, activatedJsonOffline);

                    LogActivationAttempt(licenseKey, machineId, attemptTs, success: true, offline: true, message: "Stored locally (offline mode)");

                    return (true, "Activation stored locally (offline mode). Set AURASCAN_LICENSE_STORAGE_CONN to enable server activation.");
                }

                // Download pending activation blob from server
                var pendingName = $"pending/{licenseKey}.json";
                var pendingBlob = _container.GetBlobClient(pendingName);
                if (!await pendingBlob.ExistsAsync())
                {
                    LogActivationAttempt(licenseKey, machineId, attemptTs, success: false, offline: false, message: "Key not found on server");
                    return (false, "Activation key not found on license server.");
                }

                var download = await pendingBlob.DownloadContentAsync();
                var json = download.Value.Content.ToString();
                var rec = JsonSerializer.Deserialize<LicenseRecord>(json);
                if (rec == null)
                {
                    LogActivationAttempt(licenseKey, machineId, attemptTs, success: false, offline: false, message: "Malformed server record");
                    return (false, "Malformed license record on server.");
                }

                // Initialize activations list if missing
                rec.Activations ??= new List<ActivationEntry>();

                // Check if this machine is already activated
                var alreadyActivated = rec.Activations.Any(a =>
                    string.Equals(a.MachineId, machineId, StringComparison.OrdinalIgnoreCase));

                if (alreadyActivated)
                {
                    // Re-download the existing activation for this machine
                    int seatUsed = rec.Activations.Count;
                    int seatMax = rec.MaxActivations > 0 ? rec.MaxActivations : seatUsed;
                    var seatStatus = $"{seatUsed}/{seatMax} seats used";

                    var reactivatedRec = new LicenseRecord
                    {
                        LicenseKey = rec.LicenseKey ?? licenseKey,
                        MachineId = machineId,
                        Customer = rec.Customer,
                        LicenseType = rec.LicenseType,
                        IssuedAt = rec.IssuedAt == default ? DateTime.UtcNow : rec.IssuedAt,
                        ExpiresAt = rec.ExpiresAt,
                        MaxActivations = seatMax,
                        SeatStatus = seatStatus
                    };

                    var reactivatedJson = JsonSerializer.Serialize(reactivatedRec, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_localLicensePath, reactivatedJson);

                    LogActivationAttempt(licenseKey, machineId, attemptTs, success: true, offline: false, message: $"Already activated ({seatStatus})");
                    return (true, $"This machine is already activated. {seatStatus}.");
                }

                // Enforce seat limit
                int maxSeats = rec.MaxActivations > 0 ? rec.MaxActivations : int.MaxValue;
                if (rec.Activations.Count >= maxSeats)
                {
                    var limitMsg = $"All {maxSeats} seat(s) are in use. ({rec.Activations.Count}/{maxSeats})";
                    LogActivationAttempt(licenseKey, machineId, attemptTs, success: false, offline: false, message: limitMsg);
                    return (false, $"Activation limit reached. {limitMsg}");
                }

                // Add this machine to the activations list
                rec.Activations.Add(new ActivationEntry
                {
                    MachineId = machineId,
                    MachineName = Environment.MachineName,
                    ActivatedAt = DateTime.UtcNow
                });

                // Update the pending blob with the new activations list
                var updatedPendingJson = JsonSerializer.Serialize(rec, new JsonSerializerOptions { WriteIndented = true });
                using (var pendingMs = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(updatedPendingJson)))
                {
                    await pendingBlob.UploadAsync(pendingMs, overwrite: true);
                }

                // Build seat status string
                int currentSeats = rec.Activations.Count;
                int totalSeats = rec.MaxActivations > 0 ? rec.MaxActivations : currentSeats;
                var currentSeatStatus = $"{currentSeats}/{totalSeats} seats used";

                // Create activated record for this machine
                var activatedName = $"activated/{machineId}.json";
                var activatedBlob = _container.GetBlobClient(activatedName);

                var activatedRec = new LicenseRecord
                {
                    LicenseKey = rec.LicenseKey ?? licenseKey,
                    MachineId = machineId,
                    Customer = rec.Customer,
                    LicenseType = rec.LicenseType,
                    IssuedAt = rec.IssuedAt == default ? DateTime.UtcNow : rec.IssuedAt,
                    ExpiresAt = rec.ExpiresAt,
                    MaxActivations = totalSeats,
                    SeatStatus = currentSeatStatus
                };

                var activatedJson = JsonSerializer.Serialize(activatedRec, new JsonSerializerOptions { WriteIndented = true });
                using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(activatedJson)))
                {
                    await activatedBlob.UploadAsync(ms, overwrite: true);
                }

                // Persist locally
                File.WriteAllText(_localLicensePath, activatedJson);

                LogActivationAttempt(licenseKey, machineId, attemptTs, success: true, offline: false, message: $"Activated ({currentSeatStatus})");

                return (true, $"Activation successful. {currentSeatStatus}.");
            }
            catch (RequestFailedException rf)
            {
                LogActivationAttempt(licenseKey, machineId, attemptTs, success: false, offline: false, message: $"Azure request failed: {rf.Message}");
                return (false, $"Azure request failed: {rf.Message}");
            }
            catch (Exception ex)
            {
                LogActivationAttempt(licenseKey, machineId, attemptTs, success: false, offline: false, message: ex.Message);
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Deactivates the current machine's license, freeing the seat for reuse.
        /// Removes the machine from the pending blob's Activations list,
        /// deletes the activated/{machineId}.json blob, and removes the local license file.
        /// </summary>
        public async Task<(bool Success, string Message)> DeactivateAsync()
        {
            var machineId = GetMachineId();
            var timestamp = DateTime.UtcNow;

            try
            {
                // Read local license to get the key
                LicenseRecord? localRec = null;
                if (File.Exists(_localLicensePath))
                {
                    var json = File.ReadAllText(_localLicensePath);
                    localRec = JsonSerializer.Deserialize<LicenseRecord>(json);
                }

                var licenseKey = localRec?.LicenseKey ?? "(unknown)";

                // Online deactivation: remove from Azure blobs
                if (!_offlineMode && _container != null && localRec?.LicenseKey != null)
                {
                    // 1. Remove machine from pending/{key}.json Activations[]
                    var pendingName = $"pending/{localRec.LicenseKey}.json";
                    var pendingBlob = _container.GetBlobClient(pendingName);
                    if (await pendingBlob.ExistsAsync())
                    {
                        var download = await pendingBlob.DownloadContentAsync();
                        var pendingJson = download.Value.Content.ToString();
                        var pendingRec = JsonSerializer.Deserialize<LicenseRecord>(pendingJson);
                        if (pendingRec?.Activations != null)
                        {
                            int removed = pendingRec.Activations.RemoveAll(a =>
                                string.Equals(a.MachineId, machineId, StringComparison.OrdinalIgnoreCase));

                            if (removed > 0)
                            {
                                var updatedJson = JsonSerializer.Serialize(pendingRec, new JsonSerializerOptions { WriteIndented = true });
                                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(updatedJson));
                                await pendingBlob.UploadAsync(ms, overwrite: true);
                            }
                        }
                    }

                    // 2. Delete activated/{machineId}.json blob
                    var activatedName = $"activated/{machineId}.json";
                    var activatedBlob = _container.GetBlobClient(activatedName);
                    await activatedBlob.DeleteIfExistsAsync();
                }

                // 3. Delete local license file
                if (File.Exists(_localLicensePath))
                    File.Delete(_localLicensePath);

                LogActivationAttempt(licenseKey, machineId, timestamp, success: true, offline: _offlineMode,
                    message: "License deactivated — seat released");

                return (true, "License deactivated. This machine's seat has been freed.");
            }
            catch (RequestFailedException rf)
            {
                LogActivationAttempt("(deactivate)", machineId, timestamp, success: false, offline: false,
                    message: $"Azure request failed: {rf.Message}");
                return (false, $"Deactivation failed (Azure): {rf.Message}");
            }
            catch (Exception ex)
            {
                LogActivationAttempt("(deactivate)", machineId, timestamp, success: false, offline: _offlineMode,
                    message: ex.Message);
                return (false, $"Deactivation failed: {ex.Message}");
            }
        }

        private void LogActivationAttempt(string licenseKey, string machineId, DateTime timestamp, bool success, bool offline, string message)
        {
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuraScan");
                Directory.CreateDirectory(appData);
                var logPath = Path.Combine(appData, "license-activation.log");
                var keyDisplay = string.IsNullOrWhiteSpace(licenseKey) ? "(empty)" : (licenseKey.Length > 8 ? licenseKey.Substring(0, 4) + "..." + licenseKey.Substring(licenseKey.Length - 4) : licenseKey);
                var line = $"{timestamp:O}\tMachine:{machineId}\tKey:{keyDisplay}\tOffline:{offline}\tSuccess:{success}\tMessage:{message}";
                File.AppendAllLines(logPath, new[] { line });
            }
            catch
            {
                // swallow logging errors
            }
        }

        /// <summary>
        /// Attempts to read a machine identifier from the registry (MachineGuid). Falls back to a generated guid
        /// if the registry key is unavailable.
        /// </summary>
        /// <returns></returns>
        public static string GetMachineId()
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
                var mg = key?.GetValue("MachineGuid") as string;
                if (!string.IsNullOrWhiteSpace(mg)) return mg;
            }
            catch
            {
                // ignore
            }

            // fallback: compute from machine name + processor id
            try
            {
                var name = Environment.MachineName;
                var id = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Guid.NewGuid().ToString();
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name + ":" + id));
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }
    }
}
