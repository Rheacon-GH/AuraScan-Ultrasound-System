using System.Text;
using AuraScan.Server.Data;
using AuraScan.Server.Data.Entities;
using AuraScan.Server.Hubs;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AuraScan.Server.Services;

public class DicomScpOptions
{
    public int Port { get; set; } = 11112;
    public string AeTitle { get; set; } = "AURASCAN_SCP";
    public string StoragePath { get; set; } = "DicomStorage";
}

public class DicomScpHostedService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<AuraScanHub> _hubContext;
    private readonly DicomScpOptions _options;
    private readonly ILogger<DicomScpHostedService> _logger;
    private IDicomServer? _server;

    public DicomScpHostedService(
        IServiceScopeFactory scopeFactory,
        IHubContext<AuraScanHub> hubContext,
        IOptions<DicomScpOptions> options,
        ILogger<DicomScpHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.StoragePath);

        _server = DicomServerFactory.Create<StorageScpProvider>(
            _options.Port,
            userState: new ScpState(_scopeFactory, _hubContext, _options, _logger));

        _logger.LogInformation("DICOM SCP started on port {Port} with AE Title {AeTitle}",
            _options.Port, _options.AeTitle);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Dispose();
        _server = null;
        _logger.LogInformation("DICOM SCP stopped");
        return Task.CompletedTask;
    }

    public void Dispose() => _server?.Dispose();

    internal record ScpState(
        IServiceScopeFactory ScopeFactory,
        IHubContext<AuraScanHub> HubContext,
        DicomScpOptions Options,
        ILogger Logger);

    private class StorageScpProvider : DicomService, IDicomServiceProvider, IDicomCStoreProvider, IDicomCEchoProvider
    {
        private readonly ScpState _state;

        public StorageScpProvider(INetworkStream stream, Encoding fallbackEncoding,
            ILogger log, DicomServiceDependencies deps)
            : base(stream, fallbackEncoding, log, deps)
        {
            _state = (ScpState)UserState!;
        }

        public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            foreach (var pc in association.PresentationContexts)
            {
                pc.SetResult(DicomPresentationContextResult.Accept);
            }
            return SendAssociationAcceptAsync(association);
        }

        public Task OnReceiveAssociationReleaseRequestAsync()
            => SendAssociationReleaseResponseAsync();

        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
            => _state.Logger.LogWarning("DICOM Abort received: {Source}/{Reason}", source, reason);

        public void OnConnectionClosed(Exception? exception)
        {
            if (exception != null)
                _state.Logger.LogError(exception, "DICOM connection closed with error");
        }

        public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
        {
            try
            {
                var sopInstanceUid = request.SOPInstanceUID.UID;
                var fileName = $"{sopInstanceUid}.dcm";
                var filePath = Path.Combine(_state.Options.StoragePath, fileName);

                await request.File.SaveAsync(filePath);

                using var scope = _state.ScopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuraScanDbContext>();
                var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

                var callingAe = Association?.CallingAE ?? "UNKNOWN";

                var image = new ImageEntity
                {
                    SopInstanceUid = sopInstanceUid,
                    DicomFilePath = filePath,
                    AcquisitionDateTimeUtc = DateTime.UtcNow,
                    ImagingMode = request.Dataset.GetSingleValueOrDefault(DicomTag.Modality, "US"),
                    Width = request.Dataset.GetSingleValueOrDefault(DicomTag.Columns, 0),
                    Height = request.Dataset.GetSingleValueOrDefault(DicomTag.Rows, 0),
                };

                db.Images.Add(image);
                await db.SaveChangesAsync();

                await audit.LogAsync("DicomCStoreReceived", "Image", image.Id,
                    details: $"CallingAE={callingAe}, SOP={sopInstanceUid}");

                await _state.HubContext.Clients.All.SendAsync("DicomReceived", callingAe, sopInstanceUid);

                _state.Logger.LogInformation("C-STORE received: {SopUid} from {Ae}", sopInstanceUid, callingAe);

                return new DicomCStoreResponse(request, DicomStatus.Success);
            }
            catch (Exception ex)
            {
                _state.Logger.LogError(ex, "Error processing C-STORE");
                return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
            }
        }

        public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
        {
            _state.Logger.LogError(e, "C-STORE exception for temp file {File}", tempFileName);
            return Task.CompletedTask;
        }

        public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
            => Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
    }
}
