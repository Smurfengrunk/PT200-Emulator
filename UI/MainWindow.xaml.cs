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
using System.Windows.Threading;
using static PT200Emulator.Core.Config.TransportConfig;
using static PT200Emulator.UI.TerminalControl;

namespace PT200Emulator.UI
{
    public partial class MainWindow : Window
    {
        internal ILoggerFactory _loggerFactory;
        internal ILogger _logger;
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
        private string basePath;
        private readonly TimeSpan _burstIdle = TimeSpan.FromMilliseconds(8);
        
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

            _loggerFactory = loggerFactory ?? LoggerFactoryProvider.Instance;
            _logger = _loggerFactory.CreateLogger("MainWindow");
            LoggerFactoryProvider.SetMinimumLevel(LogLevel.Trace);
            LogLevelCombo.ItemsSource = Enum.GetValues(typeof(LogLevel));
            LogLevelCombo.SelectedItem = LoggerFactoryProvider.GetMinimumLevel();
            LogLevelCombo.SelectionChanged += (s, e) =>
            {
                if (LogLevelCombo.SelectedItem is LogLevel level)
                    LoggerFactoryProvider.SetMinimumLevel(level);
            };

            basePath = AppDomain.CurrentDomain.BaseDirectory;
            dataPathProvider = new DataPathProvider(basePath);

            state = new TerminalState();
            InitializeTerminal();

            _transport = transport ?? new TcpClientTransport();
            _renderer = renderer ?? new DummyImplementations.Renderer();
            _inputMapper = inputMapper ?? new PT200Emulator.Core.Input.InputMapper();

            _telnetInterpreter = new TelnetInterpreter();
            _inputController = new InputController(_inputMapper, bytes => _telnetInterpreter.SendToServer(bytes));
            //_parser = parser ?? new TerminalParser(dataPathProvider, state, _inputController);

            // Koppla UI till parserns aktuella buffer
            Terminal.AttachToBuffer(state.ScreenBuffer);
            Terminal.InputController = _inputController;
            Terminal.InputMapper = _inputMapper;

            this.LogTrace($"[INIT] BufferUpdated handler count: {state.ScreenBuffer.GetBufferUpdatedHandlerCount()}");
            this.LogTrace($"[MAINWINDOW] ScreenBuffer Hashcode: {state.ScreenBuffer.GetHashCode()}");
            this.LogTrace($"[MAINWINDOW] TerminalControl Hashcode: {Terminal.GetHashCode()}");

            _txChannel = Channel.CreateUnbounded<byte[]>();
            _rxChannel = Channel.CreateUnbounded<byte[]>();

            _configService = new ConfigService();
            var cfg = _configService.LoadTransportConfig();
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
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Terminal.FocusInput();

            Terminal.StatusText.Text = _transport.IsConnected ? "🟢 Ansluten" : "🔴 Frånkopplad";

            _inputController.SendBytes += async bytes =>
            {
                await _telnetSessionBridge.SendFromClient(bytes);
            };

            // Auto-connect vid start
            var cfg = _configService.LoadTransportConfig();
            Connect(cfg.Host, cfg.Port);

            // Initiera terminalen
            await Terminal.InitializeSession(session);

            LogTerminalHealth();
            // Event från DCS-hanteraren
            // Efter att parser och session är skapade i Connect() eller direkt efter Connect() i Window_Loaded:
            dcsHandler = new DcsSequenceHandler(state, Path.Combine(basePath, "Data", "DcsBitGroups.json"));

            // Koppla events
            dcsHandler.OnStatusUpdate += msg =>
                Dispatcher.Invoke(() => Terminal.UpdateStatus(msg, Brushes.Black));

            // Event från transporten
            _transport.OnStatusUpdate += msg =>
                Dispatcher.Invoke(() => Terminal.UpdateStatus(msg, Brushes.Black));

            this.LogTrace($"[MainWindow] Kopplar in OnDataReceived");

