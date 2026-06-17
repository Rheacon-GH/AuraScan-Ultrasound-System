using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AuraScan_Ultrasound_System.Models;
using AuraScan_Ultrasound_System.ViewModels;

namespace AuraScan_Ultrasound_System
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.Dispose();
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm &&
                sender is ComboBox cb &&
                cb.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                vm.SetPresetCommand.Execute(tag);
            }
        }

        private void SegmentationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm &&
                sender is ComboBox cb &&
                cb.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag &&
                Enum.TryParse<SegmentationAlgorithm>(tag, out var algo))
            {
                vm.SelectedSegmentation = algo;
            }
        }

        private void UltrasoundImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm && sender is Image image)
            {
                var pos = e.GetPosition(image);
                // Convert display coordinates to image pixel coordinates
                double scaleX = (vm.DisplayImage?.PixelWidth ?? 640) / image.ActualWidth;
                double scaleY = (vm.DisplayImage?.PixelHeight ?? 480) / image.ActualHeight;
                var seedPoint = new Point(pos.X * scaleX, pos.Y * scaleY);
                vm.RunSegmentationCommand.Execute(seedPoint);
            }
        }

        private void ProbeSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb &&
                cb.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag)
            {
                // Toggle visibility of transport-specific settings panels
                if (UsbSettingsPanel != null)
                    UsbSettingsPanel.Visibility = tag == "UsbSerial" ? Visibility.Visible : Visibility.Collapsed;
                if (EthernetSettingsPanel != null)
                    EthernetSettingsPanel.Visibility = tag == "Ethernet" ? Visibility.Visible : Visibility.Collapsed;

                // Update the ViewModel
                if (DataContext is MainViewModel vm &&
                    Enum.TryParse<ProbeConnectionType>(tag, out var connType))
                {
                    vm.SelectedConnectionType = connType;
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}