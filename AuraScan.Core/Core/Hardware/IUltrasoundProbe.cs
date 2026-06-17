using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Hardware
{
    /// <summary>
    /// Hardware abstraction interface for ultrasound probe SDK integration.
    /// </summary>
    public interface IUltrasoundProbe : IDisposable
    {
        ProbeConfiguration Configuration { get; }
        bool IsConnected { get; }
        bool IsAcquiring { get; }

        event EventHandler<UltrasoundFrame>? FrameAcquired;
        event EventHandler<string>? ErrorOccurred;

        Task<bool> ConnectAsync(CancellationToken ct = default);
        Task DisconnectAsync(CancellationToken ct = default);
        Task StartAcquisitionAsync(ScanParameters parameters, CancellationToken ct = default);
        Task StopAcquisitionAsync(CancellationToken ct = default);
        Task UpdateParametersAsync(ScanParameters parameters, CancellationToken ct = default);
        Task<double[][]> AcquireFrameAsync(CancellationToken ct = default);
        Task<double[][][]> AcquireDopplerEnsembleAsync(int ensembleLength, CancellationToken ct = default);
    }
}
