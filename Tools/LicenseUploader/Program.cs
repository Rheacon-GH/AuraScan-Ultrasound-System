using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace LicenseUploader
{
    internal class ActivationEntry
    {
        public string? MachineId { get; set; }
        public string? MachineName { get; set; }
        public DateTime ActivatedAt { get; set; }
    }

    internal class LicenseRecord
    {
        public string? LicenseKey { get; set; }
        public string? Customer { get; set; }
        public string? LicenseType { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Notes { get; set; }
        public int MaxActivations { get; set; }
        public List<ActivationEntry> Activations { get; set; } = new();
    }

    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LicenseUploader <connectionString> <licenseKey> [customer] [type] [expiryYYYY-MM-DD] [maxActivations]");
                return 2;
            }

            var conn = args[0];
            var key = args[1];
            var cust = args.Length >= 3 ? args[2] : "Rheacon Systems";
            var type = args.Length >= 4 ? args[3] : "Subscription";
            DateTime? expiry = null;
            if (args.Length >= 5 && DateTime.TryParse(args[4], out var d)) expiry = d;
            int maxAct = 2;
            if (args.Length >= 6 && int.TryParse(args[5], out var ma)) maxAct = ma;

            var rec = new LicenseRecord
            {
                LicenseKey = key,
                Customer = cust,
                LicenseType = type,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = expiry,
                MaxActivations = maxAct,
                Activations = new List<ActivationEntry>()
            };

            var json = JsonSerializer.Serialize(rec, new JsonSerializerOptions { WriteIndented = true });

            var service = new BlobServiceClient(conn);
            var container = service.GetBlobContainerClient("aurascan-licenses");
            container.CreateIfNotExists();

            var blobName = $"pending/{key}.json";
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var blob = container.GetBlobClient(blobName);
            blob.Upload(ms, overwrite: true);
            Console.WriteLine($"Uploaded: {blobName}");
            Console.WriteLine($"  MaxActivations: {maxAct}");
            Console.WriteLine($"  Seats available: {maxAct} (0/{maxAct} used)");
            return 0;
        }
    }
}
