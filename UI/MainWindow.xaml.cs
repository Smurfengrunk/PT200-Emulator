using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Options;
using PT200Emulator.Core.Config;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.Core.Rendering;
using PT200Emulator.DummyImplementations;
using PT200Emulator.Infrastructure.Logging;
using PT200Emulator.Infrastructure.Networking;
using System;
using System.Linq.Expressions;
using System.Net;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using static PT200Emulator.Core.Config.TransportConfig;
using static PT200Emulator.UI.TerminalControl;
using System.Text;
using PT200Emulator.Protocol;

namespace PT200Emulator.UI
{
    public partial class MainWindow : Window
    {
        internal ILoggerFactory _loggerFactory;
        internal ILogger _logger;
        private readonly ITransport _transport;
        private readonly ITerminalParser _parser;
        private readonly IScreenBuffer _screenBuffer;
        private readonly IRenderer _renderer;
        private readonly IInputMapper _inputMapper;
        private readonly InputController _inputController;
        private readonly Channel<byte[]> _txChannel;
        private readonly Channel<byte[]> _rxChannel;
        private readonly ConfigService _configService;
        private readonly TelnetInterpreter _telnetInterpreter;
        private TelnetSessionBridge _telnetSessionBridge;
        private string host = "localhost";
        private int port = 2323;
        private TerminalSession session;
        private DcsSequenceHandler dcsHandler;
        private DataPathProvider dataPathProvider;
        private TerminalState state;
        private string basePath;

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
            if (loggerFactory == null) loggerFactory = LoggerFactoryProvider.Instance;

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger("MainWindow");
            basePath = AppDomain.CurrentDomain.BaseDirectory;
            dataPathProvider = new DataPathProvider(basePath);
            state = new TerminalState();

            _transport = transport ?? new TcpClientTransport();
            _screenBuffer = state.ScreenBuffer;
            _renderer = renderer ?? new DummyImplementations.Renderer();
            _inputMapper = inputMapper ?? new PT200Emulator.Core.Input.InputMapper();
            _inputController = new InputController(_inputMapper, bytes => _telnetInterpreter.SendToServer(bytes));
            _telnetInterpreter = new();
            _parser = parser ?? new TerminalParser(dataPathProvider, state, _inputController);
            this.LogDebug($"[MainWindow] Telnet interpreter hash: {_telnetInterpreter.GetHashCode()}");
            Terminal.InputController = _inputController;

            _txChannel = Channel.CreateUnbounded<byte[]>();
            _rxChannel = Channel.CreateUnbounded<byte[]>();


            Terminal.InputMapper = _inputMapper;
            _configService = new ConfigService();

            // Ladda config och fyll i fälten
            var cfg = _configService.LoadTransportConfig();
            HostTextBox.Text = cfg.Host;
            PortTextBox.Text = cfg.Port.ToString();
            LoggerFactoryProvider.SetMinimumLevel(LogLevel.Debug);
            // Loggnivåer
            LogLevelCombo.ItemsSource = Enum.GetValues(typeof(LogLevel));
            LogLevelCombo.SelectedItem = LoggerFactoryProvider.GetMinimumLevel();
            LogLevelCombo.SelectionChanged += (s, e) =>
            {
                if (LogLevelCombo.SelectedItem is LogLevel level)
                    LoggerFactoryProvider.SetMinimumLevel(level);
            };

            // Skärmformat
            var formats = Enum.GetValues(typeof(TerminalState.ScreenFormat))
                              .Cast<TerminalState.ScreenFormat>()
                              .Select(f => new { Format = f, Name = EnumHelper.GetDescription(f) })
                              .ToList();

            ScreenFormatCombo.ItemsSource = formats;
            ScreenFormatCombo.DisplayMemberPath = "Name";
            ScreenFormatCombo.SelectedValuePath = "Format";
            ScreenFormatCombo.SelectedValue = state.screenFormat;

            // Loggindikator
            LogHelper.OnLogCountChanged = (count, level) =>
            {
                Dispatcher.Invoke(() =>
                {
                    LogLevelIndicator.Text = $"Loggnivå: {level} | {count} loggar";
                });
            };

        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.LogDebug("MainWindow loaded");

            Terminal.FocusInput();

            // Initiera session och DCS-hanterare
            session = TerminalSessionFactory.Create(_inputController, _inputMapper, basePath, state, _parser);
            dcsHandler = new DcsSequenceHandler(state, Path.Combine(basePath, "Data", "DcsBitGroups.json"));
            
            _inputController.SendBytes += async bytes =>
            {
                await _telnetSessionBridge.SendFromClient(bytes);
            };

            // Event från DCS-hanteraren
            dcsHandler.OnStatusUpdate += msg =>
                Dispatcher.Invoke(() => Terminal.UpdateStatus(msg, Brushes.Black));

            // Event från transporten
            _transport.OnStatusUpdate += msg =>
                Dispatcher.Invoke(() => Terminal.UpdateStatus(msg, Brushes.Black));

            _transport.OnDataReceived += (buffer, length) =>
            {
                //this.LogDebug($"Transport mottog: {BitConverter.ToString(buffer)}");
                _telnetSessionBridge.Feed(buffer, length);
            };

            _transport.Disconnected += () =>
                Dispatcher.Invoke(UpdateButtonStates);

            _transport.Reconnected += () =>
                Dispatcher.Invoke(() =>
                {
                    Terminal.UpdateStatus("🟢 Återansluten", Brushes.Green);
                    UpdateButtonStates();
                });

            // Initiera terminalen
            await Terminal.InitializeSession(session);

            session.ScreenBuffer.BufferUpdated += () =>
                Dispatcher.Invoke(() => Terminal.RenderFromBuffer(session.ScreenBuffer));

            //this.LogDebug($"Focused element: {Keyboard.FocusedElement}");

            // Auto-connect vid start
            var cfg = _configService.LoadTransportConfig();
            Connect(cfg.Host, cfg.Port);

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

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            this.LogDebug("Connect-knappen klickad");

            host = HostTextBox.Text.Trim();
            if (!int.TryParse(PortTextBox.Text, out port))
            {
                this.LogWarning($"Ogiltigt portnummer: {PortTextBox.Text}");
                return;
            }

            // Spara värdena
            var cfg = new TransportConfig { Host = host, Port = port };
            cfg.Save();
            Connect(host, port);

            Terminal.FocusInput(); // anropa detta efter ConnectAsync
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            this.LogDebug("Disconnect-knappen klickad");
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
            if (_isConnected) return;

            try
            {
                await _transport.ConnectAsync(host, port, CancellationToken.None);
                TerminalReady = true;

                _telnetSessionBridge = new TelnetSessionBridge(
                    _telnetInterpreter,
                    bytes => _transport.SendAsync(bytes, CancellationToken.None).Wait(),
                    msg => this.LogDebug(msg)
                );

                _telnetSessionBridge.HookEvents(_telnetInterpreter, _parser);
            }
            catch (Exception ex)
            {
                TerminalReady = false;
                Terminal.UpdateStatus("🔴 Frånkopplad", Brushes.Red);
                this.LogError($"Kunde inte ansluta: {ex}");
            }

            _isConnected = true;
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
                var (cols, rows) = state.GetDimensions();
                state.ScreenBuffer = new ScreenBuffer(rows, cols);

                MainWindowControl.Width = cols * 8 + 40;
                MainWindowControl.Height = rows * 18 + 60;

                Terminal?.InvalidateVisual();
                Terminal?.UpdateLayout();
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
