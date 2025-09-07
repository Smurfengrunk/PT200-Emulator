using PT200Emulator.Core;
using PT200Emulator.IO;
using PT200Emulator.Models;
using PT200Emulator.Parser;
using PT200Emulator.Util;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using static PT200Emulator.Core.PT200State;
using static PT200Emulator.Util.Logger;
using static ScreenBuffer;
using static System.Net.Mime.MediaTypeNames;

namespace PT200Emulator.UI
{

    public partial class MainWindow : Window
    {
        private PT200State state;
        private DispatcherTimer clockTimer;
        private DispatcherTimer cursorTimer;
        private DispatcherTimer idleTimer;
        private ScreenBuffer screenBuffer;
        private EscapeSequenceParser parser;
        private ITerminalClient tcpClient; // instansen vi använder i eventhandlers
        static double fontSize = 14;
        static double lineHeight = fontSize + 2;
        private readonly Queue<string> statusHistory = new Queue<string>();
        private const int MaxStatusHistory = 50;
        private bool _terminalReady = false;
        private static EmacsLayoutModel EmacsLayout;
        private readonly ControlCharacterHandler controlHandler = new();
        internal AttributeTracker attr = new AttributeTracker();
        private ThemeManager themeManager;
        private TerminalSessionManager session;
        private System.Net.IPAddress host = System.Net.IPAddress.Loopback; // localhost
        private int port = 2323; // Justera efter din WSL-setup
        private string MainWindow_Statustext;
        private Brush MainWindow_StatustextColor = Brushes.OrangeRed;
        private System.Windows.Shapes.Path cursorPath = new System.Windows.Shapes.Path();
        private DispatcherTimer threadPulseTimer;
        private readonly InputTracer _inputTracer;
        private readonly ObservableCollection<string> _traceLog = new();
        private readonly TerminalRenderer renderer = new();
        private readonly KeyboardDecoder keyboardDecoder = new KeyboardDecoder();
        private DateTime lastRenderTime = DateTime.MinValue;

