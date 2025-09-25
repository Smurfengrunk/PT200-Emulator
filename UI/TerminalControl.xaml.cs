#nullable disable

using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Config;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Storage.Streams;
using static PT200Emulator.Core.Config.UiConfig;

namespace PT200Emulator.UI
{
    public partial class TerminalControl : UserControl
    {
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _speedTimer;
        private int _bytesReceivedThisSecond = 0;
        private Run[,] _runs;
        private UiConfig _uiConfig;

        public InputController InputController { get; set; } = default!;
        public IInputMapper InputMapper { get; set; }

        private Brush foreGround = Brushes.LimeGreen;
        private Brush backGround = Brushes.Black;

        private DateTime _lastRender = DateTime.MinValue;
        private readonly TimeSpan _renderInterval = TimeSpan.FromMilliseconds(50); // ~20 FPS
        private bool _renderScheduled;
        private char[,] _lastChars; // för ändringsdetektering
        private TerminalSession _session;
        private TerminalState _state;
        private bool _layoutReady = false;
        internal IScreenBuffer _attachedBuffer;
        private DispatcherTimer _caretBlinkTimer;
        private bool _caretVisibleState = true;
        public bool ManualInputEnabled = true;
        public bool ManualInputDisabled = false;

        //private string _savedStatusText;
        //private bool _inSystemLine = false;

        private double _cellHeight, _cellWidth;

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
            this.LogTrace($"[TERMINALCONTROL] Hashcode: {this.GetHashCode()}");
            CaretVisual.Visibility = Visibility.Visible;
        }

        public void InitializeInput(InputController controller, IInputMapper mapper = null)
        {
            InputController = controller;
            InputMapper = mapper;
        }

        public async Task InitializeSession(TerminalSession session, UiConfig config)
        {
            _uiConfig = config;
            // Nu byter vi till den nya*/
            _session = session ?? throw new ArgumentNullException(nameof(session));
            //_state = _session._state;
            AttachToBuffer(session._state.ScreenBuffer);
            InputController = session.Controller;
            var count = _state.ScreenBuffer.GetBufferUpdatedHandlerCount();
            this.LogTrace($"[TERMINALCONTROL/INITIALIZESESSION] BufferUpdated handler count: {count}");
            InputMapper = session.Mapper;
            StatusText.Text = $"✅ {session.TerminalId} @ {session.BaudRate} baud";

            // Initiera dokumentet och ev. breddjustering här
            _layoutReady = false;
            InitDocument(_state.ScreenBuffer);
            AdjustTerminalWidth(_state.ScreenBuffer);
            AdjustTerminalHeight(_state.ScreenBuffer);
            _layoutReady = true;

            SafeRender();
            _session.ScreenBuffer.CursorMoved += (row, col) => UpdateCaretPosition(row, col, 'W', TerminalText.Foreground);
            StartCaretBlink();

            // Viktigt: initiera sessionens interna eventkopplingar (bl.a. DCS-svar)
            await session.InitializeAsync();
        }

        internal void AttachToBuffer(IScreenBuffer buffer)
        {
            // Inget att göra om redan korrekt kopplad
            if (_attachedBuffer == buffer)
                return;

            // Koppla ur från tidigare buffer om den fanns
            if (_attachedBuffer != null)
            {
                _attachedBuffer.BufferUpdated -= OnBufferUpdated;
                this.LogTrace($"[TerminalControl] Detached from buffer {_attachedBuffer.GetHashCode()}");
            }

            // Koppla in till nya buffer
            _attachedBuffer = buffer;
            if (_attachedBuffer != null)
            {
                _attachedBuffer.BufferUpdated += OnBufferUpdated;
                this.LogTrace($"[TerminalControl] Attached to buffer {_attachedBuffer.GetHashCode()}");
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            TerminalText.Document.Foreground = Brushes.LimeGreen;
            TerminalText.Document.Background = Brushes.Black;
            TextOptions.SetTextRenderingMode(TerminalText, TextRenderingMode.Aliased);
            TextOptions.SetTextFormattingMode(TerminalText, TextFormattingMode.Display);
            foreach (var block in TerminalText.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    p.Foreground = Brushes.LimeGreen;
                    p.Background = Brushes.Black;
                }
            }

            if (_session != null)
            {
                FocusInput();
                UpdateLockIndicators();
                AdjustTerminalWidth(_session.ScreenBuffer);
                AdjustTerminalHeight(_session.ScreenBuffer);
                this.LogTrace("[USERCONTROL_LOADED] Session initialized");
            }
            else this.LogDebug("[USERCONTROL_LOADED] Session not initialized");
            TerminalText.InvalidateVisual();
        }

