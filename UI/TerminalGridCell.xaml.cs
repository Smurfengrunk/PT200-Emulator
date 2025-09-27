using PT200Emulator.Core.Emulator;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PT200Emulator.UI
{
    public partial class TerminalGridCell : UserControl
    {
        public TerminalGridCell()
        {
            // Låt cellen vara “passiv” i designern
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            InitializeComponent();
        }

        public void SetContent(char character, StyleInfo style)
        {
            var s = style ?? new StyleInfo();

            CellText.Text = character.ToString();

            Brush fg = s.ReverseVideo ? s.Background : s.Foreground;
            Brush bg = s.ReverseVideo ? s.Foreground : s.Background;

            CellText.Foreground = fg ?? Brushes.Lime;
            CellText.Background = s.Transparent ? Brushes.Transparent : (bg ?? Brushes.Black);

            CellText.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;

            // TextDecorations kan inte innehålla både Underline och Strikethrough samtidigt utan sammanslagning
            if (s.Underline && s.StrikeThrough)
            {
                var decs = new TextDecorationCollection(TextDecorations.Underline) { TextDecorations.Strikethrough[0] };
                CellText.TextDecorations = decs;
            }
            else if (s.Underline)
            {
                CellText.TextDecorations = TextDecorations.Underline;
            }
            else if (s.StrikeThrough)
            {
                CellText.TextDecorations = TextDecorations.Strikethrough;
            }
            else
            {
                CellText.TextDecorations = null;
            }

            if (s.Blink)
            {
                var blink = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = System.TimeSpan.FromMilliseconds(500),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                CellText.BeginAnimation(OpacityProperty, blink);
            }
            else
            {
                CellText.BeginAnimation(OpacityProperty, null);
                CellText.Opacity = 1.0;
            }
        }
    }
}