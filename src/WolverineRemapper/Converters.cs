using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WolverineRemapper
{
    /// <summary>
    /// bool → Brush. The old code abused a text converter for Background
    /// bindings, which silently failed and left every indicator gray.
    /// </summary>
    public class BoolToBrushConverter : IValueConverter
    {
        public Brush TrueBrush { get; set; } = Brushes.LimeGreen;
        public Brush FalseBrush { get; set; } = Brushes.DimGray;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? TrueBrush : FalseBrush;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// double → value * factor + offset. Parameter: "factor" or "factor,offset".
    /// Used for deadzone ring sizing/centering and live stick-nub translation.
    /// </summary>
    public class ScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            double factor = 1, offset = 0;

            if (parameter is string s)
            {
                var parts = s.Split(',');
                if (parts.Length > 0) double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out factor);
                if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
            }

            return v * factor + offset;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>VirtualButtonId → short human label ("DPadUp" → "D-Up").</summary>
    public class ButtonNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value?.ToString() switch
            {
                "DPadUp" => "D-Up",
                "DPadDown" => "D-Down",
                "DPadLeft" => "D-Left",
                "DPadRight" => "D-Right",
                var s => s ?? ""
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>bool → Visibility with optional inversion.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is true;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
