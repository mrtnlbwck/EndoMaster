using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace EndoMaster.Views
{
    // Konwerter używany w XAML:
    // <local:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool flag = value is bool b && b;

            // Jeśli w XAML podasz ConverterParameter="Invert"
            // to odwróci logikę (true -> Collapsed, false -> Visible)
            if (parameter is string s &&
                s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // Na razie nie używamy ConvertBack – rzucamy wyjątek.
            throw new NotImplementedException();
        }
    }
}
