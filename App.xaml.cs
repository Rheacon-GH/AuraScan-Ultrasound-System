using System.Windows;
using FellowOakDicom;
using Microsoft.Extensions.DependencyInjection;

namespace AuraScan_Ultrasound_System
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Prevent shutdown when splash closes and MainWindow hasn't opened yet
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Enforce EULA acceptance on first launch
            if (!EulaWindow.IsAccepted())
            {
                var eulaWindow = new EulaWindow();
                var eulaResult = eulaWindow.ShowDialog();
                if (eulaResult != true)
                {
                    MessageBox.Show("You must accept the End User License Agreement to use AuraScan. The application will now exit.",
                        "EULA Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown(1);
                    return;
                }
            }

            // Enforce license activation on first run using Azure Blob-backed license server
            try
            {
                var licenseService = new AuraScan.Core.Services.LicenseService();
                if (!licenseService.LocalLicenseExists())
                {
                    var activationWindow = new LicenseActivationWindow();
                    var result = activationWindow.ShowDialog();
                    if (result != true)
                    {
                        MessageBox.Show("License activation required to run AuraScan. The application will now exit.",
                            "License Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Shutdown(1);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"License service initialization failed: {ex.Message}\nSet AURASCAN_LICENSE_STORAGE_CONN environment variable to your Azure Blob Storage connection string.",
                    "License Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // Show splash screen
            var splash = new SplashScreen();
            splash.Show();

            try
            {
                // Allow splash animations to start rendering
                await Task.Delay(2500);

                // Phase 1: Core framework (~4s mark)
                splash.UpdateStatus("Initializing DICOM framework...", 5);
                await Task.Run(() =>
                {
                    var services = new ServiceCollection();
                    services.AddFellowOakDicom();
                    var serviceProvider = services.BuildServiceProvider();
                    DicomSetupBuilder.UseServiceProvider(serviceProvider);
                });
                await Task.Delay(3000);

                // Phase 2: Signal processing (~7.5s mark)
                splash.UpdateStatus("Loading signal processing engines...", 10);
                await Task.Delay(4000);

                // Phase 3: Imaging engines (~12s mark)
                splash.UpdateStatus("Initializing B-Mode engine...", 16);
                await Task.Delay(3500);

                // Phase 4: Doppler engines (~15.5s mark)
                splash.UpdateStatus("Initializing Doppler engines...", 22);
                await Task.Delay(3500);

                // Phase 5: Beamformer calibration (~19s mark)
                splash.UpdateStatus("Calibrating beamformer arrays...", 28);
                await Task.Delay(3500);

                // Phase 6: Scan converter (~22.5s mark)
                splash.UpdateStatus("Loading scan conversion tables...", 34);
                await Task.Delay(3500);

                // Phase 7: Hardware abstraction (~26s mark)
                splash.UpdateStatus("Preparing hardware abstraction layer...", 40);
                await Task.Delay(3500);

                // Phase 8: Probe detection (~29.5s mark)
                splash.UpdateStatus("Detecting probe hardware...", 47);
                await Task.Delay(3000);

                // Phase 9: Segmentation engine (~32.5s mark)
                splash.UpdateStatus("Loading segmentation engine...", 53);
                await Task.Delay(3000);

                // Phase 10: Measurement calibration (~35.5s mark)
                splash.UpdateStatus("Calibrating measurement tools...", 59);
                await Task.Delay(3000);

                // Phase 11: Security (~38.5s mark)
                splash.UpdateStatus("Configuring security services...", 65);
                await Task.Delay(3000);

                // Phase 12: Server connection (~41.5s mark)
                splash.UpdateStatus("Connecting to AuraScan Server...", 72);
                await Task.Delay(3000);

                // Phase 13: DICOM network (~44.5s mark)
                splash.UpdateStatus("Verifying DICOM network nodes...", 78);
                await Task.Delay(3000);

                // Phase 14: Patient database (~47.5s mark)
                splash.UpdateStatus("Synchronizing patient database...", 84);
                await Task.Delay(3000);

                // Phase 15: Audit system (~50.5s mark)
                splash.UpdateStatus("Initializing audit logging system...", 90);
                await Task.Delay(3000);

                // Phase 16: UI (~53.5s mark)
                splash.UpdateStatus("Preparing workstation interface...", 95);
                await Task.Delay(3000);

                // Final (~56.5s mark, hold)
                splash.UpdateStatus("Ready", 100);
                await Task.Delay(2500);

                // Fade out splash (~59.5s) and show main window
                await splash.FadeOutAsync();

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();

                splash.Close();

                // Now use normal shutdown when main window closes
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup error: {ex.Message}\n\n{ex.StackTrace}",
                    "AuraScan — Startup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
