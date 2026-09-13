using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WolverineRemapper
{
    /// <summary>
    /// Attached property that renders a string with **bold** markers as
    /// TextBlock inlines, so localized guide text can emphasise keywords
    /// without per-language XAML. Line breaks in the string are kept.
    /// </summary>
    public static class MarkupText
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(MarkupText), new PropertyMetadata(null, OnTextChanged));

        public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
        public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

        private static readonly Brush BoldBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            string text = e.NewValue as string ?? "";

            var parts = text.Split("**");
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (i % 2 == 1)
                {
                    tb.Inlines.Add(new Run(parts[i]) { FontWeight = FontWeights.Bold, Foreground = BoldBrush });
                }
                else
                {
                    tb.Inlines.Add(new Run(parts[i]));
                }
            }
        }
    }
}
