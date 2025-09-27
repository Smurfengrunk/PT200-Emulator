using PT200Emulator.Core.Emulator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PT200Emulator.UI
{
    /// <summary>
    /// Interaction logic for TerminalGridCell.xaml
    /// </summary>
    public partial class TerminalGridCell : UserControl
    {
        public TerminalGridCell()
        {
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            InitializeComponent();
        }

        public void SetContent(char character, StyleInfo style)
        {
            CellText.Text = character.ToString();

            Brush fg = style.ReverseVideo ? style.Background : style.Foreground;
            Brush bg = style.ReverseVideo ? style.Foreground : style.Background;

            CellText.Foreground = fg;
            CellText.Background = bg;

            CellText.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
            CellText.TextDecorations = style.Underline ? TextDecorations.Underline : null;

            if (style.Blink)
            {
                var blinkAnimation = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                CellText.BeginAnimation(OpacityProperty, blinkAnimation);
            }
            else
            {
                CellText.BeginAnimation(OpacityProperty, null);
                CellText.Opacity = 1.0;
            }

            if (style.Transparent)
            {
                CellText.Background = Brushes.Transparent;
            }

            if (style.StrikeThrough)
            {
                CellText.TextDecorations = TextDecorations.Strikethrough;
            }

            // VisualAttributeLock kan hanteras separat om det påverkar interaktivitet eller stilskydd
        }
    }
}
