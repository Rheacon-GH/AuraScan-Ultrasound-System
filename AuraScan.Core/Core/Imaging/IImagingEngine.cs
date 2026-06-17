using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Core.Imaging
{
    /// <summary>
    /// Interface for all ultrasound imaging engines.
    /// </summary>
    public interface IImagingEngine : IDisposable
    {
        /// <summary>Active imaging mode.</summary>
        ImagingMode Mode { get; }

        /// <summary>Whether the engine is actively processing frames.</summary>
        bool IsRunning { get; }

        /// <summary>Current frame rate in Hz.</summary>
        double FrameRateHz { get; }

        /// <summary>Initialize the engine with probe and scan configuration.</summary>
        void Initialize(ProbeConfiguration probeConfig, ScanParameters scanParams, int displayWidth, int displayHeight);

        /// <summary>Process a raw RF frame and produce display-ready image data.</summary>
        UltrasoundFrame ProcessFrame(double[][] rfData, ScanParameters scanParams);

        /// <summary>Start the imaging engine.</summary>
        void Start();

        /// <summary>Stop the imaging engine.</summary>
        void Stop();

        /// <summary>Event raised when a processed frame is ready for display.</summary>
        event EventHandler<UltrasoundFrame>? FrameReady;
    }
}
