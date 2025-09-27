#nullable disable

using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Config;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.ComponentModel;
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
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Storage.Streams;
using static PT200Emulator.Core.Config.UiConfig;

namespace PT200Emulator.UI
{
    public partial class TerminalControl : UserControl
    {
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _speedTimer;
        private int _bytesReceivedThisSecond = 0;
        internal UiConfig _uiConfig;
        private FixedUniformGrid _terminalGrid;
        //private Canvas _overlayCanvas;
        private Rectangle _caretVisual;
        private TextBox _inputOverlay;
        private LayeredTerminalHost _host;

        public InputController InputController { get; set; } = default!;
        public IInputMapper InputMapper { get; set; }

        private Brush foreGround = Brushes.LimeGreen;
        private Brush backGround = Brushes.Black;

        private DateTime _lastRender = DateTime.MinValue;
        private readonly TimeSpan _renderInterval = TimeSpan.FromMilliseconds(50); // ~20 FPS
        private bool _renderScheduled;
        internal TerminalSession _session;
        private TerminalState _state;
        private bool _layoutReady = false;
        internal IScreenBuffer _attachedBuffer;
        private DispatcherTimer _caretBlinkTimer;
        private bool _caretVisibleState = true;
        public bool ManualInputEnabled = true;
        public bool ManualInputDisabled = false;
        private Brush _defaultForeground = Brushes.LimeGreen;
        private Brush _defaultBackground = Brushes.Black;
        private FontFamily _defaultFontFamily = new FontFamily("Consolas");
        private int _defaultFontSize = 16;
        //private bool _layoutMeasured = false;

        //private string _savedStatusText;
        //private bool _inSystemLine = false;

        private double _cellHeight, _cellWidth;
        private TextBlock[,] _cells;
        private (int row, int col)? _pendingCaret;

        public TerminalControl()
        {
            InitializeComponent();

            // Viktigt: lämna direkt i design-mode för att undvika XDG-fel
            if (DesignerProperties.GetIsInDesignMode(this))
                return;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Kör ingen runtime-init i designern
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            // Skydda mot null i runtime-kontext (om appen laddar långsamt)
            var style = _state?.ScreenBuffer?.CurrentStyle ?? new StyleInfo();

            _host = new LayeredTerminalHost(style);
            _host.TerminalGrid.InitCells(); // skapa celler endast vid runtime
            _terminalGrid = _host.TerminalGrid;

            TerminalHost.Children.Add(_host);

            // Synka overlaymått och fokus när terminalen har faktiska mått
            _host.LayoutUpdated += (_, __) =>
            {
                var w = _host.TerminalGrid.ActualWidth;
                var h = _host.TerminalGrid.ActualHeight;
                if (w > 0 && h > 0)
                {
                    _host.InputOverlay.Width = w;
                    _host.InputOverlay.Height = h;

                    Keyboard.Focus(_host.InputOverlay);
                    _host.InputOverlay.Focus();
                }
            };
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) =>
            {
                ClockTextBlock.Text = DateTime.Now.ToString("HH:mm");
            };
            _clockTimer.Start();

            _speedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _speedTimer.Tick += (s, e) =>
            {
                SpeedTextBlock.Text = $"| ↩ {_bytesReceivedThisSecond} B/s";
                _bytesReceivedThisSecond = 0;
            };
            _speedTimer.Start();
            _caretVisual = _host.CaretVisual;
            _inputOverlay = _host.InputOverlay;
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
            if (session != null) _session = session;
            else
            {
                this.LogStackTrace("[INITIALIZESESSION] session is null!");
                throw new ArgumentNullException(nameof(session));
            }
            //_state = _session._state;
            AttachToBuffer(session._state.ScreenBuffer);
            InputController = session.Controller;
            var count = _state.ScreenBuffer.GetBufferUpdatedHandlerCount();
            this.LogTrace($"[TERMINALCONTROL/INITIALIZESESSION] BufferUpdated handler count: {count}");
            InputMapper = session.Mapper;
            StatusText.Text = $"✅ {session.TerminalId} @ {session.BaudRate} baud";

            // Initiera dokumentet och ev. breddjustering här
            _session.ScreenBuffer.CursorMoved += (row, col) => UpdateCaretPosition(row, col, _attachedBuffer.CurrentStyle.Foreground);
            StartCaretBlink();

