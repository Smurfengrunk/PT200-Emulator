using PT200Emulator.Core;
using PT200Emulator.IO;
using PT200Emulator.Models;
using PT200Emulator.Parser;
using PT200Emulator.Protocol;
using PT200Emulator.Util;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
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
using static PT200Emulator.Protocol.ControlCharacterHandler;
using static PT200Emulator.Util.Logger;
using static System.Net.Mime.MediaTypeNames;

namespace PT200Emulator.UI
{

    public partial class MainWindow : Window
    {
        private PT200State state;
        private DispatcherTimer clockTimer;
        private DispatcherTimer cursorTimer;
        private DispatcherTimer idleTimer;
        private bool cursorVisible = true;
        private DisplayType currentDisplayType = DisplayType.White;
        private ScreenBuffer screenBuffer;
        private EscapeSequenceParser parser;
        private ITerminalClient tcpClient; // instansen vi använder i eventhandlers
        static double fontSize = 14;
        static double lineHeight = fontSize + 2;
        static double charWidth = 8; // Justera efter fonten
        private string lastStatusLine = "";
        private readonly Queue<string> statusHistory = new Queue<string>();
        private const int MaxStatusHistory = 50;
        private bool promptWasMarkedLastFrame = false;
        private static EmacsLayoutModel EmacsLayout;
        private readonly ControlCharacterHandler controlHandler = new();
        internal AttributeTracker attr = new AttributeTracker();
        private ThemeManager themeManager;
        private TerminalSessionManager session;
        private System.Net.IPAddress host = System.Net.IPAddress.Loopback; // localhost
        private int port = 2323; // Justera efter din WSL-setup
        private bool _terminalReady;
        private string MainWindow_Statustext;
        private Brush MainWindow_StatustextColor = Brushes.OrangeRed;
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
            attr = new AttributeTracker();
            themeManager = new ThemeManager(attr, TerminalCanvas, StatusText, ClockTextBlock);
            StartCursorBlink();
            cursorVisible = true;
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
                ClockTextBlock.Text = DateTime.Now.ToString("HH:mm");
            };
            clockTimer.Start();

            MainWindow_StatustextColor = terminalReady ? Brushes.Black : Brushes.Red;
            MainWindow_Statustext = terminalReady ? "✅ Terminalen är redo" : "⏳ Terminalen laddar...";

