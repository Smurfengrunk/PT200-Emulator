using PT200Emulator.Core;
using PT200Emulator.IO;
using PT200Emulator.Models;
using PT200Emulator.Parser;
using PT200Emulator.Protocol;
using PT200Emulator.Util;
using System;
using System.Globalization;
using System.IO;
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

namespace PT200Emulator.UI
{
    public partial class MainWindow : Window
    {
        private PT200State state;
        private DispatcherTimer clockTimer;
        private DispatcherTimer cursorTimer;
        private bool cursorVisible = true;
        private DisplayType currentDisplayType = DisplayType.White;
        private ScreenBuffer screenBuffer;
        private EscapeSequenceParser parser;
        private TcpTerminalClient tcpClient; // instansen vi använder i eventhandlers
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

        public MainWindow()
        {
            InitializeComponent();
            attr = new AttributeTracker();
            themeManager = new ThemeManager(attr, TerminalCanvas, StatusText, ClockTextBlock);
            ConsoleManager.Open();
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

            controlHandler.RawOutput += async bytes =>
            {
                Logger.LogHex(bytes, bytes.Length, "RAW");
                await tcpClient.SendAsync(bytes);
            };
            controlHandler.BreakReceived += () =>
            {
                Logger.Log("BREAK mottagen (Ctrl+P)", LogLevel.Info);
            };

            // Skapa buffert och parser
            screenBuffer = new ScreenBuffer(cols, rows, attr);
            // Skapa parsern
            parser = new EscapeSequenceParser(tcpClient, screenBuffer);
            tcpClient = new TcpTerminalClient(parser);

            // Koppla parserns event till klientens SendAsync
            parser.OutgoingDcs += async bytes =>
            {
                await tcpClient.SendAsync(bytes);
            };
            parser.OutgoingRaw += async bytes =>
            {
                Logger.Log($"[TX MainWindow] Skickar {bytes.Length} bytes: {BitConverter.ToString(bytes)}", LogLevel.Info);
                Logger.LogHex(bytes, bytes.Length, "RAW");
                await tcpClient.SendAsync(bytes);
            };

            parser.EmacsLayoutUpdated += () =>
            {
                Dispatcher.Invoke(() => UpdateTerminalDisplay());
            };

            // Uppdatera UI när data kommer in från WSL/Primos
            tcpClient.DataReceived += () =>
            {
                Dispatcher.Invoke(UpdateTerminalDisplay);
            };

            // Starta anslutningen (justera port/host efter din WSL-setup)
            _ = tcpClient.ConnectAsync("localhost", 2323);
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

            // Rita första gången
            ApplyDisplayTheme(DisplayType.Green);
            UpdateTerminalDisplay();

            // RunParserSelfTest();
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
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyDisplayTheme(DisplayType.Green); // eller valfritt

            /*screenBuffer.MoveCursorHome();

            //attr.SetForeground(Brushes.LimeGreen);
            //attr.SetBackground(Brushes.DarkBlue);

            screenBuffer.WriteChar('L');
            screenBuffer.WriteChar('o');
            screenBuffer.WriteChar('g');
            screenBuffer.WriteChar('i');
            screenBuffer.WriteChar('n');
            screenBuffer.WriteChar(':');
            screenBuffer.WriteChar(' ');
            screenBuffer.WriteChar('\n');

            screenBuffer.WriteChar('>');
            screenBuffer.WriteChar(' ');
            screenBuffer.WriteChar('E');
            screenBuffer.WriteChar('n');
            screenBuffer.WriteChar('t');
            screenBuffer.WriteChar('e');
            screenBuffer.WriteChar('r');
            screenBuffer.WriteChar(' ');
            screenBuffer.WriteChar('p');
            screenBuffer.WriteChar('a');
            screenBuffer.WriteChar('s');
            screenBuffer.WriteChar('s');
            screenBuffer.WriteChar('w');
            screenBuffer.WriteChar('o');
            screenBuffer.WriteChar('r');
            screenBuffer.WriteChar('d');
            screenBuffer.WriteChar(':');
            screenBuffer.WriteChar('\n');*/

            Keyboard.Focus(this); // Se till att fönstret har fokus
        }

        private async void Window_KeyDown(object sender, KeyEventArgs e)
        {
            Logger.Log($"[TX] KeyDown", LogLevel.Info);// Ctrl + [A-Z] → kontrolltecken 0x01–0x1A
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key >= Key.A && e.Key <= Key.Z)
                {
                    char ctrlChar = (char)((int)e.Key - (int)Key.A + 1);
                    var result = controlHandler.Handle(ctrlChar);

                    if (result != ControlCharacterResult.NotHandled)
                    {
                        Logger.Log($"[KEY] Kontrolltecken: {result}", LogLevel.Debug);
                        await tcpClient.SendAsync(new string(ctrlChar, 1));
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Key == Key.OemOpenBrackets) // Ctrl+[ → ESC
                {
                    await tcpClient.SendAsync("\x1B");
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.D2) // Ctrl+@ → 0x00
                {
                    await tcpClient.SendAsync("\0");
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Key == Key.P && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                await tcpClient.SendAsync(new string((char)0x10, 1)); // Ctrl+P = 0x10
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

            if (seq != null)
            {
                await tcpClient.SendAsync(seq);
                e.Handled = true;
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

            await tcpClient.SendAsync(new string(ch, 1)); // Skicka till värddatorn
            await parser.Feed(ch);                              // Visa lokalt
            e.Handled = true;
        }

        private async void Window_TextInput(object sender, TextCompositionEventArgs e)
        {
            string input = e.Text;

            // Ignorera carriage return och backspace här – de hanteras i KeyDown
            if (input.Contains('\r') || input.Contains('\b')) return;

            // Skicka bara till servern – låt parsern mata när det kommer tillbaka
            await tcpClient.SendAsync(input);
        }

        private void UpdateTerminalDisplay()
        {
            //Logger.Log($"[TX] UpdateTerminalDisplay", LogLevel.Info);// Ctrl + [A-Z] → kontrolltecken 0x01–0x1A
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



            if (parser.EmacsMode && parser.EmacsLayout != null)
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
                StatusText.Text = $"Cursor: ({screenBuffer.CursorRow},{screenBuffer.CursorCol}) | Färg: {state.Display}";
            }
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