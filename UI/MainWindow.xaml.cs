using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using PT200Emulator.Core.Config;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.Core.Rendering;
using PT200Emulator.DummyImplementations;
using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.Infrastructure.Networking;
using PT200Emulator.Protocol;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Net;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Windows.Media.Capture;

namespace PT200Emulator.UI
{
    public partial class MainWindow : Window
    {
        private ILoggerFactory loggerFactory;
        private ILogger _logger;
        internal static ILogger Logger { get; private set; }
        private ITransport _transport;
        private ITerminalParser _parser;
        private readonly IRenderer _renderer;
        private readonly IInputMapper _inputMapper;
        private InputController _inputController;
        private readonly Channel<byte[]> _txChannel;
        private readonly Channel<byte[]> _rxChannel;
        private readonly ConfigService _configService;
        private TelnetInterpreter _telnetInterpreter;
        private TelnetSessionBridge _telnetSessionBridge, _oldBridge;
        private string host = "localhost";
        private int port = 2323;
        private TerminalSession session;
        private DcsSequenceHandler dcsHandler;
        private DataPathProvider dataPathProvider;
        private TerminalState state;
        private VisualAttributeManager visualAttributeManager = new VisualAttributeManager();
        internal static ScreenBuffer ScreenBuffer {  get; private set; }
        private string basePath;
        private readonly TimeSpan _burstIdle = TimeSpan.FromMilliseconds(8);
        private TransportConfig cfg;
        private UiConfig _uiConfig;
        private CsiSequenceHandler csiHandler;
        private ModeManager modeManager;
        private LocalizationProvider localizationProvider;
        
        // Fält
        private CancellationTokenSource _burstCts;
        private IDisposable _burstScope;
        private bool _burstActive;
        private DateTime _lastUserSendUtc;
        private long _burstBytes;
        private int _burstChunks;

        // Tuning
        private static readonly TimeSpan SmallIdle = TimeSpan.FromMilliseconds(10);   // typing
        private static readonly TimeSpan LargeIdle = TimeSpan.FromMilliseconds(55);   // server burst
        private static readonly TimeSpan MaxIdle = TimeSpan.FromMilliseconds(140);  // hard cap






        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        public bool TerminalReady { get; private set; }
        internal ILoggerFactory LoggerFactory { get => loggerFactory; set => loggerFactory = value; }

        private bool _isConnected = false;

        public MainWindow() : this(null, null, null, null, null, null) { }