            // RunParserSelfTest();
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
            UpdateTerminalDisplay();
            Keyboard.Focus(this);
        }

        private void SetWhiteDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.White);
        private void SetBlueDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.Blue);
        private void SetGreenDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.Green);
        private void SetAmberDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.Amber);
        private void SetFullColorDisplay(object sender, RoutedEventArgs e) => ApplyDisplayTheme(DisplayType.FullColor);

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
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

                session = new TerminalSessionManager(
                    host,
                    port,
                    screenBuffer,
                    text => Dispatcher.BeginInvoke(UpdateTerminalDisplay),
                    () => Dispatcher.BeginInvoke(UpdateTerminalDisplay)
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
                    await session.StartSessionAsync();

                    await Task.Delay(500); // Vänta 0.5 sek efter StartSessionAsync
                    terminalReady = true;
                    //tcpClient = session.Client;
                    InitializeTerminalSession();
                    Logger.Log($"tcpClient tilldelad – Hash: {tcpClient?.GetHashCode() ?? -1}", LogLevel.Info);
                    parser = session.Parser;
                    controlHandler.RawOutput += async bytes =>
                    {
                        if (tcpClient != null)
                        {
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
                    UpdateTerminalDisplay();
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
            Logger.Log($"tcpClient tilldelad – Hash: {tcpClient?.GetHashCode() ?? -1}", LogLevel.Info);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!terminalReady)
            {
                Logger.Log("Input blockerat – terminalen är inte redo", LogLevel.Warning);
                e.Handled = true;
            }
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Logger.Log($"[KeyDown] MainWindow-instans: {this.GetHashCode()}", LogLevel.Info);
            Logger.Log($"[KeyDown] StatusText-instans: {StatusText.GetHashCode()}", Logger.LogLevel.Info);
            Logger.Log($"[KeyDown] StatusText.Text: \"{StatusText.Text}\"", Logger.LogLevel.Info);
            idleTimer.Stop();
            idleTimer.Start(); // 🔁 Starta om varje gång användaren skriver

            Logger.Log($"KeyDown – terminalReady: {terminalReady}, tcpClient null? {tcpClient == null}", LogLevel.Debug);
            Logger.Log($"KeyDown – IsFocused: {this.IsFocused}, Key Prressed = {e.Key}", LogLevel.Debug);
            // Testa att skicka ett tecken direkt
            SendTestA_Click(sender, e);
            Logger.Log($"[Window_KeyDown] tcpClient tilldelad – Hash: {tcpClient.GetHashCode()}", LogLevel.Info);
            Logger.Log($"tcpClient tilldelad – Hash: {tcpClient?.GetHashCode() ?? -1}", LogLevel.Info);

            if (!terminalReady || tcpClient == null)
            {
                Logger.Log("Terminalen är inte redo – KeyDown ignoreras", Logger.LogLevel.Warning);
                e.Handled = true;
                return;
            }
            Logger.Log($"tcpClient-check passerad – null? {tcpClient == null}", Logger.LogLevel.Debug);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key >= Key.A && e.Key <= Key.Z)
                {
                    char ctrlChar = (char)((int)e.Key - (int)Key.A + 1);
                    var result = controlHandler.Handle(ctrlChar);

                    if (result != ControlCharacterResult.NotHandled)
                    {
                        Logger.Log($"[KEY] Kontrolltecken: {result}", LogLevel.Debug);
                        try
                        {
                            await tcpClient.SendAsync(new string(ctrlChar, 1));
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                            tcpClient = null;
                            terminalReady = false;
                        }
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Key == Key.OemOpenBrackets) // Ctrl+[ → ESC
                {
                    try
                    {
                        await tcpClient.SendAsync("\x1B");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                        tcpClient = null;
                        terminalReady = false;
                    }
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.D2) // Ctrl+@ → 0x00
                {
                    try
                    {
                        await tcpClient.SendAsync("\0");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                        tcpClient = null;
                        terminalReady = false;
                    }
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                try
                {
                    await tcpClient.SendAsync(new string((char)0x10, 1)); // Ctrl+P = 0x10
                }
                catch (Exception ex)
                {
                    Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                    tcpClient = null;
                    terminalReady = false;
                }
                e.Handled = true;
                return;
            }

            bool hasMod = Keyboard.Modifiers != ModifierKeys.None;
            string seq = null;

            switch (e.Key)
            {
                case Key.Tab when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                    seq = "\x1B[Z"; // Backtab
                    break;
                case Key.Tab:
                    seq = "\t";
                    break;
                case Key.Enter:
                    seq = "\r\n"; // PT200 skickar CR
                    break;
                case Key.Back:
                    seq = "\b";
                    break;

                // Navigering – pilar
                case Key.Up:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}A" : "\x1B[A";
                    break;
                case Key.Down:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}B" : "\x1B[B";
                    break;
                case Key.Right:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}C" : "\x1B[C";
                    break;
                case Key.Left:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}D" : "\x1B[D";
                    break;

                // Home/End
                case Key.Home:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}H" : "\x1B[H";
                    break;
                case Key.End:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}F" : "\x1B[F";
                    break;

                // PageUp/PageDown
                case Key.PageUp:
                    seq = hasMod ? $"\x1B[5;{GetModCode()}~" : "\x1B[5~";
                    break;
                case Key.PageDown:
                    seq = hasMod ? $"\x1B[6;{GetModCode()}~" : "\x1B[6~";
                    break;

                // Insert/Delete
                case Key.Insert:
                    seq = hasMod ? $"\x1B[2;{GetModCode()}~" : "\x1B[2~";
                    break;
                case Key.Delete:
                    seq = hasMod ? $"\x1B[3;{GetModCode()}~" : "\x1B[3~";
                    break;

                // F1–F4 (PF1–PF4 på PT200)
                case Key.F1:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}P" : "\x1BOP";
                    break;
                case Key.F2:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}Q" : "\x1BOQ";
                    break;
                case Key.F3:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}R" : "\x1BOR";
                    break;
                case Key.F4:
                    seq = hasMod ? $"\x1B[1;{GetModCode()}S" : "\x1BOS";
                    break;

                // F5–F12 (VT220-stil)
                case Key.F5:
                    seq = hasMod ? $"\x1B[15;{GetModCode()}~" : "\x1B[15~";
                    break;
                case Key.F6:
                    seq = hasMod ? $"\x1B[17;{GetModCode()}~" : "\x1B[17~";
                    break;
                case Key.F7:
                    seq = hasMod ? $"\x1B[18;{GetModCode()}~" : "\x1B[18~";
                    break;
                case Key.F8:
                    seq = hasMod ? $"\x1B[19;{GetModCode()}~" : "\x1B[19~";
                    break;
                case Key.F9:
                    seq = hasMod ? $"\x1B[20;{GetModCode()}~" : "\x1B[20~";
                    break;
                case Key.F10:
                    seq = hasMod ? $"\x1B[21;{GetModCode()}~" : "\x1B[21~";
                    break;
                case Key.F11:
                    seq = hasMod ? $"\x1B[23;{GetModCode()}~" : "\x1B[23~";
                    break;
                case Key.F12:
                    seq = hasMod ? $"\x1B[24;{GetModCode()}~" : "\x1B[24~";
                    break;
            }

            if (seq != null && tcpClient != null)
            {
                try
                {
                    Logger.Log($"Försöker skicka: \"{seq}\"", LogLevel.Debug);
                    await tcpClient.SendAsync(seq);
                }
                catch (Exception ex)
                {
                    Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                    tcpClient = null;
                    terminalReady = false;
                }
                e.Handled = true;
            }
            else if (tcpClient == null)
            {
                Logger.Log("tcpClient är null i Window_KeyDown – sekvens ignoreras", Logger.LogLevel.Warning);
            }
        }

        int GetModCode()
        {
            int code = 1;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) code += 1;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) code += 2;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) code += 4;
            return code;
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
            string input = e.Text;
            Logger.Log($"TextInput triggas – text: \"{e.Text}\"", LogLevel.Debug);
            Logger.Log($"terminalReady: {terminalReady}", LogLevel.Debug);

            // Ignorera carriage return och backspace här – de hanteras i KeyDown
            if (input.Contains('\r') || input.Contains('\b')) return;

            // Skicka bara till servern – låt parsern mata när det kommer tillbaka
            if (tcpClient != null)
                try
                {
                    await tcpClient.SendAsync(input);
                }
                catch (Exception ex)
                {
                    Logger.Log($"💥 SendAsync misslyckades: {ex.Message}", LogLevel.Error);
                    tcpClient = null;
                    terminalReady = false;
                }
            else
            {
                Logger.Log($"Ignorerar input \"{input}\" – tcpClient är null", Logger.LogLevel.Warning);
            }
        }

        private DateTime lastUpdate = DateTime.MinValue;

        private void UpdateTerminalDisplay()
        {
            //Logger.Log($"[TX] UpdateTerminalDisplay", LogLevel.Info);// Ctrl + [A-Z] → kontrolltecken 0x01–0x1A

            if ((DateTime.Now - lastUpdate).TotalMilliseconds < 50)
                return;

            lastUpdate = DateTime.Now;

            bool isPromptPosition = screenBuffer.CursorRow == 21 && screenBuffer.CursorCol == 0;
            TerminalCanvas.Height = screenBuffer.Rows * lineHeight;
            TerminalCanvas.Children.Clear();

            var lines = screenBuffer.GetAllLines().ToList();
            // Säkerställ att antalet rader i lines är lika många som i screenbuffer
            while (lines.Count <= screenBuffer.CursorRow)
                lines.Add(""); // Lägg till tomma rader så att cursorn kan ritas

            var typeface = new Typeface("Consolas");

            for (int r = 0; r < screenBuffer.Rows; r++)
            {
                for (int c = 0; c < screenBuffer.Cols; c++)
                {
                    var cell = screenBuffer.GetCell(r, c);

                    var text = new TextBlock
                    {
                        Text = cell.Character.ToString(),
                        Foreground = cell.Foreground,
                        //Foreground = Brushes.LimeGreen,
                        //Background = Brushes.DarkBlue,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = fontSize,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var border = new Border
                    {
                        Background = cell.Background,
                        Width = charWidth,
                        Height = lineHeight,
                        Child = text
                    };
                    /*if (!string.IsNullOrWhiteSpace(cell.Character.ToString()))
                    {
                        Logger.Log($"[Render] '{cell.Character}' at ({r},{c}) → FG={cell.Foreground}, BG={cell.Background}", LogLevel.Debug);
                    }*/
                    Canvas.SetLeft(border, c * charWidth);
                    Canvas.SetTop(border, r * lineHeight);
                    TerminalCanvas.Children.Add(border);
                }
            }

            if (isPromptPosition)
            {
                var marker = new Rectangle
                {
                    Width = charWidth,
                    Height = lineHeight,
                    Fill = Brushes.Yellow,
                    Opacity = 0.3
                };
                Canvas.SetTop(marker, screenBuffer.CursorRow * lineHeight);
                Canvas.SetLeft(marker, screenBuffer.CursorCol * charWidth);
                TerminalCanvas.Children.Add(marker);
                if (!promptWasMarkedLastFrame)
                {
                    Logger.Log("[UI] Visuell markering för EMACS-prompt satt", Logger.LogLevel.Debug);
                    promptWasMarkedLastFrame = true;
                }
                else
                {
                    promptWasMarkedLastFrame = false;
                }
            }



            if (parser != null && parser.EmacsMode && parser.EmacsLayout != null)
            {
                Logger.Log($"[UI] EMACS-läge: {parser.EmacsMode}, Fält: {parser.EmacsLayout?.Fields.Count ?? 0}", LogLevel.Debug);
                if (parser.EmacsLayout?.IsActive == true)
                {
                    Logger.Log($"[UI] UpdateTerminalDisplay – EMACS aktiv: {parser.EmacsMode}, layout: {(parser.EmacsLayout == null ? "null" : "fylld")}", LogLevel.Info);
                    foreach (var field in parser.EmacsLayout.Fields)
                    {
                        Logger.Log($"[UI] Fält: rad={field.Row}, kol={field.Col}, längd={field.Length}", LogLevel.Debug);
                        Logger.Log($"[EMACS] Fält tolkade: {parser.EmacsLayout?.Fields.Count ?? 0}", LogLevel.Info);


                    }
                }
            }
            else
            {
                string statusLine = screenBuffer.GetLine(screenBuffer.Rows - 1);
                if (!string.IsNullOrWhiteSpace(statusLine) && statusLine != lastStatusLine)
                {
                    lastStatusLine = statusLine;

                    if (statusHistory.Count >= MaxStatusHistory)
                        statusHistory.Dequeue();

                    statusHistory.Enqueue(statusLine.Trim());

                    Logger.Log($"[Status] Updated: '{lastStatusLine}'", LogLevel.Info);
                }

                StatusHistoryBox.Items.Clear();

                foreach (var status in statusHistory)
                {
                    var item = new ListBoxItem
                    {
                        Content = status,
                        Foreground = ClassifyStatusColor(status)
                    };
                    StatusHistoryBox.Items.Add(item);
                }

                // Lägg till cursor som en separat Path
                var cursorY = screenBuffer.CursorRow * lineHeight + fontSize;
                var cursorX = screenBuffer.CursorCol * charWidth;

                var cursorGeometry = new LineGeometry(
                    new Point(cursorX, cursorY),
                    new Point(cursorX + charWidth, cursorY));

                var cursorPath = new System.Windows.Shapes.Path
                {
                    Data = cursorGeometry,
                    Stroke = DisplayTheme.GetForeground(currentDisplayType),
                    StrokeThickness = 1
                };

                if (cursorVisible)
                {
                    TerminalCanvas.Children.Add(cursorPath);
                }

                // Uppdatera statusrad en gång
                StatusText.Foreground = MainWindow_StatustextColor;
                StatusText.Text = $"Cursor: ({screenBuffer.CursorRow},{screenBuffer.CursorCol}) | Färg: {state.Display} | {MainWindow_Statustext}";
            }
            Logger.Log($"[UpdateTerminalDisplay] StatusText-instans: {StatusText.GetHashCode()}", Logger.LogLevel.Info);
            Logger.Log($"[UpdateTerminalDisplay] StatusText.Text: \"{StatusText.Text}\"", Logger.LogLevel.Info);

        }

        private void StartCursorBlink()
        {
            cursorTimer = new DispatcherTimer();
            cursorTimer.Interval = TimeSpan.FromMilliseconds(500);
            cursorTimer.Tick += (s, e) =>
            {
                cursorVisible = !cursorVisible;
                UpdateTerminalDisplay();
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

}