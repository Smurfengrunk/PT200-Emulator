using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PT200Emulator
{
    public class LogColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string line = value as string ?? "";

            if (line.Contains("SEND")) return Brushes.LightBlue;
            if (line.Contains("TextInput")) return Brushes.LightGreen;
            if (line.Contains("KeyDown")) return Brushes.Gray;
            if (line.Contains("RX")) return Brushes.Orange;
            if (line.Contains("TELNET")) return Brushes.MediumPurple;

            return Brushes.LightGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}