using System;
using System.Threading.Tasks;
using System.Windows;
using AuraScan.Core.Services;

namespace AuraScan_Ultrasound_System
{
    public partial class LicenseActivationWindow : Window
    {
        private readonly LicenseService _licenseService;
        private readonly bool _managementMode;

        /// <summary>
        /// Creates the license window in activation mode (default) or management/deactivation mode.
        /// </summary>
        /// <param name="managementMode">When true, shows current license info and Deactivate button.</param>
        public LicenseActivationWindow(bool managementMode = false)
        {
            InitializeComponent();

            _licenseService = new LicenseService();
            _managementMode = managementMode;

            if (_managementMode)
            {
                Title = "License Management";
                ActivationPanel.Visibility = Visibility.Collapsed;
                ManagementPanel.Visibility = Visibility.Visible;
                PopulateManagementView();
            }
            else
            {
                MachineIdTextBox.Text = LicenseService.GetMachineId();
                LicenseTypeCombo.Items.Add("Trial");
                LicenseTypeCombo.Items.Add("Standard");
                LicenseTypeCombo.Items.Add("Enterprise");
                LicenseTypeCombo.SelectedIndex = 1;
            }
        }

        private void PopulateManagementView()
        {
            MgmtMachineIdTextBox.Text = LicenseService.GetMachineId();

            var license = _licenseService.GetLocalLicense();
            if (license != null)
            {
                MgmtLicenseKeyTextBox.Text = license.LicenseKey ?? "(none)";
                MgmtLicenseTypeTextBox.Text = license.LicenseType ?? "(unknown)";

                var status = "Active";
                if (license.ExpiresAt.HasValue)
                {
                    status = license.ExpiresAt.Value < DateTime.UtcNow
                        ? $"Expired ({license.ExpiresAt.Value:yyyy-MM-dd})"
                        : $"Active (expires {license.ExpiresAt.Value:yyyy-MM-dd})";
                }
                if (!string.IsNullOrEmpty(license.SeatStatus))
                    status += $" — {license.SeatStatus}";

                MgmtStatusTextBox.Text = status;
            }
            else
            {
                MgmtLicenseKeyTextBox.Text = "(no license)";
                MgmtLicenseTypeTextBox.Text = "-";
                MgmtStatusTextBox.Text = "Not activated";
                MgmtStatusTextBox.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xAA, 0x44, 0x44));
                DeactivateButton.IsEnabled = false;
            }
        }

        private async void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            ActivateButton.IsEnabled = false;
            var key = LicenseKeyTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show(this, "Please enter a license key.", "Activation", MessageBoxButton.OK, MessageBoxImage.Warning);
                ActivateButton.IsEnabled = true;
                return;
            }

            var (ok, msg) = await _licenseService.ActivateWithKeyAsync(key);
            if (ok)
            {
                MessageBox.Show(this, "Activation successful.", "Activation", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(this, $"Activation failed: {msg}", "Activation", MessageBoxButton.OK, MessageBoxImage.Error);
                ActivateButton.IsEnabled = true;
            }
        }

        private async void DeactivateButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(this,
                "Are you sure you want to deactivate this license?\n\n" +
                "The application will close and require re-activation on next launch.",
                "Confirm Deactivation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            DeactivateButton.IsEnabled = false;

            var (ok, msg) = await _licenseService.DeactivateAsync();
            if (ok)
            {
                MessageBox.Show(this,
                    "License deactivated successfully.\n\nThe application will now close.",
                    "Deactivated", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(this, $"Deactivation failed: {msg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DeactivateButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
