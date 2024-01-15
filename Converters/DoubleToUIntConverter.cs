using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace fs2ff.Converters
{
    public class DoubleToUIntConverter : MarkupExtension, IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is double d
                ? System.Convert.ToUInt32(d)
                : Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is uint i
                ? System.Convert.ToDouble(i)
                : Binding.DoNothing;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