        private bool terminalReady
        {
            get => _terminalReady;
            set
            {
                _terminalReady = value;
                Logger.Log($"terminalReady satt till: {value}", LogLevel.Info);
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            ConsoleManager.Open();
            _inputTracer = new InputTracer();
            InputTraceList.ItemsSource = _inputTracer.TraceLog;
            attr = new AttributeTracker();
            themeManager = new ThemeManager(attr, TerminalCanvas, StatusText, ClockTextBlock);
            StartCursorBlink();
            EmacsLayout = new EmacsLayoutModel();
            Logger.CurrentLevel = Logger.LogLevel.Debug; // Behövs just nu för att få all information i loggen

            // Skapa state först
            state = new PT200State
            {
                screenFormat = PT200State.ScreenFormat.S80x24,
                IsColor = true,
                CursorBlink = true,
                StatusBarVisible = true,
                PrintMode = false
            };
            state.cursorVisible = true;

            // Beräkna storlek baserat på state.Format
            int cols = state.screenFormat switch
            {
                PT200State.ScreenFormat.S80x24 => 80,
                PT200State.ScreenFormat.S80x48 => 80,
                PT200State.ScreenFormat.S132x27 => 132,
                PT200State.ScreenFormat.S160x24 => 160,
                _ => 80
            };

            int rows = state.screenFormat switch
            {
                PT200State.ScreenFormat.S80x48 => 48,
                PT200State.ScreenFormat.S132x27 => 27,
                _ => 24
            };
            screenBuffer = new ScreenBuffer(cols, rows, attr);
            Logger.Log($"MainWindow instans skapad – terminalReady initialt: {terminalReady}", LogLevel.Info);
            Logger.Log($"MainWindow körs på instans – Hash: {this.GetHashCode()}", LogLevel.Info);

            // Initiera klockan
            clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            clockTimer.Tick += (s, e) =>
            {
                ClockTextBlock.Text = DateTime.Now.ToString("HH:mm ");
            };
            clockTimer.Start();

            MainWindow_StatustextColor = terminalReady ? Brushes.Black : Brushes.Red;
            MainWindow_Statustext = terminalReady ? "✅ Terminalen är redo" : "⏳ Terminalen laddar...";

            LogProfileComboBox.SelectedIndex = 1; // t.ex. "Normal"
            ActiveProfileLabel.Text = "Aktiv loggprofil: Normal";
            Logger.SetProfile(Logger.LogProfile.Normal);

            // RunParserSelfTest();
        }

        public void Trace(string msg)
        {
            Logger.Log(msg, Logger.LogLevel.Debug);
            _traceLog.Add(msg);
            if (_traceLog.Count > 100)
                _traceLog.RemoveAt(0); // håll loggen lätt
        }

        private async void SendTestA_Click(object sender, RoutedEventArgs e)
        {
            if (terminalReady && tcpClient != null)
            {
                Logger.Log("Försöker skicka 'A'...", LogLevel.Debug);

                try
                {
                    var sendTask = tcpClient.SendAsync("A");
                    if (await Task.WhenAny(sendTask, Task.Delay(1000)) == sendTask)
                    {
                        Logger.Log("Skickade 'A' inom timeout", LogLevel.Info);
                    }
                    else
                    {
                        Logger.Log("⚠️ SendAsync timeout – ingen respons", LogLevel.Warning);
                        tcpClient = null;
                        terminalReady = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                    tcpClient = null;
                    terminalReady = false;
                }
            }
            else
            {
                Logger.Log("❌ Testknapp kunde inte skicka – terminalen är inte redo", LogLevel.Warning);
            }
        }

        private void ApplyDisplayTheme(DisplayType type)
        {
            themeManager.Apply(type);
            UpdateSourceTracker.Set("ApplyDisplayTheme");
            if (DateTime.Now - lastRenderTime > TimeSpan.FromMilliseconds(50))
            {
                UpdateTerminalDisplay();
                lastRenderTime = DateTime.Now;
            }
            Keyboard.Focus(this);
        }

        private void SetWhiteDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.White);
        private void SetBlueDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.Blue);
        private void SetGreenDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.Green);
        private void SetAmberDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.Amber);
        private void SetFullColorDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.FullColor);

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSourceTracker.Set("Startup");
            if (DateTime.Now - lastRenderTime > TimeSpan.FromMilliseconds(50))
            {
                UpdateTerminalDisplay();
                lastRenderTime = DateTime.Now;
            }
            UpdateSourceTracker.Set("Idle");
            try
            {
                Logger.Log("🚪 Window_Loaded startar", LogLevel.Info);
                idleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30)
                };
                idleTimer.Tick += (s, e) =>
                {
                    Logger.Log("🕒 30 sekunder utan input – sessionen kan ha stängt", LogLevel.Warning);
                };
                idleTimer.Start();
                DispatcherTimer keepAlive = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(20)
                };

                keepAlive.Tick += async (s, e) =>
                {
                    if (terminalReady && tcpClient != null)
                    {
                        try
                        {
                            await tcpClient.SendAsync(""); // eller "\0" om värddatorn accepterar det
                            Logger.Log("🔄 Keep-alive skickad", Logger.LogLevel.Debug);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"💥 Keep-alive misslyckades: {ex.Message}", Logger.LogLevel.Warning);
                            tcpClient = null;
                            terminalReady = false;
                        }
                    }
                };

                keepAlive.Start();

                Logger.Log($"Fönstret har fokus: {this.IsFocused}", LogLevel.Info);
                ApplyDisplayTheme(DisplayType.Green); // eller valfritt

                UpdateSourceTracker.Set("Window_Loaded (new TerminalSessionManager)");
                session = new TerminalSessionManager(
                    host,
                    port,
                    screenBuffer,
                    ch =>
                    {
                        screenBuffer.AddChar(ch);
                        UpdateTerminalDisplay();
                    },
                    () => UpdateTerminalDisplay()
                );
                Dispatcher.Invoke(() =>
                {
                    StatusText.Foreground = Brushes.LightGreen;
                    StatusText.Text = "Ansluten";
                    Logger.Log($"[Window_Loaded] StatusText-instans: {StatusText.GetHashCode()}", Logger.LogLevel.Info);
                    Logger.Log($"[Window_Loaded] StatusText.Text: \"{StatusText.Text}\"", Logger.LogLevel.Info);
                });

                bool connected = await session.ConnectAsync();
                if (connected)
                {
                    await Task.Run(() => session.StartSessionAsync());

                    await Task.Delay(500); // Vänta 0.5 sek efter StartSessionAsync
                    terminalReady = true;
                    //tcpClient = session.Client;
                    InitializeTerminalSession();
                    Logger.Log($"tcpClient tilldelad – Hash: {tcpClient?.GetHashCode() ?? -1}", LogLevel.Info);
                    parser = session.Parser;
                    parser.BufferUpdated += UpdateTerminalDisplay;
                    controlHandler.RawOutput += async bytes =>
                    {
                        Logger.Log("📡 RawOutput-event kopplat", Logger.LogLevel.Info);
                        if (tcpClient != null)
                        {
                            Logger.Log("📨 RawOutput triggat", Logger.LogLevel.Debug);
                            Logger.LogHex(bytes, bytes.Length, "RAW");
                            try
                            {
                                Logger.Log($"RawOutput triggas – skickar {bytes.Length} byte", LogLevel.Debug);
                                //await tcpClient.SendAsync(bytes);
                                await ((TcpTerminalClient)tcpClient).SendAsync(bytes);
                                Logger.Log("RawOutput-anrop till SendAsync är klar", LogLevel.Debug);
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                                tcpClient = null;
                                terminalReady = false;
                            }
                        }
                        else
                        {
                            Logger.Log("Kan inte skicka data – tcpClient är null", Logger.LogLevel.Warning);
                        }
                    };
                    controlHandler.BreakReceived += () =>
                    {
                        Logger.Log("BREAK mottagen (Ctrl+P)", LogLevel.Info);
                    };

                    terminalReady = true;
                    Logger.Log("✅ terminalReady satt till true", LogLevel.Info);

                    Logger.Log(session.GetSessionStatus(), Logger.LogLevel.Info);
                    RunTerminalSanityReport();
                    ApplyDisplayTheme(DisplayType.Green);
                    UpdateSourceTracker.Set("Window_Loaded (after successful connect)");
                    screenBuffer.WriteChar('X'); // Testa att skriva ett tecken
                    if (DateTime.Now - lastRenderTime > TimeSpan.FromMilliseconds(50))
                    {
                        UpdateTerminalDisplay();
                        lastRenderTime = DateTime.Now;
                    }
                    StatusText.Foreground = MainWindow_StatustextColor;
                    MainWindow_Statustext = "| Ansluten";
                }
                else
                {
                    StatusText.Foreground = Brushes.Red;
                    MainWindow_Statustext = "| Ej ansluten";
                }
                Logger.Log($"[Window_Loaded] StatusText-instans: {StatusText.GetHashCode()}", Logger.LogLevel.Info);
                Logger.Log($"[Window_Loaded] StatusText.Text: \"{StatusText.Text}\"", Logger.LogLevel.Info);
                this.Focus(); // Försök ge fönstret keyboard-fokus
                Keyboard.Focus(this); // Dubbel säkerhet
                Logger.Log("MainWindow har fått fokus", LogLevel.Info);
                UpdateSourceTracker.Set("Window_Loaded (try)");
                if (DateTime.Now - lastRenderTime > TimeSpan.FromMilliseconds(50))
                {
                    UpdateTerminalDisplay();
                    lastRenderTime = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"💥 Window_Loaded kraschade: {ex.Message}", LogLevel.Error);
                StatusText.Foreground = Brushes.Red;
                StatusText.Text = "Fel vid anslutning";
                terminalReady = false;
            }
            Logger.Log($"[Window_Loaded] StatusText-instans: {StatusText.GetHashCode()}", Logger.LogLevel.Info);
            Logger.Log($"[Window_Loaded] StatusText.Text: \"{StatusText.Text}\"", Logger.LogLevel.Info);

        }

        private void InitializeTerminalSession()
        {
            Logger.Log("🔧 Initierar terminalsession manuellt", LogLevel.Info);
            tcpClient = session.Client;
            tcpClient.DataReceived += text =>
            {
                Dispatcher.Invoke(() => UpdateTerminalDisplay());
            };
            Logger.Log($"tcpClient tilldelad – Hash: {tcpClient?.GetHashCode() ?? -1}", LogLevel.Info);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!terminalReady)
            {
                Logger.Log($"⛔ Blockerat tangenttryck: {e.Key} – terminalen är inte redo", LogLevel.Warning);
                e.Handled = true;
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (!terminalReady)
            {
                Logger.Log($"⛔ Ignorerar KeyDown: {e.Key} – terminalen är inte redo", LogLevel.Warning);
                e.Handled = true;
                return;
            }

            var bytes = keyboardDecoder.DecodeKey(e.Key, Keyboard.Modifiers);

            // Filtrera bort vanliga tecken (de hanteras av TextInput)
            if (bytes.Length == 0 || IsPrintableKey(e.Key)) return;

            try
            {
                await session.SendBytes(bytes);
                Logger.Log($"[KeyDown] {keyboardDecoder.Describe(e.Key, Keyboard.Modifiers)} → {BitConverter.ToString(bytes)}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log($"💥 KeyDown Send misslyckades: {ex.Message}", LogLevel.Error);
                terminalReady = false;
            }

            e.Handled = true;
        }

        private async void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
                return;

            char ch = e.Text[0];
            Logger.Log($"[KEY] PreviewTextInput: '{ch}'", LogLevel.Debug);

            try
            {
                await tcpClient.SendAsync(new string(ch, 1)); // Skicka till värddatorn
            }
            catch (Exception ex)
            {
                Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                tcpClient = null;
                terminalReady = false;
            }
            await parser.Feed(ch);                              // Visa lokalt
            e.Handled = true;
        }

        private async void Window_TextInput(object sender, TextCompositionEventArgs e)
        {
            if (!terminalReady)
            {
                Logger.Log($"⛔ Blockerat textinput: \"{e.Text}\" – terminalen är inte redo", LogLevel.Warning);
                return;
            }

            string input = e.Text;

            // Ignorera CR och BS – de hanteras i KeyDown
            if (input.Contains('\r') || input.Contains('\b')) return;

            _inputTracer.TraceTextInput(input);
            var bytes = Encoding.ASCII.GetBytes(input);
            _inputTracer.TraceSend(bytes);

            try
            {
                await session.SendBytes(bytes);
                Logger.Log($"[TextInput] \"{input}\" → {BitConverter.ToString(bytes)}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                terminalReady = false;
            }
        }

        private DateTime lastUpdate = DateTime.MinValue;

        private void UpdateTerminalDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                renderer.Render(TerminalCanvas, screenBuffer, state);
            });
        }

        private void StartCursorBlink()
        {
            /*cursorTimer = new DispatcherTimer();
              cursorTimer.Interval = TimeSpan.FromMilliseconds(500);
              cursorTimer.Tick += (s, e) =>
              {
                  state.cursorVisible = !state.cursorVisible;
                  UpdateSourceTracker.Set("CursorTimer_Tick";
                  if (DateTime.Now - lastRenderTime > TimeSpan.FromMilliseconds(50))
                  {
                    UpdateTerminalDisplay();
                    lastRenderTime = DateTime.Now;
                  }
              };*/
            Logger.Log("🟢 StartCursorBlink körs", Logger.LogLevel.Info);
            cursorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            cursorTimer.Tick += (s, e) =>
            {
                state.cursorVisible = !state.cursorVisible;
                cursorPath.Visibility = state.cursorVisible ? Visibility.Visible : Visibility.Hidden;
            };

            cursorTimer.Start();
        }

        private void ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            ConsoleManager.Toggle();
        }

        private void RunParserSelfTest()
        {
            var payload = new byte[]
            {
                (byte)'$', (byte)'Q',
                80, 24, // cols, rows
                5, 10, 10, 1,
                8, 20, 15, 0
            };
            parser.ParseDcsPayload(payload);

        }

        private async Task SendDCS(string data)
        {
            await parser.Feed('\x1B');
            await parser.Feed('P');
            foreach (var ch in data)
                await parser.Feed(ch);
            await parser.Feed('\x1B');
            await parser.Feed('\\');
        }

        private void SaveStatusHistory_Click(object sender, RoutedEventArgs e)
        {
            string filename = $"status_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            File.WriteAllLines(filename, statusHistory);
            Logger.Log($"[Status] Historik sparad till {filename}", LogLevel.Info);
        }

        private Brush ClassifyStatusColor(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return Brushes.Gray;

            status = status.ToUpperInvariant();

            if (status.Contains("OK")) return Brushes.Green;
            if (status.Contains("ER") || status.Contains("ERROR")) return Brushes.Red;
            if (status.Contains("QUIT")) return Brushes.Orange;
            if (status.Contains("EMACS")) return Brushes.Blue;

            return Brushes.DarkGray;
        }

        private void RunTerminalSanityReport()
        {
            Logger.Log("=== TerminalSanityReport ===", Logger.LogLevel.Info);

            Logger.Log($"Session: {(session != null ? "OK" : "NULL")}", Logger.LogLevel.Info);
            Logger.Log($"TcpClient: {(tcpClient != null ? "OK" : "NULL")}", Logger.LogLevel.Info);
            Logger.Log($"Parser: {(parser != null ? "OK" : "NULL")}", Logger.LogLevel.Info);
            Logger.Log($"ScreenBuffer: {(screenBuffer != null ? "OK" : "NULL")}", Logger.LogLevel.Info);
            Logger.Log($"ControlHandler: {(controlHandler != null ? "OK" : "NULL")}", Logger.LogLevel.Info);

            if (parser != null)
            {
                Logger.Log($"EmacsMode: {parser.EmacsMode}", Logger.LogLevel.Info);
                Logger.Log($"EmacsLayout: {(parser.EmacsLayout != null ? "OK" : "NULL")}", Logger.LogLevel.Info);
            }

            if (session != null)
            {
                Logger.Log($"Session.IsConnected: {session.IsConnected}", Logger.LogLevel.Info);
            }

            Logger.Log("=== Slut på rapport ===", Logger.LogLevel.Info);
        }

        private async void SendTest_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log("Testknapp: försöker skicka 'A'", LogLevel.Info);
            await tcpClient.SendAsync("A");
        }
        private void ShowStatus_Click(object sender, RoutedEventArgs e)
        {
            Logger.Log($"🧪 StatusText just nu: \"{StatusText.Text}\"", Logger.LogLevel.Info);
        }

        private void StartThreadPulseMonitor()
        {
            threadPulseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            threadPulseTimer.Tick += (s, e) =>
            {
                Logger.Log($"🧠 ThreadPulse: UI={Dispatcher.CheckAccess()}, ClientConnected={tcpClient?.Connected ?? false}", LogLevel.Info);
            };
        }

        private bool IsPrintableKey(Key key)
        {
            return key >= Key.A && key <= Key.Z ||
                   key >= Key.D0 && key <= Key.D9 ||
                   key >= Key.NumPad0 && key <= Key.NumPad9 ||
                   key == Key.Space || key == Key.OemComma || key == Key.OemPeriod;
        }

        private void OnLogProfileChanged(object sender, SelectionChangedEventArgs e)
        {
            string profileName = LogProfileComboBox.SelectedItem.ToString();
            if (LogProfileComboBox.SelectedItem is ComboBoxItem selectedItem)
            {

                if (Enum.TryParse(profileName, out Logger.LogProfile profile))
                {
                    Logger.SetProfile(profile);
                    ActiveProfileLabel.Text = $"Aktiv loggprofil: {profileName}";
                }
            }
        }

        public class StatusColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                string status = value as string;
                if (string.IsNullOrWhiteSpace(status)) return Brushes.Gray;

                status = status.ToUpperInvariant();

                if (status.Contains("OK")) return Brushes.Green;
                if (status.Contains("ER") || status.Contains("ERROR")) return Brushes.Red;
                if (status.Contains("QUIT")) return Brushes.Orange;
                if (status.Contains("EMACS")) return Brushes.Blue;

                return Brushes.DarkGray;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
        }

        public static class UpdateSourceTracker
        {
            public static string LastCaller;
            public static void Set(string source)
            {
                LastCaller = source;
                Logger.Log($"[Tracker] UpdateTerminalDisplay anropas av: {source}", LogLevel.Debug);
            }
        }

        public class InputTracer
        {
            private const int MaxEntries = 100;

            private readonly ObservableCollection<string> _traceLog = new();

            public ObservableCollection<string> TraceLog => _traceLog;

            public void TraceTextInput(string text)
            {
                foreach (char ch in text)
                {
                    byte ascii = (byte)ch;
                    string msg = $"📝 TextInput: '{ch}' → ASCII: 0x{ascii:X2}";
                    Add(msg);
                }
            }

            public void TraceKeyDown(Key key)
            {
                string msg = $"⌨️ KeyDown: {key}";
                Add(msg);
            }

            public void TraceSend(byte[] bytes)
            {
                string ascii = Encoding.ASCII.GetString(bytes)
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");

                string hex = BitConverter.ToString(bytes);
                string msg = $"📤 SEND: {hex}  ASCII: \"{ascii}\"";
                Add(msg);
            }

            private void Add(string msg)
            {
                Logger.Log(msg, Logger.LogLevel.Debug);
                _traceLog.Add(msg);

                if (_traceLog.Count > MaxEntries)
                    _traceLog.RemoveAt(0);
            }
        }
    }
}