            // Viktigt: initiera sessionens interna eventkopplingar (bl.a. DCS-svar)
            await session.InitializeAsync();
            InitCellMetrics();
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
                if (_attachedBuffer.CurrentStyle == null)
                {
                    _attachedBuffer.CurrentStyle = new StyleInfo();
                    _attachedBuffer.CurrentStyle.Foreground = Brushes.LimeGreen;
                    _attachedBuffer.CurrentStyle.Background = Brushes.Black;
                    this.LogDebug("[AttachToBuffer] CurrentStyle was null, default style applied");
                }
            }
        }

        public void FocusInput()
        {
            if (_inputOverlay == null) return;
            _inputOverlay.Focus();
            Keyboard.Focus(_inputOverlay);
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
            Insert.Background = StatusText.Background;

            NumLock.Foreground = Keyboard.IsKeyToggled(Key.NumLock) ? Brushes.Black : StatusText.Background;
            CapsLock.Foreground = Keyboard.IsKeyToggled(Key.CapsLock) ? Brushes.Purple : StatusText.Background;
            ScrollLock.Foreground = Keyboard.IsKeyToggled(Key.Scroll) ? Brushes.Red : StatusText.Background;
            Insert.Foreground = Keyboard.IsKeyToggled(Key.Insert) ? Brushes.Black : StatusText.Background;
        }

        public void InitDocument(IScreenBuffer buffer)
        {
            if (buffer == null)
            {
                this.LogWarning("[InitDocument] Skipped – buffer is null");
                return;
            }
            if (_terminalGrid == null)
            {
                this.LogWarning("InitDocument avbröts: _terminalGrid ej initierad");
                return;
            }

            // Fortsätt med layout och rendering

            _terminalGrid.Children.Clear();
            _terminalGrid.Rows = buffer.Rows;
            _terminalGrid.Columns = buffer.Cols;

            _cells = new TextBlock[buffer.Rows, buffer.Cols];

            for (int r = 0; r < buffer.Rows; r++)
            {
                for (int c = 0; c < buffer.Cols; c++)
                {
                    var tb = new TextBlock
                    {
                        Text = " ",
                        Foreground = _defaultForeground,
                        Background = _defaultBackground,
                        FontFamily = _defaultFontFamily,
                        FontSize = _defaultFontSize,
                        FontWeight = FontWeights.Normal,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0),
                        TextAlignment = TextAlignment.Left
                    };
                    TextOptions.SetTextRenderingMode(tb, TextRenderingMode.Aliased);
                    TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);

                    _terminalGrid.Children.Add(tb);
                    _cells[r, c] = tb;
                }

                // Använd mått från state
                _terminalGrid.Height = _cellHeight * buffer.Rows;
                _layoutReady = true;
            }
        }

        public void RenderFromBuffer(IScreenBuffer buffer, bool forceRepaint = false)
        {
            if (_cells == null) return;
            // OBS: Ingen "dirty"-skip här om forceRepaint == true
            for (int r = 0; r < buffer.Rows; r++)
            {
                for (int c = 0; c < buffer.Cols; c++)
                {
                    var tb = _cells[r, c];
                    var style = buffer.GetStyle(r, c);
                    var ch = buffer.GetChar(r, c);

                    // Sätt text alltid (eller bara vid förändring om du vill)
                    tb.Text = ch.ToString();

                    // Hantera attribut först (reverse, low-intensity)
                    Brush fg = style.Foreground;
                    Brush bg = style.Background;

                    if (style.ReverseVideo)
                    {
                        var tmp = fg;
                        fg = bg;
                        bg = tmp;
                    }
                    if (style.LowIntensity)
                    {
                        // enkel dämpning: 70% på foreground
                        fg = new SolidColorBrush(((SolidColorBrush)fg).Color) { Opacity = 0.7 };
                    }

                    // Om forceRepaint: alltid applicera färg, annars bara om det behövs
                    if (forceRepaint || tb.Foreground != fg)
                        tb.Foreground = fg;
                    if (forceRepaint || tb.Background != bg)
                        tb.Background = bg;
                }
            }
        }

        public void UpdateStatus(string statusText, Brush color)
        {
            StatusText.Text = statusText;
            StatusText.Foreground = color;
        }

        public void SetDisplayTheme(TerminalState.DisplayType theme)
        {
            // 1) Välj tema
            switch (theme)
            {
                case TerminalState.DisplayType.Green:
                    foreGround = Brushes.LimeGreen; backGround = Brushes.Black; break;
                case TerminalState.DisplayType.Blue:
                    foreGround = Brushes.DeepSkyBlue; backGround = Brushes.Black; break;
                case TerminalState.DisplayType.Amber:
                    foreGround = new SolidColorBrush(Color.FromRgb(255, 126, 0)); backGround = Brushes.Black; break;
                case TerminalState.DisplayType.White:
                case TerminalState.DisplayType.FullColor:
                default:
                    foreGround = Brushes.White; backGround = Brushes.Black; break;
            }

            // 2) Statusrad alltid reverse video
            StatusBarElement.Foreground = backGround;
            StatusBarElement.Background = foreGround;

            // 3) Uppdatera StyleInfo (ConsoleColor) för alla celler
            if (_state?.ScreenBuffer == null) return;

            _attachedBuffer.CurrentStyle.Foreground = foreGround;
            _attachedBuffer.CurrentStyle.Background = backGround;

            for (int r = 0; r < _state.ScreenBuffer.Rows; r++)
            {
                for (int c = 0; c < _state.ScreenBuffer.Cols; c++)
                {
                    var cell = _state.ScreenBuffer[r, c];
                    var s = cell.Style;
                    if (s == null) continue;

                    s.LowIntensity = false;
                    s.Foreground = foreGround;
                    s.Background = backGround;
                    cell.Foreground = s.Foreground;
                    cell.Background = s.Background;
                    // Valfritt: klara reverse här om din statusrad ligger i bufferten
                    // s.ReverseVideo = false; // för vanliga celler
                }
            }

            // 4) Tvinga full färgomritning på UI-tråden efter att layouten är klar
            Dispatcher.InvokeAsync(() =>
            {
                RenderFromBuffer(_state.ScreenBuffer, forceRepaint: true);
                UpdateCaretPosition(_state.ScreenBuffer.CursorRow, _state.ScreenBuffer.CursorCol, foreGround);
            }, System.Windows.Threading.DispatcherPriority.Render);
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
            if (_terminalGrid == null)
            {
                this.LogWarning("AdjustTerminalWidth avbröts: VisualTreeHelper.GetDpi(_terminalGrid) returnerade null");
                return;
            }
            var typeface = new Typeface(
                _defaultFontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            var ft = new FormattedText(
                new string('W', buffer.Cols),
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                _defaultFontSize,
                Brushes.White,
                new NumberSubstitution(),
                1.0);

            double charWidth = ft.WidthIncludingTrailingWhitespace / buffer.Cols;

            double extra = SystemParameters.VerticalScrollBarWidth; // om du har scrollbar

            double totalWidth = (charWidth * buffer.Cols) + extra;

            this.Width = totalWidth;
        }

        internal void AdjustTerminalHeight(IScreenBuffer buffer)
        {
            if (_terminalGrid == null)
            {
                this.LogWarning("AdjustTerminalHeight avbröts: VisualTreeHelper.GetDpi(_terminalGrid) returnerade null");
                return;
            }
            var dpi = VisualTreeHelper.GetDpi(_terminalGrid).PixelsPerDip;
            var ft = new FormattedText(
                "W",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(_defaultFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                _defaultFontSize,
                Brushes.White,
                dpi);

            double lineHeight = ft.Height;

            double totalHeight = _cellHeight * buffer.Rows;
            _terminalGrid.Height = totalHeight;
            this.Height = totalHeight;
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

            // 1. Avsluta ev. pågående burst
            state.ScreenBuffer?.ForceEndUpdate();

            // 2. Ändra storlek
            state.ScreenBuffer.Resize(state.Rows, state.Cols);

            // 3. Clampa cursor
            int clampedRow = Math.Min(state.ScreenBuffer.CursorRow, state.Rows - 1);
            int clampedCol = Math.Min(state.ScreenBuffer.CursorCol, state.Cols - 1);
            state.ScreenBuffer.SetCursorPosition(clampedCol, clampedRow);
            this.LogTrace($"[ApplyScreenFormatChange] Pos={clampedRow}, {clampedCol}");

            // 4. Initiera layout
            InitDocument(state.ScreenBuffer);
            AdjustTerminalWidth(state.ScreenBuffer);
            AdjustTerminalHeight(state.ScreenBuffer);

            // 5. Markera dirty
            state.ScreenBuffer.MarkDirty();

            // 6. Justera fönsterhöjd efter layout
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_cells?[0, 0].ActualHeight > 0 && StatusBarElement.ActualHeight > 0)
                {
                    double cellHeight = _cells[0, 0].ActualHeight;
                    double totalHeight = _terminalGrid.DesiredSize.Height + StatusBarElement.ActualHeight;

                    if (Window.GetWindow(this) is Window window)
                    {
                        window.Height = totalHeight + 40; // +40 för chrome, padding etc
                        this.LogDebug($"[ApplyScreenFormatChange] Window.Height set to {window.Height}");
                        this.LogDebug($"[Layout] _terminalGrid Actual: {_terminalGrid.ActualHeight}, Desired: {_terminalGrid.DesiredSize.Height}");
                        this.LogDebug($"[Layout] StatusBar Actual: {StatusBarElement.ActualHeight}, Desired: {StatusBarElement.DesiredSize.Height}");
                    }

                    // 7. Placera caret
                    var caretBrush = _cells[clampedRow, clampedCol].Foreground;
                    UpdateCaretPosition(clampedRow, clampedCol, caretBrush);
                }
            }), DispatcherPriority.Loaded);

            _layoutReady = true;
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
                _cells == null ||
                _cells.GetLength(0) != buffer.Rows ||
                _cells.GetLength(1) != buffer.Cols;

            if (dimensionsMismatch)
            {
                this.LogWarning("[SafeRender] Reinitializing layout due to dimension mismatch");

                this.LogDebug($"Dimensions mismatch: {dimensionsMismatch}. Skapar om dokumentet med InitDocument!");
                InitDocument(buffer);
                AdjustTerminalWidth(buffer);
                AdjustTerminalHeight(buffer);
            }


            RenderFromBuffer(buffer);
            UpdateCaretPosition(_state.ScreenBuffer.CursorRow, _state.ScreenBuffer.CursorCol, _attachedBuffer.CurrentStyle.Foreground);
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

        public void UpdateCaretPosition(int row, int col, Brush foreground)
        {
            if (_cells == null || _cells[0, 0].ActualWidth == 0)
            {
                // Layouten är inte klar – spara positionen
                _pendingCaret = (row, col);
                return;
            }
            double cw = _cells[0, 0].ActualWidth;
            double ch = _cells[0, 0].ActualHeight;

            double x = col * cw;
            double y = row * ch;

            // Välj stil
            switch (_uiConfig.CaretStylePreference)
            {
                case CaretStyle.VerticalBar:
                    _caretVisual.Width = Math.Max(1, cw * 0.1);
                    _caretVisual.Height = ch;
                    Canvas.SetLeft(_caretVisual, x + Math.Min(GetGlyphWidth('X'), cw - _caretVisual.Width));
                    Canvas.SetTop(_caretVisual, y);
                    break;
                case CaretStyle.Underscore:
                    _caretVisual.Width = cw;
                    _caretVisual.Height = Math.Max(1, ch * 0.1);
                    Canvas.SetLeft(_caretVisual, x);
                    Canvas.SetTop(_caretVisual, y + ch - _caretVisual.Height);
                    break;
                case CaretStyle.Block:
                default:
                    _caretVisual.Width = cw;
                    _caretVisual.Height = ch;
                    Canvas.SetLeft(_caretVisual, x);
                    Canvas.SetTop(_caretVisual, y);
                    break;
            }

            _caretVisual.Visibility = Visibility.Visible;
            _caretVisual.Fill = foreground ?? Brushes.LimeGreen;
            //Panel.SetZIndex(_overlayCanvas, 1000);
        }


        private void InitCellMetrics()
        {
            if (_terminalGrid == null)
            {
                this.LogWarning("InitCellMetrics avbröts: VisualTreeHelper.GetDpi(_terminalGrid) returnerade null");
                return;
            }
            var pixelsPerDip = VisualTreeHelper.GetDpi(_terminalGrid).PixelsPerDip;
            var typeface = new Typeface(
                _defaultFontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal
            );

            var ft = new FormattedText(
                "W",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                _defaultFontSize,
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
                _caretVisual.Visibility = _caretVisibleState ? Visibility.Visible : Visibility.Collapsed;
            };
            _caretBlinkTimer.Start();
        }

        private void StopCaretBlink()
        {
            _caretBlinkTimer?.Stop();
            _caretVisual.Visibility = Visibility.Visible;
        }

        private void TerminalHost_LayoutUpdated(object sender, EventArgs e)
        {
            if (_pendingCaret.HasValue)
            {
                var (row, col) = _pendingCaret.Value;
                PlaceCaret(row, col);
                _pendingCaret = null; // rensa
            }
        }

        private void PlaceCaret(int row, int col)
        {
            double cw = _cells[0, 0].ActualWidth;
            double ch = _cells[0, 0].ActualHeight;

            Canvas.SetLeft(_caretVisual, col * cw);
            Canvas.SetTop(_caretVisual, row * ch);
            _caretVisual.Visibility = Visibility.Visible;
        }

        public static readonly DependencyProperty RowsProperty =
    DependencyProperty.Register(nameof(Rows), typeof(int), typeof(FixedUniformGrid),
        new FrameworkPropertyMetadata(24, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int Rows
        {
            get => (int)GetValue(RowsProperty);
            set => SetValue(RowsProperty, value);
        }
    }
}