            _transport.OnDataReceived += (buffer, length) =>
            {
                // Hämta konkreta bufferten via bridge/parser
                var screenBuffer = _telnetSessionBridge?.Parser?.screenBuffer; // se not nedan om typer

                if (screenBuffer == null)
                    return;

                // Starta burst vid första chunk
                if (!_burstActive)
                {
                    _burstActive = true;
                    _burstScope = screenBuffer.BeginUpdate(); // INTE EndUpdate; vi sparar scopet och Disposar senare
                }

                _telnetSessionBridge.Feed(buffer, length);

                // Restart burst-idle-timer
                _burstCts?.Cancel();
                _burstCts = new CancellationTokenSource();
                var token = _burstCts.Token;

                Task.Delay(_burstIdle, token).ContinueWith(_ =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        // Stäng bursten genom att Dispos:a scopet
                        _burstScope?.Dispose();
                        _burstScope = null;
                        _burstActive = false;
                    }
                }, TaskScheduler.Default);
            };

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
            cfg.Save();

            Connect(host, port);
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



        private async void Connect(string host, int port)
        {
            this.LogDebug($"[MainWindow] Connect() called – host: {host}, port: {port}");

            try
            {
                TerminalReady = false;

                // 1. Stäng ner gamla resurser
                _oldBridge?.Dispose();
                _transport?.DisposeAsync();
                _transport = new TcpClientTransport();

                // 2. Skapa ny state, parser och interpreter för varje session
                //state = new TerminalState();
                Terminal.SetTerminalState(state);

                _telnetInterpreter = new TelnetInterpreter();
                _inputController = new InputController(_inputMapper, bytes => _telnetInterpreter.SendToServer(bytes));
                _parser = new TerminalParser(dataPathProvider, state, _inputController);

                // 3. Koppla UI till parserns aktuella buffer
                Terminal.AttachToBuffer(state.ScreenBuffer);
                Terminal.InputController = _inputController;

                session = new TerminalSession(_inputController, _inputMapper, basePath, state, _parser);
                await Terminal.InitializeSession(session);

                this.LogTrace($"[CONNECT] BufferUpdated handler count: {state.ScreenBuffer.GetBufferUpdatedHandlerCount()}");
                this.LogTrace($"[CONNECT] ScreenBuffer Hashcode: {state.ScreenBuffer.GetHashCode()}");

                // 4. Skapa bridge och koppla events innan connect
                _telnetSessionBridge = new TelnetSessionBridge(
                    _telnetInterpreter,
                    _parser,
                    b => _transport.SendAsync(b, CancellationToken.None, host, port),
                    msg => this.LogTrace(msg)
                );
                _oldBridge = _telnetSessionBridge;

                // Koppla transportens OnDataReceived till bridgen
                _transport.OnDataReceived += (buffer, length) =>
                {
                    var sb = _telnetSessionBridge?.Parser?.screenBuffer;
                    if (sb == null || length <= 0) return;

                    var idle = ComputeIdleWindow(length);

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

                    // restart timer
                    _burstCts?.Cancel();
                    _burstCts = new CancellationTokenSource();
                    var token = _burstCts.Token;

                    Task.Delay(idle, token).ContinueWith(_ =>
                    {
                        if (token.IsCancellationRequested) return;

                        // Säkerhetsventil: om vi ändå fortsätter få små drip-chunkar, sätt ett hårt max-tak
                        // Här kan du hålla reda på när burst startade och om elapsed > MaxIdle → commit ändå.
                        // (enkel variant: kör commit direkt här)
                        try
                        {
                            _burstScope?.Dispose();
                        }
                        finally
                        {
                            _burstScope = null;
                            _burstActive = false;
                            _burstBytes = 0;
                            _burstChunks = 0;
                        }
                    }, TaskScheduler.Default);
                };

                // 5. Anslut transporten sist
                await _transport.ConnectAsync(host, port, CancellationToken.None);

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
            if (LogLevelCombo.SelectedItem is ComboBoxItem cbi &&
                Enum.TryParse<LogLevel>(cbi.Content?.ToString(), out var level))
            {
                LoggerFactoryProvider.SetMinimumLevel(level);
            }
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
        }

        protected override void OnClosed(EventArgs e)
        {
            this.LogInformation("Programmet avslutas normalt.");
            base.OnClosed(e);
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

            // Initiera layout och rendering
            Terminal.InitDocument(state.ScreenBuffer);
            Terminal.AdjustTerminalWidth(state.ScreenBuffer);
            Terminal.AdjustTerminalHeight(state.ScreenBuffer);
            Terminal.RenderFromBuffer(state.ScreenBuffer);

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