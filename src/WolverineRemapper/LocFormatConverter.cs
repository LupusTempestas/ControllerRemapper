using System;
using System.Globalization;
using System.Windows.Data;

namespace WolverineRemapper
{
    /// <summary>
    /// MultiBinding converter for localized formatted labels: the first value
    /// is the (already localized) format string, the rest are its arguments.
    /// Binding the format string through L10n's indexer keeps the label live
    /// when the language changes.
    /// </summary>
    public class LocFormatConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 0 || values[0] is not string format) return "";
            var args = new object?[values.Length - 1];
            Array.Copy(values, 1, args, 0, args.Length);
            try { return string.Format(culture, format, args); }
            catch (FormatException) { return format; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
