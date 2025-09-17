#nullable disable

using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PT200Emulator.UI
{
    public partial class TerminalControl : UserControl
    {
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _speedTimer;
        private int _bytesReceivedThisSecond = 0;
        private Run[,] _runs;

        public InputController InputController { get; set; } = default!;
        public IInputMapper InputMapper { get; set; }

        private Brush _monoForeground = Brushes.LimeGreen;
        private Brush _monoBackground = Brushes.Black;

        private DateTime _lastRender = DateTime.MinValue;
        private readonly TimeSpan _renderInterval = TimeSpan.FromMilliseconds(50); // ~20 FPS
        private bool _renderScheduled;
        private char[,] _lastChars; // för ändringsdetektering
        private TerminalSession _session;

        public TerminalControl()
        {
            InitializeComponent();

            // Starta klocka
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) =>
            {
                ClockTextBlock.Text = $"| {DateTime.Now:HH:mm} ";
            };
            _clockTimer.Start();

            _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _speedTimer.Tick += (_, _) =>
            {
                SpeedTextBlock.Text = $"| ↩ {_bytesReceivedThisSecond} B/s";
                _bytesReceivedThisSecond = 0;
            };
            _speedTimer.Start();
        }

        public void InitializeInput(InputController controller, IInputMapper mapper = null)
        {
            InputController = controller;
            InputMapper = mapper;
        }

        public async Task InitializeSession(TerminalSession session)
        {
            InputController = session.Controller;
            InputMapper = session.Mapper;
            StatusText.Text = $"✅ {session.TerminalId} @ {session.BaudRate} baud";
            _session = session ?? throw new ArgumentNullException(nameof(session));

            // Initiera dokumentet och ev. breddjustering här
            InitDocument(session.ScreenBuffer);

            // Prenumerera på buffertens uppdateringshändelse
            session.ScreenBuffer.BufferUpdated += OnBufferUpdated;

            // Viktigt: initiera sessionens interna eventkopplingar (bl.a. DCS-svar)
            await session.InitializeAsync();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            FocusInput();
            UpdateLockIndicators();
            AdjustTerminalWidth(_session.ScreenBuffer);
            AdjustTerminalHeight(_session.ScreenBuffer);
        }

        public void FocusInput()
        {
            InputOverlay.Focus();
            Keyboard.Focus(InputOverlay);
        }

        private async void InputOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (await InputController.DetectSpecialKey(e))
            {
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.P) || e.Key == Key.Pause)
            {
                await InputController?.SendBreakAsync();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Back:
                    InputController?.SendRawAsync(new byte[] { 0x08 }); // BS
                    e.Handled = true;
                    return;

                case Key.Enter:
                    InputController?.SendRawAsync(new byte[] { 0x0D, 0x0A }); // CRLF
                    e.Handled = true;
                    return;

                case Key.Space:
                    InputController?.SendRawAsync(new byte[] { 0x20 }); // Space
                    e.Handled = true;
                    return;

                case Key.Tab:
                    InputController?.SendRawAsync(new byte[] { 0x09 }); // Tab
                    e.Handled = true;
                    return;

                case Key.NumLock:
                case Key.CapsLock:
                case Key.Scroll:
                    UpdateLockIndicators();
                    break;
            }
        }

        private void InputOverlay_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var bytes = Encoding.ASCII.GetBytes(e.Text);
            InputController?.SendRawAsync(bytes);
            e.Handled = true;
        }

        private void UpdateLockIndicators()
        {
            NumLock.Background = StatusText.Background;
            CapsLock.Background = StatusText.Background;
            ScrollLock.Background = StatusText.Background;

            NumLock.Foreground = Keyboard.IsKeyToggled(Key.NumLock) ? Brushes.Black : StatusText.Background;
            CapsLock.Foreground = Keyboard.IsKeyToggled(Key.CapsLock) ? Brushes.Purple : StatusText.Background;
            ScrollLock.Foreground = Keyboard.IsKeyToggled(Key.Scroll) ? Brushes.Red : StatusText.Background;
        }

        private void InitDocument(IScreenBuffer buffer)
        {
            _runs = new Run[buffer.Rows, buffer.Cols];
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                Background = _monoBackground
            };

            _lastChars = new char[buffer.Rows, buffer.Cols];
            for (int r = 0; r < buffer.Rows; r++)
                for (int c = 0; c < buffer.Cols; c++)
                    _lastChars[r, c] = '\0';

            // Räkna ut teckenbredd för att sätta PageWidth
            var typeface = new Typeface(TerminalText.FontFamily, TerminalText.FontStyle, TerminalText.FontWeight, TerminalText.FontStretch);
            
            var pixelsPerDip = VisualTreeHelper.GetDpi(TerminalText).PixelsPerDip;
            var ft = new FormattedText(
                new string('W', buffer.Cols),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                TerminalText.FontSize,
                Brushes.White,
                pixelsPerDip // <-- rätt DPI här
            );
            double charWidth = ft.WidthIncludingTrailingWhitespace / buffer.Cols;
            double extra = TerminalText.Padding.Left + TerminalText.Padding.Right
                         + TerminalText.BorderThickness.Left + TerminalText.BorderThickness.Right;

            double safety = charWidth * 2; // två extra tecken
            double totalWidth = (charWidth * buffer.Cols) + extra + safety;
            // Viktigt: sätt på RichTextBox, inte bara UserControl
            TerminalText.Width = totalWidth;
            TerminalText.Document.PageWidth = totalWidth;
            for (int r = 0; r < buffer.Rows; r++)
            {
                var p = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) };
                for (int c = 0; c < buffer.Cols; c++)
                {
                    var run = new Run(" ")
                    {
                        Foreground = _monoForeground,
                        Background = _monoBackground
                    };
                    _runs[r, c] = run;
                    p.Inlines.Add(run);
                }
                doc.Blocks.Add(p);
            }

            TerminalText.Document = doc;
        }

        private void UpdateCell(int row, int col, char ch, StyleInfo style, bool fullColor)
        {
            var run = (_runs != null) ? _runs[row, col] : throw new ArgumentNullException("_runs null");
            run.Text = ch.ToString();

            if (fullColor)
            {
                run.Foreground = new SolidColorBrush(ConvertConsoleColor(style.Foreground));
                run.Background = new SolidColorBrush(ConvertConsoleColor(style.Background));
            }
            else
            {
                run.Foreground = _monoForeground;
                run.Background = _monoBackground;
            }

            run.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
        }

        public void RenderFromBuffer(IScreenBuffer buffer, bool fullColor = false)
        {
            if (DateTime.UtcNow - _lastRender < _renderInterval)
                return;

            for (int r = 0; r < buffer.Rows; r++)
            {
                for (int c = 0; c < buffer.Cols; c++)
                {
                    char ch = buffer.GetChar(r, c);
                    StyleInfo style = buffer.GetStyle(r, c);
                    UpdateCell(r, c, ch, style, fullColor);
                }
            }

            // Flytta caret
            var caretPos = TerminalText.Document.ContentStart;
            int offset = buffer.CursorRow * (buffer.Cols + 1) + buffer.CursorCol;
            caretPos = caretPos.GetPositionAtOffset(offset, LogicalDirection.Forward) ?? caretPos;
            TerminalText.CaretPosition = caretPos;
            TerminalText.ScrollToEnd();

            _lastRender = DateTime.UtcNow;
            FocusInput();
        }

        private Color ConvertConsoleColor(ConsoleColor color)
        {
            return color switch
            {
                ConsoleColor.Black => Colors.Black,
                ConsoleColor.DarkBlue => Colors.DarkBlue,
                ConsoleColor.DarkGreen => Colors.DarkGreen,
                ConsoleColor.DarkCyan => Colors.DarkCyan,
                ConsoleColor.DarkRed => Colors.DarkRed,
                ConsoleColor.DarkMagenta => Colors.DarkMagenta,
                ConsoleColor.DarkYellow => Colors.Olive,
                ConsoleColor.Gray => Colors.Gray,
                ConsoleColor.DarkGray => Colors.DarkGray,
                ConsoleColor.Blue => Colors.Blue,
                ConsoleColor.Green => Colors.Green,
                ConsoleColor.Cyan => Colors.Cyan,
                ConsoleColor.Red => Colors.Red,
                ConsoleColor.Magenta => Colors.Magenta,
                ConsoleColor.Yellow => Colors.Yellow,
                ConsoleColor.White => Colors.White,
                _ => Colors.White
            };
        }
        public void UpdateStatus(string statusText, Brush color)
        {
            StatusText.Text = statusText;
            StatusText.Foreground = color;
        }

        public void SetDisplayTheme(TerminalState.DisplayType theme)
        {
            switch (theme)
            {
                case TerminalState.DisplayType.White:
                    _monoForeground = Brushes.White;
                    _monoBackground = Brushes.Black;
                    break;
                case TerminalState.DisplayType.Green:
                    _monoForeground = Brushes.LimeGreen;
                    _monoBackground = Brushes.Black;
                    break;
                case TerminalState.DisplayType.Blue:
                    _monoForeground = Brushes.DeepSkyBlue;
                    _monoBackground = Brushes.Black;
                    break;
                case TerminalState.DisplayType.Amber:
                    _monoForeground = new SolidColorBrush(Color.FromRgb(255, 191, 0));
                    _monoBackground = Brushes.Black;
                    break;
                case TerminalState.DisplayType.FullColor:
                default:
                    _monoForeground = Brushes.White;
                    _monoBackground = Brushes.Black;
                    break;
            }

            // Ny text
            TerminalText.Background = _monoBackground;
            TerminalText.Foreground = _monoForeground;

            // Befintlig text
            foreach (var block in TerminalText.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (var inline in p.Inlines)
                    {
                        if (inline is Run r)
                        {
                            r.Foreground = _monoForeground;
                            r.Background = _monoBackground;
                        }
                    }
                }
            }

            // Statusrad
            StatusBarElement.Foreground = _monoBackground;
            StatusBarElement.Background = _monoForeground;
        }

        private void OnBufferUpdated()
        {
            // Throttle: om redan schemalagd, gör inget
            if (_renderScheduled) return;
            _renderScheduled = true;

            // Hoppa över till UI-tråden
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_session == null)
                        throw new ArgumentNullException(nameof(_session), "TerminalSession null");

                    var buffer = _session.ScreenBuffer;

                    // Uppdatera bara ändrade celler
                    for (int r = 0; r < buffer.Rows; r++)
                    {
                        for (int c = 0; c < buffer.Cols; c++)
                        {
                            char ch = buffer.GetChar(r, c);
                            if (_lastChars[r, c] != ch)
                            {
                                _lastChars[r, c] = ch;
                                var run = _runs[r, c];
                                run.Text = ch.ToString();

                                if (_session.DisplayTheme == TerminalState.DisplayType.FullColor)
                                {
                                    var style = buffer.GetStyle(r, c);
                                    run.Foreground = new SolidColorBrush(ConvertConsoleColor(style.Foreground));
                                    run.Background = new SolidColorBrush(ConvertConsoleColor(style.Background));
                                    run.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
                                }
                                else
                                {
                                    run.Foreground = _monoForeground;
                                    run.Background = _monoBackground;
                                    run.FontWeight = FontWeights.Normal;
                                }
                            }
                        }
                    }

                    // Flytta caret
                    var caretPos = TerminalText.Document.ContentStart;
                    int paragraphOffset = 0;
                    for (int i = 0; i < buffer.CursorRow; i++)
                        paragraphOffset += buffer.Cols + 1; // +1 för paragrafseparator
                    caretPos = caretPos.GetPositionAtOffset(paragraphOffset + buffer.CursorCol, LogicalDirection.Forward) ?? caretPos;
                    TerminalText.CaretPosition = caretPos;

                    // Återställ fokus till input
                    FocusInput();
                }
                finally
                {
                    _lastRender = DateTime.UtcNow;
                    _renderScheduled = false;
                }
            }), DispatcherPriority.Background);
        }

        private void AdjustTerminalWidth(IScreenBuffer buffer)
        {
            var typeface = new Typeface(
                TerminalText.FontFamily,
                TerminalText.FontStyle,
                TerminalText.FontWeight,
                TerminalText.FontStretch);

            var ft = new FormattedText(
                new string('W', buffer.Cols),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                TerminalText.FontSize,
                Brushes.White,
                new NumberSubstitution(),
                1.0);

            double charWidth = ft.WidthIncludingTrailingWhitespace / buffer.Cols;

            double extra = TerminalText.Padding.Left + TerminalText.Padding.Right
                         + TerminalText.BorderThickness.Left + TerminalText.BorderThickness.Right
                         + SystemParameters.VerticalScrollBarWidth;

            double totalWidth = (charWidth * buffer.Cols) + extra;

            // Viktigt: sätt på UserControl-nivå
            this.Width = totalWidth;
        }

        private void AdjustTerminalHeight(IScreenBuffer buffer)
        {
            // Mät radens höjd med rätt DPI
            var dpi = VisualTreeHelper.GetDpi(TerminalText).PixelsPerDip;
            var ft = new FormattedText(
                "W", // ett tecken räcker för höjdmätning
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(TerminalText.FontFamily, TerminalText.FontStyle, TerminalText.FontWeight, TerminalText.FontStretch),
                TerminalText.FontSize,
                Brushes.White,
                dpi
            );

            double lineHeight = ft.Height;

            // Totalhöjd = radhöjd × antal rader + padding + border
            double totalHeight =
                (lineHeight * buffer.Rows) +
                TerminalText.Padding.Top + TerminalText.Padding.Bottom +
                TerminalText.BorderThickness.Top + TerminalText.BorderThickness.Bottom;

            // Om du har en horisontell scrollbar, lägg till dess höjd
            if (TerminalText.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                totalHeight += SystemParameters.HorizontalScrollBarHeight;

            TerminalText.Height = totalHeight;
        }
    }
}