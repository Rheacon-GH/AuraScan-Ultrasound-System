using System;
using System.Threading.Tasks;
using System.Windows;
using AuraScan.Core.Services;

namespace AuraScan_Ultrasound_System
{
    public partial class LicenseActivationWindow : Window
    {
        private readonly LicenseService _licenseService;

        public LicenseActivationWindow()
        {
            InitializeComponent();

            _licenseService = new LicenseService();
            MachineIdTextBox.Text = LicenseService.GetMachineId();

            LicenseTypeCombo.Items.Add("Trial");
            LicenseTypeCombo.Items.Add("Standard");
            LicenseTypeCombo.Items.Add("Enterprise");
            LicenseTypeCombo.SelectedIndex = 1;
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
