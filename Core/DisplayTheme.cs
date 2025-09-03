using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using static PT200Emulator.Core.PT200State;

namespace PT200Emulator.Core
{
    public static class DisplayTheme
    {
        public static Brush GetForeground(DisplayType type) => type switch
        {
            DisplayType.White => new SolidColorBrush(Color.FromRgb(255, 255, 255)),     // Full vit
            DisplayType.Blue => new SolidColorBrush(Color.FromRgb(135, 206, 250)),     // LightSkyBlue
            DisplayType.Green => new SolidColorBrush(Color.FromRgb(144, 238, 144)),     // LightGreen
            DisplayType.Amber => new SolidColorBrush(Color.FromRgb(255, 215, 0)),       // Gold
            DisplayType.FullColor => new SolidColorBrush(Color.FromRgb(255, 255, 255)),     // Behåll vit
            _ => new SolidColorBrush(Color.FromRgb(255, 255, 255)),     // Fallback vit
        };

        public static Brush GetBackground(DisplayType type) => new SolidColorBrush(Color.FromRgb(10, 10, 10)); // Mjukare än ren svart

        public static Brush GetInvertedForeground(DisplayType type) => GetBackground(type);
        public static Brush GetInvertedBackground(DisplayType type) => GetForeground(type);
    }
}