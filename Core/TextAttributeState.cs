using System.Windows.Media;

namespace PT200Emulator.Core
{
    public class TextAttributeState
    {
        public Brush Foreground { get; set; } = Brushes.White;
        public Brush Background { get; set; } = Brushes.Black;
        public bool Blink { get; set; } = false;

        public void Reset()
        {
            Foreground = Brushes.White;
            Background = Brushes.Black;
            Blink = false;
        }

        public TextAttributeState Clone()
        {
            return new TextAttributeState
            {
                Foreground = this.Foreground,
                Background = this.Background,
                Blink = this.Blink
            };
        }
    }
}