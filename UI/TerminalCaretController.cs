using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PT200Emulator.UI
{
    public class TerminalCaretController
    {
        private readonly TextBox _textBox;
        private readonly Rectangle _caret;
        private readonly ScrollViewer _contentHost;
        private readonly DispatcherTimer _blinkTimer;
        private bool _visible = true;



        public Rect rect { get; private set; }

        public TerminalCaretController(TextBox textBox, Rectangle caret)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
            _caret = caret ?? throw new ArgumentNullException(nameof(caret));

            // Se till att caret ligger rätt i layouten
            _caret.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            _caret.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            _caret.RenderTransformOrigin = new System.Windows.Point(0, 0);

            // Hämta ScrollViewer inuti TextBoxen
            _contentHost = (ScrollViewer)_textBox.Template.FindName("PART_ContentHost", _textBox);
            if (_contentHost == null)
                throw new InvalidOperationException("Kunde inte hitta PART_ContentHost i TextBox-templatet.");

            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blinkTimer.Tick += (_, _) =>
            {
                _visible = !_visible;
                _caret.Visibility = _visible ? Visibility.Visible : Visibility.Collapsed;
            };
            _blinkTimer.Start();
        }

        public void UpdatePosition()
        {
            if (_textBox.Text.Length == 0)
            {
                _caret.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            // Hämta caret-rektangel i textens koordinater
            rect = _textBox.GetRectFromCharacterIndex(_textBox.CaretIndex, true);
            if (rect.IsEmpty)
            {
                // Fallback om trailingEdge ger tom rect
                rect = _textBox.GetRectFromCharacterIndex(Math.Max(0, _textBox.CaretIndex - 1), false);
                if (rect.IsEmpty) return;
            }

            // Kompensera för scroll, padding och border
            double x = rect.X - _contentHost.HorizontalOffset
                       + _textBox.Padding.Left
                       + _textBox.BorderThickness.Left;

            /*double y = rect.Y - _contentHost.VerticalOffset
                         + _textBox.Padding.Top
                         + _textBox.BorderThickness.Top;*/
            double y = rect.Y + rect.Height - _caret.Height;

            // Flytta caret
            _caret.RenderTransform = new TranslateTransform(x, y);

            // Visa caret om dold
            if (_caret.Visibility != System.Windows.Visibility.Visible)
                _caret.Visibility = System.Windows.Visibility.Visible;
        }
    }
}