        public void FocusInput()
        {
            InputOverlay.Focus();
            Keyboard.Focus(InputOverlay);
        }

        private async void InputOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ManualInputEnabled)
            {
                if (await InputController.DetectSpecialKey(e))
                {
                    e.Handled = true;
                    return;
                }

                // Låt InputMapper göra jobbet
                var bytes = InputMapper.MapKey(e.Key, Keyboard.Modifiers);

                if (bytes != null)
                {
                    // Specialfall för backspace i Prime-läge
                    if (e.Key == Key.Back)
                        bytes = new byte[] { 0x08, 0x20, 0x08 };

                    await InputController.SendRawAsync(bytes);
                    e.Handled = true;
                    return;
                }

                // Hantera låsindikatorer separat
                if (e.Key == Key.NumLock || e.Key == Key.CapsLock || e.Key == Key.Scroll)
                    UpdateLockIndicators();
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

        internal void InitDocument(IScreenBuffer buffer)
        {
            this.LogTrace($"[INITDOCUMENT] Screenbuffer hashcode: {buffer.GetHashCode()}, Rows: {buffer.Rows}  Cols: {buffer.Cols}");
            _runs = new Run[buffer.Rows, buffer.Cols];
            // Räkna ut teckenbredd för att sätta PageWidth
            var typeface = new Typeface(TerminalText.FontFamily, TerminalText.FontStyle, TerminalText.FontWeight, TerminalText.FontStretch);

            var pixelsPerDip = VisualTreeHelper.GetDpi(TerminalText).PixelsPerDip;
            var ft = new FormattedText(
                "W",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                TerminalText.FontSize,
                Brushes.White,
                pixelsPerDip // <-- rätt DPI här
            );
            InitCellMetrics();
            //double extra = TerminalText.Padding.Left + TerminalText.Padding.Right
            //             + TerminalText.BorderThickness.Left + TerminalText.BorderThickness.Right;

            double totalWidth = (_cellWidth * buffer.Cols)/* + extra*/;

            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                LineHeight = _cellHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                PageWidth = totalWidth
            };

            TerminalText.IsUndoEnabled = false;
            TerminalText.AllowDrop = false;
            TerminalText.IsDocumentEnabled = false;

            _lastChars = new char[buffer.Rows, buffer.Cols];
            for (int r = 0; r < buffer.Rows; r++)
                for (int c = 0; c < buffer.Cols; c++)
                    _lastChars[r, c] = '\0';

            for (int r = 0; r < buffer.Rows; r++)
            {
                var p = new Paragraph
                {
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    LineHeight = _cellHeight,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                };
                for (int c = 0; c < buffer.Cols; c++)
                {
                    var run = new Run(" ")
                    {
                        Foreground = Foreground,
                        Background = Background
                    };
                    _runs[r, c] = run;
                    p.Inlines.Add(run);
                }
                doc.Blocks.Add(p);
            }

            TerminalText.Document = doc;
            TerminalText.Width = totalWidth; // valfritt, om du vill låsa bredden
            TerminalText.Height = buffer.Rows * _cellHeight;
            TerminalText.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        }

        private void UpdateCell(int row, int col, char ch, StyleInfo style, bool fullColor)
        {
            var run = (_runs != null) ? _runs[row, col] : throw new ArgumentNullException("_runs null");
            run.Text = ch.ToString();

            if (fullColor && style != null)
            {
                run.Foreground = new SolidColorBrush(ConvertConsoleColor(style.Foreground));
                run.Background = new SolidColorBrush(ConvertConsoleColor(style.Background));
            }
            else if (style != null)
            {
                run.Foreground = new SolidColorBrush(ConvertConsoleColor(_state.ScreenBuffer.CurrentStyle.Foreground));
                run.Background = new SolidColorBrush(ConvertConsoleColor(_state.ScreenBuffer.CurrentStyle.Background));
            }
            else
            {
                this.LogError($"[UPDATECELL] style is null for cell at {row} {col}");
                return;
            }

            run.FontWeight = style?.Bold == true ? FontWeights.Bold : FontWeights.Normal;
            if (row == 0 && col == 0)
            {
                run.Foreground = Brushes.White;
                run.Background = Brushes.Black;
                run.FontWeight = FontWeights.Bold;
                TextOptions.SetTextFormattingMode(run, TextFormattingMode.Display);
            }

            //this.LogDebug($"UpdateCell: Pos=({row},{col}), FG={style.Foreground}, BG={style.Background}, LowIntensity={style.LowIntensity}");
        }

        public void RenderFromBuffer(IScreenBuffer buffer, bool fullColor = true)
        {
            //this.LogTrace($"[RENDERFROMBUFFER] Start of RenderFromBuffer, Dirty flag: {buffer.GetDirty()}");
            try
            {
                if (_runs == null || _lastChars == null)
                {
                    this.LogWarning("[RenderFromBuffer] Skipped – layout arrays not initialized");
                    return;
                }

                if (_runs.GetLength(0) != buffer.Rows || _runs.GetLength(1) != buffer.Cols ||
                    _lastChars.GetLength(0) != buffer.Rows || _lastChars.GetLength(1) != buffer.Cols)
                {
                    this.LogWarning("[RenderFromBuffer] Skipped – layout arrays mismatch");
                    return;
                }

                buffer = _state.ScreenBuffer;
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

                UpdateCaretPosition(buffer.CursorRow, buffer.CursorCol, 'W', TerminalText.Foreground);
                _lastRender = DateTime.UtcNow;
                FocusInput();
                this.LogTrace($"[RENDERFROMBUFFER] End of RenderBuffer, Dirty flag: {buffer.GetDirty()}");
            }
            finally
            {
                buffer.ClearDirty();
            }
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
                case TerminalState.DisplayType.Green:
                    foreGround = Brushes.LimeGreen;
                    backGround = Brushes.Black;
                    break;
                case TerminalState.DisplayType.Blue:
                    foreGround = Brushes.DeepSkyBlue;
                    backGround = Brushes.Black;
                    break;
                case TerminalState.DisplayType.Amber:
                    foreGround = new SolidColorBrush(Color.FromRgb(255, 191, 0));
                    backGround = Brushes.Black;
                    break;
                case TerminalState.DisplayType.White:
                case TerminalState.DisplayType.FullColor:
                default:
                    foreGround = Brushes.White;
                    backGround = Brushes.Black;
                    break;
            }

            // Ny text
            TerminalText.Background = backGround;
            TerminalText.Foreground = foreGround;

            // Befintlig text
            foreach (var block in TerminalText.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (var inline in p.Inlines)
                    {
                        if (inline is Run r)
                        {
                            r.Foreground = foreGround;
                            r.Background = backGround;
                        }
                    }
                }
            }

            // Statusrad
            StatusBarElement.Foreground = backGround;
            StatusBarElement.Background = foreGround;

            // Uppdatera även ScreenBuffer om du har tillgång till den
            if (_state.ScreenBuffer != null)
            {
                _state.ScreenBuffer.CurrentStyle.Foreground = ConsoleColor.Green;
                _state.ScreenBuffer.CurrentStyle.Background = ConsoleColor.Black;

                for (int r = 0; r < _state.ScreenBuffer.Rows; r++)
                    for (int c = 0; c < _state.ScreenBuffer.Cols; c++)
                    {
                        var cell = _state.ScreenBuffer[r, c];
                        if (cell.Style != null)
                        {
                            cell.Style.LowIntensity = false;
                            cell.Foreground = foreGround;
                            cell.Background = backGround;
                        }
                    }


                _state.ScreenBuffer.MarkDirty(); // trigga redraw
            }
            this.LogStackTrace();
        }

        private void OnBufferUpdated()
        {
            this.LogTrace($"[TERMINALCONTROL] OnBufferUpdated fired – buffer hash: {this._attachedBuffer?.GetHashCode()}");
            //this.LogTrace($"[OnBufferUpdated] Triggered from buffer {_state?.ScreenBuffer?.GetHashCode()}");
            //this.LogStackTrace("[OnBufferUpdated]");
            if (!_layoutReady)
            {
                this.LogDebug("[OnBufferUpdated] Skipped – layout not ready");
                return;
            }

            if (_renderScheduled) return;
            _renderScheduled = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _renderScheduled = false;
                    SafeRender();
                }
                finally
                {
                    _lastRender = DateTime.UtcNow;
                    _renderScheduled = false;
                }
            }), DispatcherPriority.Background);
        }

        internal void AdjustTerminalWidth(IScreenBuffer buffer)
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

        internal void AdjustTerminalHeight(IScreenBuffer buffer)
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
                (lineHeight * buffer.Rows)/* +
                TerminalText.Padding.Top + TerminalText.Padding.Bottom +
                TerminalText.BorderThickness.Top + TerminalText.BorderThickness.Bottom*/;

            // Om du har en horisontell scrollbar, lägg till dess höjd
            if (TerminalText.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                totalHeight += SystemParameters.HorizontalScrollBarHeight;

            TerminalText.Height = totalHeight;
        }

        public void SetTerminalState(TerminalState state)
        {
            _state = state;
            this.LogTrace($"[TerminalControl] BufferUpdated += → handler count: {_state.ScreenBuffer.GetBufferUpdatedHandlerCount()}");

            // Koppla bort tidigare buffert om den finns
            if (_state.ScreenBuffer != null)
            {

                this.LogTrace($"[TERMINALCONTROL/SETTERMINALSTATE] Hashcode: {this.GetHashCode()}");
                _state.ScreenBuffer.BufferUpdated -= OnBufferUpdated;
                this.LogTrace($"[TERMINALCONTROL/SETTERMINALSTATE] Kopplade ur gammal BufferUpdated med ScreenBuffer hashcode {_state.ScreenBuffer.GetHashCode()}");

            }
            ApplyScreenFormatChange(state);
        }

        public void ApplyScreenFormatChange(TerminalState state)
        {
            _layoutReady = false;
            // 1. Avsluta ev. pågående burst om TerminalControl har referens till MainWindow eller burst-hanteraren
            if (state.ScreenBuffer != null)
            {
                state.ScreenBuffer.ForceEndUpdate(); // den lilla hjälpfunktionen vi la i ScreenBuffer
            }

            // 2. Ändra storlek
            state.ScreenBuffer.Resize(state.Rows, state.Cols);

            // 3. Clampa cursor
            state.ScreenBuffer.SetCursorPosition(
                Math.Min(state.ScreenBuffer.CursorCol, state.Cols - 1),
                Math.Min(state.ScreenBuffer.CursorRow, state.Rows - 1)
            );
            this.LogTrace($"[ApplyScreenFormatChange] Pos={Math.Min(state.ScreenBuffer.CursorCol, state.Cols - 1)},{Math.Min(state.ScreenBuffer.CursorRow, state.Rows - 1)})");


            // 4. Initiera dokumentlayout
            InitDocument(state.ScreenBuffer);
            AdjustTerminalWidth(state.ScreenBuffer);
            AdjustTerminalHeight(state.ScreenBuffer);

            // 5. Markera dirty så att hela ytan ritas om
            state.ScreenBuffer.MarkDirty();

            _layoutReady = true;
            UpdateCaretPosition(Math.Min(state.ScreenBuffer.CursorCol, state.Cols - 1), Math.Min(state.ScreenBuffer.CursorRow, state.Rows - 1), 'W', TerminalText.Foreground);
        }
        private void SafeRender()
        {
            if (_state.ScreenBuffer._updateDepth > 0)
            {
                this.LogTrace("[RENDER] Skipped due to UpdateDepth > 0");
                return;
            }
            if (_session == null)
            {
                this.LogWarning("[SafeRender] Skipped – session not initialized");
                return;
            }
            if (!_layoutReady)
            {
                this.LogDebug("[SafeRender] Skipped – layout not ready");
                return;
            }

            if (_session.ScreenBuffer == null)
            {
                this.LogWarning("[SafeRender] Skipped – session or buffer is null");
                return;
            }

            var buffer = _session?.ScreenBuffer;
            if (buffer == null)
            {
                this.LogWarning("[SafeRender] Skipped – buffer is null");
                return;
            }

            if (!buffer.GetDirty())
            {
                this.LogTrace("[RENDER] Skipped – no dirty changes");
                return;
            }

            // Kontrollera dimensionsmatchning
            bool dimensionsMismatch =
                _lastChars == null || _runs == null ||
                _lastChars.GetLength(0) != buffer.Rows ||
                _lastChars.GetLength(1) != buffer.Cols ||
                _runs.GetLength(0) != buffer.Rows ||
                _runs.GetLength(1) != buffer.Cols;

            if (dimensionsMismatch)
            {
                this.LogWarning("[SafeRender] Reinitializing layout due to dimension mismatch");

                this.LogDebug($"Dimensions mismatch: {dimensionsMismatch}. Skapar om dokumentet med InitDocument!");
                InitDocument(buffer); // Återskapa _runs och _lastChars
                AdjustTerminalWidth(buffer);
                AdjustTerminalHeight(buffer);
            }


            RenderFromBuffer(buffer);
        }

        // TerminalControl
        internal void DetachFromBuffer()
        {
            if (_attachedBuffer != null)
            {
                _attachedBuffer.BufferUpdated -= OnBufferUpdated;
                this.LogTrace($"[TerminalControl] Detached från buffer {_attachedBuffer.GetHashCode()}");
                _attachedBuffer = null;
            }
        }

        private void UpdateCaretPosition(int row, int col, char ch, Brush foreground)
        {
            double baseX = col * _cellWidth + GetGlyphWidth('0');
            double baseY = row * _cellHeight;
            double glyphWidth = GetGlyphWidth(ch);

            double caretWidth;
            double caretHeight;
            double caretX = baseX;
            double caretY = baseY;

            if (_uiConfig == null) return;

            switch (_uiConfig.CaretStylePreference)
            {
                case CaretStyle.VerticalBar:
                    caretWidth = Math.Max(1, _cellWidth * 0.1);
                    caretHeight = _cellHeight;
                    caretX = baseX + Math.Min(glyphWidth, _cellWidth - caretWidth);
                    break;

                case CaretStyle.Underscore:
                    caretWidth = _cellWidth;
                    caretHeight = Math.Max(1, _cellHeight * 0.1);
                    caretY = baseY + _cellHeight - caretHeight;
                    caretX = baseX + caretWidth - 1;
                    break;

                case CaretStyle.Block:
                    caretWidth = _cellWidth;
                    caretHeight = _cellHeight;
                    break;

                default:
                    caretWidth = Math.Max(1, _cellWidth * 0.1);
                    caretHeight = _cellHeight;
                    break;
            }

            CaretVisual.Margin = new Thickness(caretX, caretY, 0, 0);
            CaretVisual.Width = caretWidth;
            CaretVisual.Height = caretHeight;
            CaretVisual.Fill = foreground;
            CaretVisual.Visibility = Visibility.Visible;
        }

        // 1) Mät cellbredd en gång
        private void InitCellMetrics()
        {
            var pixelsPerDip = VisualTreeHelper.GetDpi(TerminalText).PixelsPerDip;
            var typeface = new Typeface(
                TerminalText.FontFamily,
                TerminalText.FontStyle,
                TerminalText.FontWeight,
                TerminalText.FontStretch
            );

            var ft = new FormattedText(
                "W", // referenstecken
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                TerminalText.FontSize,
                Brushes.Transparent,
                pixelsPerDip
            );

            _cellWidth = ft.WidthIncludingTrailingWhitespace;
            _cellHeight = ft.Height;
        }

        private double GetGlyphWidth(char ch)
        {
            return _cellWidth;
        }

        private void StartCaretBlink()
        {
            _caretBlinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _caretBlinkTimer.Tick += (s, e) =>
            {
                _caretVisibleState = !_caretVisibleState;
                CaretVisual.Visibility = _caretVisibleState ? Visibility.Visible : Visibility.Collapsed;
            };
            _caretBlinkTimer.Start();
        }

        private void StopCaretBlink()
        {
            _caretBlinkTimer?.Stop();
            CaretVisual.Visibility = Visibility.Visible;
        }
    }
}