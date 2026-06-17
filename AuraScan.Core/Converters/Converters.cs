using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AuraScan_Ultrasound_System.Models;

namespace AuraScan_Ultrasound_System.Converters
{
    /// <summary>
    /// Converts a boolean to Visibility.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Visible;
    }

    /// <summary>
    /// Converts ImagingMode enum to a display-friendly string.
    /// </summary>
    public class ImagingModeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                ImagingMode.BMode => "B-Mode",
                ImagingMode.MMode => "M-Mode",
                ImagingMode.ColorDoppler => "Color",
                ImagingMode.PowerDoppler => "Power",
                ImagingMode.SpectralDoppler => "PW",
                ImagingMode.Volume3D => "3D",
                _ => value?.ToString() ?? ""
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts AcquisitionState to a status indicator color.
    /// </summary>
    public class AcquisitionStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                AcquisitionState.Scanning => new SolidColorBrush(Color.FromRgb(0, 200, 0)),
                AcquisitionState.Frozen => new SolidColorBrush(Color.FromRgb(0, 150, 255)),
                _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Compares the current ImagingMode to a parameter value for button highlighting.
    /// </summary>
    public class ImagingModeEqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ImagingMode mode && parameter is string paramStr &&
                Enum.TryParse<ImagingMode>(paramStr, out var target))
                return mode == target;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a double depth value (cm) to a formatted display string.
    /// </summary>
    public class DepthDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? $"{d:F1} cm" : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Formats a double value with a format string + optional suffix from parameter.
    /// Parameter examples: "F0", "F1 MHz", "F0 dB", "F0°".
    /// </summary>
    public class DoubleFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d && parameter is string param && param.Length > 0)
            {
                // Extract standard numeric format (letter + optional digits) from start
                int i = 1;
                while (i < param.Length && char.IsDigit(param[i])) i++;
                string fmt = param[..i];
                string suffix = param[i..];
                return d.ToString(fmt, culture) + suffix;
            }
            return value?.ToString() ?? "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts ProbeConnectionState to a status indicator color.
    /// </summary>
    public class ProbeConnectionStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                ProbeConnectionState.Connected => new SolidColorBrush(Color.FromRgb(0, 200, 0)),
                ProbeConnectionState.Connecting or ProbeConnectionState.Reconnecting
                    => new SolidColorBrush(Color.FromRgb(255, 200, 0)),
                ProbeConnectionState.Error => new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts battery percentage (int) to a color brush:
    /// 0-10 = Red, 11-20 = Orange, 21-50 = Yellow, 51-100 = Green.
    /// </summary>
    public class BatteryLevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int percent)
            {
                return percent switch
                {
                    <= 10 => new SolidColorBrush(Color.FromRgb(255, 50, 50)),
                    <= 20 => new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                    <= 50 => new SolidColorBrush(Color.FromRgb(255, 210, 0)),
                    _ => new SolidColorBrush(Color.FromRgb(0, 210, 80))
                };
            }
            return new SolidColorBrush(Color.FromRgb(128, 128, 128));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts battery percentage (int 0-100) to a proportional width for the fill bar.
    /// Parameter should be the max width (e.g., "20").
    /// </summary>
    public class BatteryLevelToWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int percent && parameter is string maxStr && double.TryParse(maxStr, out double maxWidth))
            {
                return Math.Max(0, Math.Min(maxWidth, maxWidth * percent / 100.0));
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Inverts a boolean value. True?Collapsed, False?Visible when used with BoolToVisibility.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Collapsed;
    }
}