        public MainWindow(
            ITransport transport = null,
            ITerminalParser parser = null,
            IScreenBuffer screenBuffer = null,
            IRenderer renderer = null,
            IInputMapper inputMapper = null,
            ILoggerFactory loggerFactory = null)
        {
            InitializeComponent();

            basePath = AppDomain.CurrentDomain.BaseDirectory;
            dataPathProvider = new DataPathProvider(basePath);
            state = new TerminalState(new CharTableManager(System.IO.Path.Combine(basePath, "data", "chartables", "g0.json"), System.IO.Path.Combine(basePath, "data", "chartables", "g1.json")), basePath);
            ScreenBuffer = state.ScreenBuffer;
            this.LogDebug($"[MAINWINDOW] Jobbar med TerminalState hashcode {state.GetHashCode()}");
            InitializeTerminal();

            _configService = new ConfigService(System.IO.Path.Combine(basePath, "config"));
            cfg = _configService.LoadTransportConfig();
            _uiConfig = _configService.LoadUiConfig();
            LogLevelCombo.ItemsSource = Enum.GetValues(typeof(LogLevel));
            LogLevelCombo.SelectionChanged += (s, e) =>
            {
                this.LogTrace($"SelectionChanged fired. SelectedItem type: {LogLevelCombo.SelectedItem?.GetType().FullName ?? "null"}");
                if (LogLevelCombo.SelectedItem is LogLevel level)
                {
                    LoggerFactoryProvider.SetMinimumLevel(level);
                    this.LogInformation($"Loggnivå ändrad till {level}");
                }
            };
            LoadUiConfig();

            LoggerFactory = loggerFactory ?? new LoggerFactory();
            _logger = LoggerFactory.CreateLogger("MainWindow");
            Logger = _logger;

            _transport = transport ?? new TcpClientTransport();
            _renderer = renderer ?? new DummyImplementations.Renderer();
            _inputMapper = inputMapper ?? new PT200Emulator.Core.Input.InputMapper();

            _telnetInterpreter = new TelnetInterpreter();
            _inputController = new InputController(_inputMapper, bytes => _telnetInterpreter.SendToServer(bytes));

            // Koppla UI till parserns aktuella buffer
            Terminal.InputController = _inputController;
            Terminal.InputMapper = _inputMapper;

            this.LogTrace($"[INIT] BufferUpdated handler count: {state.ScreenBuffer.GetBufferUpdatedHandlerCount()}");
            this.LogTrace($"[MAINWINDOW] ScreenBuffer Hashcode: {state.ScreenBuffer.GetHashCode()}");
            this.LogTrace($"[MAINWINDOW] TerminalControl Hashcode: {Terminal.GetHashCode()}");

            _txChannel = Channel.CreateUnbounded<byte[]>();
            _rxChannel = Channel.CreateUnbounded<byte[]>();

            HostTextBox.Text = cfg.Host;
            PortTextBox.Text = cfg.Port.ToString();

            var formats = Enum.GetValues(typeof(TerminalState.ScreenFormat))
                              .Cast<TerminalState.ScreenFormat>()
                              .Select(f => new { Format = f, Name = EnumHelper.GetDescription(f) })
                              .ToList();
            ScreenFormatCombo.ItemsSource = formats;
            ScreenFormatCombo.DisplayMemberPath = "Name";
            ScreenFormatCombo.SelectedValuePath = "Format";
            ScreenFormatCombo.SelectedValue = state.screenFormat;

            LogHelper.OnLogCountChanged = (count, level) =>
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        LogLevelIndicator.Text = $"Loggnivå: {level} | {count} loggar";
                    });
                }
                catch (Exception ex)
                {
                    this.LogDebug($"[OnLogCountChanged] Kunde inte sätta indikator pga. {ex}");
                }
            };
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Terminal.SetDisplayTheme(_uiConfig.DisplayTheme);
            }), DispatcherPriority.ApplicationIdle);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.LogDebug($"[WINDOW_LOADED] Jobbar med TerminalState hashcode {state.GetHashCode()}");
            Terminal.FocusInput();
            Terminal.StatusText.Text = _transport.IsConnected ? "🟢 Ansluten" : "🔴 Frånkopplad";

            _inputController.SendBytes += async bytes =>
            {
                await _telnetSessionBridge.SendFromClient(bytes);
            };

            // Auto-connect vid start
            localizationProvider = new LocalizationProvider();
            modeManager = new ModeManager(localizationProvider);
            Terminal.Loaded += async (_, __) =>
            {
                await ConnectAsync(cfg.Host, cfg.Port);
                await Terminal.InitializeSession(session, _uiConfig);
            };

            LogTerminalHealth();
            // Event från DCS-hanteraren
            // Efter att parser och session är skapade i Connect() eller direkt efter Connect() i Window_Loaded:
            if (dcsHandler == null) dcsHandler = new DcsSequenceHandler(state, System.IO.Path.Combine(basePath, "Data", "DcsBitGroups.json"));

            // Koppla events
            dcsHandler.OnStatusUpdate += msg =>
                Dispatcher.Invoke(() => Terminal.UpdateStatus(msg, Brushes.Black));

            // Event från transporten
            _transport.OnStatusUpdate += msg =>
                Dispatcher.Invoke(() => Terminal.UpdateStatus(msg, Brushes.Black));

            this.LogTrace($"[MainWindow] Kopplar in OnDataReceived");

            _transport.Disconnected += () =>
            {
                Terminal.DetachFromBuffer();
                Dispatcher.Invoke(UpdateButtonStates);
            };
            _transport.Reconnected += () =>
                Dispatcher.Invoke(() =>
                {
                    Terminal.UpdateStatus("🟢 Återansluten", Brushes.Green);
                    UpdateButtonStates();
                });

            Terminal.StatusText.Text = _transport.IsConnected ? "🟢 Ansluten" : "🔴 Frånkopplad";
            ScreenBuffer.RowLocks.LogLockedRows(Logger);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { }
        private void Window_KeyDown(object sender, KeyEventArgs e) { }
        private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e) { }
        private void Window_TextInput(object sender, TextCompositionEventArgs e) { }

        private void ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            // Växla konsolfönster av/på
            const int SW_HIDE = 0;
            const int SW_SHOW = 5;

            var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            var consoleHandle = NativeMethods.GetConsoleWindow();

            if (NativeMethods.IsWindowVisible(consoleHandle))
                NativeMethods.ShowWindow(consoleHandle, SW_HIDE);
            else
                NativeMethods.ShowWindow(consoleHandle, SW_SHOW);
            Terminal.FocusInput();
        }

        private void SetWhiteDisplay(object sender, RoutedEventArgs e)
            => Terminal.SetDisplayTheme(TerminalState.DisplayType.White);

        private void SetGreenDisplay(object sender, RoutedEventArgs e)
            => Terminal.SetDisplayTheme(TerminalState.DisplayType.Green);

        private void SetBlueDisplay(object sender, RoutedEventArgs e)
            => Terminal.SetDisplayTheme(TerminalState.DisplayType.Blue);

        private void SetAmberDisplay(object sender, RoutedEventArgs e)
            => Terminal.SetDisplayTheme(TerminalState.DisplayType.Amber);

        private void SetFullColorDisplay(object sender, RoutedEventArgs e)
            => Terminal.SetDisplayTheme(TerminalState.DisplayType.FullColor);

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            host = HostTextBox.Text.Trim();
            if (!int.TryParse(PortTextBox.Text, out port))
            {
                this.LogWarning($"Ogiltigt portnummer: {PortTextBox.Text}");
                return;
            }

            // Om vi redan är anslutna – koppla ur först
            if (_isConnected)
            {
                await _transport.DisconnectAsync();
                Terminal.DetachFromBuffer(); // ny metod i TerminalControl som gör -= OnBufferUpdated
            }

            var cfg = new TransportConfig { Host = host, Port = port };
            _configService.SaveTransportConfig(cfg);
            _configService.SaveUiConfig(_uiConfig);
            await ConnectAsync(host, port);
            Terminal.FocusInput();
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _transport.DisconnectAsync();
                TerminalReady = false;
                Terminal.UpdateStatus("Frånkopplad", Brushes.Black);
            }
            catch (Exception ex)
            {
                this.LogError($"Fel vid frånkoppling. Exception {ex}");
            }
            Dispatcher.Invoke(UpdateButtonStates);
            _isConnected = false;
            Terminal.FocusInput();
        }

        private async Task ConnectAsync(string host, int port)
        {
            this.LogDebug($"[MainWindow] Connect() called – host: {host}, port: {port}");
            try
            {
                TerminalReady = false;

                _oldBridge?.Dispose();
                if (_transport != null)
                    await _transport.DisposeAsync();
                _transport = new TcpClientTransport();

                // 1) Skapa tolk + input först
                _telnetInterpreter = new TelnetInterpreter();
                _inputController = new InputController(_inputMapper, b => _telnetInterpreter.SendToServer(b));

                // 2) UI kopplas nu när rätt controller finns
                this.LogDebug($"[CONNECTASYNC] Jobbar med TerminalState hashcode {state.GetHashCode()}");
                Terminal.AttachToBuffer(state.ScreenBuffer);
                Terminal.InputController = _inputController;

                // 3) Parser + session
                _parser = new TerminalParser(dataPathProvider, state, _inputController, modeManager, Terminal);
                session = new TerminalSession(_inputController, _inputMapper, basePath, state, _parser);
                await Terminal.InitializeSession(session, _uiConfig);
                csiHandler = _parser._csiHandler;
                dcsHandler = _parser._dcsHandler;

                // 4) Priming render och sanity-check
                var count = state.ScreenBuffer.GetBufferUpdatedHandlerCount();
                if (count == 0) this.LogWarning("UI är inte kopplat till bufferten!");
                Terminal.RenderFromBuffer(state.ScreenBuffer);

                this.LogTrace($"[CONNECT] BufferUpdated handler count: {state.ScreenBuffer.GetBufferUpdatedHandlerCount()}");
                this.LogTrace($"[CONNECT] ScreenBuffer Hashcode: {state.ScreenBuffer.GetHashCode()}");

                // 5) Bridge
                _telnetSessionBridge = new TelnetSessionBridge(
                    _telnetInterpreter,
                    _parser,
                    b => _transport.SendAsync(b, CancellationToken.None, host, port),
                    msg => this.LogTrace(msg)
                );
                _oldBridge = _telnetSessionBridge;

                // 6) Viktigt: koppla nya InputController → Bridge (efter att bridgen skapats)
                _inputController.SendBytes = null; // säkerställ inga gamla handlers hänger kvar
                _inputController.SendBytes += async bytes => await _telnetSessionBridge.SendFromClient(bytes);

                // 7) Data in: clamp:a idle så vi aldrig väntar sekunder
                _transport.OnDataReceived += (buffer, length) =>
                {
                    this.LogTrace($"[ONDATARECEIVED] Inkommande data {Encoding.ASCII.GetString(buffer)} längd {length}");
                    var sb = state?.ScreenBuffer;
                    if (sb == null || length <= 0) return;

                    var idle = ComputeIdleWindow(length);
                    // Clamp för interaktivitet (t.ex. 8–50 ms)
                    var min = TimeSpan.FromMilliseconds(8);
                    var max = TimeSpan.FromMilliseconds(50);
                    if (idle < min) idle = min;
                    if (idle > max) idle = max;

                    this.LogTrace($"[BURST] length={length}, idle={idle.TotalMilliseconds}ms");

                    if (!_burstActive)
                    {
                        _burstActive = true;
                        _burstScope = sb.BeginUpdate();
                        _burstBytes = 0;
                        _burstChunks = 0;
                    }

                    _telnetSessionBridge.Feed(buffer, length);
                    _burstBytes += length;
                    _burstChunks++;

                    _burstCts?.Cancel();
                    _burstCts = new CancellationTokenSource();
                    var token = _burstCts.Token;

                    Task.Delay(idle, token).ContinueWith(_ =>
                    {
                        if (token.IsCancellationRequested) return;
                        try { Interlocked.Exchange(ref _burstScope, null)?.Dispose(); } // commit
                        finally
                        {
                            _burstActive = false;
                            _burstBytes = 0;
                            _burstChunks = 0;
                        }
                    }, TaskScheduler.Default);
                };

                // 8) Anslut sist
                await _transport.ConnectAsync(host, port, CancellationToken.None);
                await _telnetSessionBridge.SendFromClient(new byte[] { 0x0D, 0x0A }); // CRLF
                TerminalReady = true;
            }
            catch (Exception ex)
            {
                TerminalReady = false;
                Terminal.UpdateStatus("🔴 Frånkopplad", Brushes.Red);
                this.LogError($"Kunde inte ansluta: {ex}");
            }

            _isConnected = TerminalReady;
            Dispatcher.Invoke(UpdateButtonStates);
        }

        private void UpdateButtonStates()
        {
            ConnectButton.IsEnabled = !TerminalReady;
            DisconnectButton.IsEnabled = TerminalReady;
        }

        private void LogLevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_uiConfig == null || LogLevelCombo.SelectedItem == null) return;

            _uiConfig.DefaultLogLevel = (LogLevel)LogLevelCombo.SelectedItem;
            LoggerFactoryProvider.SetMinimumLevel(_uiConfig.DefaultLogLevel);
            _configService.SaveUiConfig(_uiConfig);
            Terminal.FocusInput();
        }

        private void ScreenFormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScreenFormatCombo.SelectedValue is TerminalState.ScreenFormat format)
            {
                state.screenFormat = format;
                state.SetScreenFormat(); // uppdaterar ScreenBuffer, Cols, Rows

                Terminal.ApplyScreenFormatChange(state);

                MainWindowControl.Width = state.Cols * 8 + 40;
                MainWindowControl.Height = state.Rows * 18 + 60;
            }
            Terminal.FocusInput();
        }

        protected override void OnClosed(EventArgs e)
        {
            this.LogInformation("Programmet avslutas normalt.");
            base.OnClosed(e);
            _configService.SaveTransportConfig(cfg);
            _configService.SaveUiConfig(_uiConfig);
            FreeConsole(); // stäng konsolfönstret
        }

        public static byte[] StripTelnetIac(byte[] data)
        {
            var list = new List<byte>(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b == 0xFF) // IAC
                {
                    if (i + 1 >= data.Length) break;
                    byte cmd = data[++i]; // WILL/WONT/DO/DONT/SB/SE/etc.

                    if (cmd == 0xFF)
                    {
                        // Escapad 0xFF -> data-0xFF, behåll en
                        list.Add(0xFF);
                    }
                    else if (cmd == 0xFA) // SB ... SE
                    {
                        // Subnegotiation:  IAC SB <opt> ... IAC SE
                        // Skippa till IAC SE
                        while (i + 1 < data.Length && !(data[i] == 0xFF && data[i + 1] == 0xF0))
                            i++;
                        i++; // hoppa över SE
                    }
                    else
                    {
                        // WILL(0xFB) WONT(0xFC) DO(0xFD) DONT(0xFE) <option>
                        if (i + 1 < data.Length) i++; // hoppa över <option>
                    }
                }
                else
                {
                    list.Add(b);
                }
            }
            return list.ToArray();
        }

        internal static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            internal static extern IntPtr GetConsoleWindow();

            [DllImport("user32.dll")]
            internal static extern bool IsWindowVisible(IntPtr hWnd);

            [DllImport("user32.dll")]
            internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        }

        private void InitializeTerminal()
        {
            // Skapa och konfigurera TerminalState
            //state = new TerminalState(); // eller hämta från config
            state.SetScreenFormat();     // initiera ScreenBuffer, Cols, Rows

            // Koppla in TerminalState till TerminalControl
            Terminal.SetTerminalState(state);
            Terminal._session = session; // eller via en setter
            Terminal._uiConfig = _uiConfig;
            // Justera fönsterstorlek
            MainWindowControl.Width = state.Cols * 8 + 40;
            MainWindowControl.Height = state.Rows * 18 + 60;
        }

        private void LogTerminalHealth()
        {
            var uiBuffer = Terminal._attachedBuffer; // eller motsvarande property i din TerminalControl
            var parserBuffer = state.ScreenBuffer;

            var uiHash = uiBuffer?.GetHashCode() ?? 0;
            var parserHash = parserBuffer?.GetHashCode() ?? 0;
            var handlerCount = parserBuffer?.GetBufferUpdatedHandlerCount() ?? 0;

            bool match = uiBuffer != null && parserBuffer != null && ReferenceEquals(uiBuffer, parserBuffer);

            // Grundlogg
            this.LogDebug($"[HEALTHCHECK] UI buffer hash: {uiHash}, Parser buffer hash: {parserHash}, Match: {match}, BufferUpdated handlers: {handlerCount}");

            // Automatisk varning
            if (!match || handlerCount == 0)
            {
                this.LogError($"[HEALTHCHECK] ⚠ FELKOPPLING – UI och parser delar {(match ? "samma" : "olika")} buffer, handlers: {handlerCount}");
            }
        }


        private void NoteUserSend()
        {
            _lastUserSendUtc = DateTime.UtcNow;
        }

        // Heuristik för aktivt idle baserat på kontext
        private TimeSpan ComputeIdleWindow(int chunkLen)
        {
            var sinceSend = DateTime.UtcNow - _lastUserSendUtc;

            // Större chunkar tyder på server-svar → använd längre fönster
            if (chunkLen >= 128) return LargeIdle;

            // Precis efter användarsändning (CR/LF) → använd lite längre för att hinna samla svaret
            if (sinceSend < TimeSpan.FromMilliseconds(250)) return LargeIdle;

            // Små chunkar under skrivning → kort fönster
            return SmallIdle;
        }

        private void LoadUiConfig()
        {
            this.LogDebug($"[LOADUICONFIG] Jobbar med TerminalState hashcode {state.GetHashCode()}");
            // Ladda UI-konfig
            _uiConfig = _configService.LoadUiConfig();

            // 1. Loggnivå
            LoggerFactoryProvider.SetMinimumLevel(_uiConfig.DefaultLogLevel);
            LogLevelCombo.SelectedItem = _uiConfig.DefaultLogLevel;

            // 2. Skärmformat
            state.screenFormat = _uiConfig.ScreenFormat;
            Terminal.AdjustTerminalWidth(state.ScreenBuffer);
            Terminal.AdjustTerminalHeight(state.ScreenBuffer);

            // 3. Färgtema
            state.Display = _uiConfig.DisplayTheme;
        }
    }

    public class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public StaticOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;

        public T Get(string name) => _value;

        public IDisposable OnChange(Action<T, string> listener) => null!;
    }
}