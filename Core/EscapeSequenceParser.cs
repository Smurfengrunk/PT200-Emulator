using PT200Emulator.Core.Terminal;
using PT200Emulator.Interfaces;
using PT200Emulator.IO;
using PT200Emulator.Models;
using PT200Emulator.Util;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static PT200Emulator.IO.ControlCharacterHandler;
using static PT200Emulator.Util.Logger;

namespace PT200Emulator.Core
{
    public class EscapeSequenceParser
    {
        private const bool EnableParserDebug = true;

        private enum ParseState { Normal, Escape, CSI, DCS, Esc0 }
        private ParseState state = ParseState.Normal;

        private readonly StringBuilder seqBuffer = new();
        private readonly Dictionary<byte, char> g1Map;
        private bool usingG1 = false;
        private bool emacsMode = false;
        private bool emacsDetectionEnabled = false;
        internal bool EmacsMode => emacsMode;
        private EmacsLayoutModel emacsLayout;
        internal EmacsLayoutModel EmacsLayout => emacsLayout;
        internal event Action EmacsLayoutUpdated;

        private readonly ITerminalClient tcpClient;
        private readonly IScreenBuffer screenBuffer;

        public event Func<byte[], Task> OutgoingDcs;
        public event Func<byte[], Task> OutgoingRaw;

        private readonly Dictionary<string, Action<byte[]>> dcsCommands = new();
        private readonly CsiCommandSet csiCommandSet;
        private readonly Esc0CommandSet esc0CommandSet;
        private readonly ControlCharacterHandler controlHandler;
        private readonly ParserErrorHandler errorHandler = new();

        public EscapeSequenceParser(ITerminalClient client, IScreenBuffer buffer)
        {
            tcpClient = client;
            screenBuffer = buffer;
            g1Map = InitG1Map();

            InitDcsCommands();
            csiCommandSet = new CsiCommandSet(screenBuffer);
            esc0CommandSet = new Esc0CommandSet(screenBuffer);
            controlHandler = new ControlCharacterHandler();
            controlHandler.BreakReceived += HandleBreak;
            controlHandler.RawOutput += bytes => OutgoingRaw?.Invoke(bytes);
        }

        private static bool IsControlCharacter(char ch)
        {
            return ch < 0x20 || ch == 0x7F;
        }

        public async Task Feed(char ch)
        {
            if (IsControlCharacter(ch))
            {
                if (EnableParserDebug)
                    Logger.Log($"[CTRL-IN] 0x{(int)ch:X2}");

                var result = controlHandler.Handle(ch);
                if (result != ControlCharacterResult.NotHandled)
                {
                    errorHandler.Handle($"[CTRL-IN] Hanterat kontrolltecken: 0x{(int)ch:X2}");
                    OutgoingRaw?.Invoke(controlHandler.LastRawBytes);
                    return;
                }

                errorHandler.Handle($"[CTRL-IN] Okänt kontrolltecken: 0x{(int)ch:X2}");
                OutgoingRaw?.Invoke(controlHandler.LastRawBytes);
                return;
            }

            switch (state)
            {
                case ParseState.Normal:
                    HandleNormal(ch);
                    break;
                case ParseState.Escape:
                    HandleEscape(ch);
                    break;
                case ParseState.CSI:
                    HandleCSI(ch);
                    break;
                case ParseState.DCS:
                    try
                    {
                        await HandleDCS(ch);
                    }
                    catch (Exception ex)
                    {
                        errorHandler.Handle(ex, $"DCS-sekvens: {seqBuffer}");
                        state = ParseState.Normal;
                        seqBuffer.Clear();
                    }
                    break;
                case ParseState.Esc0:
                    HandleEsc0(ch);
                    break;
            }
        }

        private void HandleNormal(char ch)
        {
            if (ch == '\x1B') // ESC
            {
                state = ParseState.Escape;
                seqBuffer.Clear();
                return;
            }

            if (ch == '\x0E') // SO
            {
                usingG1 = true;
                Log("[SO] G1 charset aktiv");
                return;
            }

            if (ch == '\x0F') // SI
            {
                usingG1 = false;
                Log("[SI] G0 charset aktiv");
                return;
            }

            if (emacsDetectionEnabled)
            {
                try
                {
                    DetectEmacs(ch);
                }
                catch (Exception ex)
                {
                    errorHandler.Handle(ex, $"DetectEmacs misslyckades för tecken 0x{(int)ch:X2} '{ch}'");
                }
            }
            char mapped = usingG1 ? MapFromG1(ch) : ch;
            screenBuffer.WriteChar(mapped);
        }

        private void HandleEscape(char ch)
        {
            if (ch == '[')
            {
                state = ParseState.CSI;
                seqBuffer.Clear();
                return;
            }

            if (ch == 'P')
            {
                state = ParseState.DCS;
                seqBuffer.Clear();
                return;
            }

            if (ch == '0')
            {
                state = ParseState.Esc0;
                seqBuffer.Clear();
                return;
            }

            HandleEscSequence(ch);
            state = ParseState.Normal;
        }

        private void HandleCSI(char ch)
        {
            seqBuffer.Append(ch);
            if (ch >= '@' && ch <= '~')
            {
                string seq = seqBuffer.ToString();
                Log($"[CSI] {seq}");
                HandleCsiSequence(seq);
                state = ParseState.Normal;
            }
        }

        internal async Task HandleDCS(char ch)
        {
            seqBuffer.Append(ch);
            if (ch == '\\')
            {
                byte[] payload = Encoding.ASCII.GetBytes(seqBuffer.ToString());
                ParseDcsPayload(payload);

                string response = GenerateDcsResponse();
                try
                {
                    await tcpClient.SendAsync(response);
                }
                catch (Exception ex)
                {
                    errorHandler.Handle(ex, "tcpClient.SendAsync i HandleDCS");
                }
                try
                {
                    if (OutgoingDcs != null)
                        await OutgoingDcs.Invoke(Encoding.ASCII.GetBytes(response));
                }
                catch (Exception ex)
                {
                    errorHandler.Handle(ex, "OutgoingDcs.Invoke i HandleDCS");
                }
            }
        }

        private void InitDcsCommands()
        {
            dcsCommands["$Q"] = HandleEmacsLayout;
            dcsCommands["$G"] = data => Logger.Log("[DCS] Set graphics mode ($G) – ignoreras", Logger.LogLevel.Warning);
            dcsCommands["$B"] = data => Logger.Log("[DCS] Load character set ($B) – ignoreras", Logger.LogLevel.Warning);
            dcsCommands["$0"] = HandleEmacsReset;
            dcsCommands["$X"] = data => Logger.Log("[DCS] Specialkommandot $X tolkas", LogLevel.Info);
        }
        internal void ParseDcsPayload(byte[] data)
        {
            Logger.Log($"[DCS-IN] {BitConverter.ToString(data)}", Logger.LogLevel.Debug);
            Logger.LogHex(data, data.Length, "DCS Payload");

            if (data.Length == 0)
            {
                Logger.Log("[DCS] Tom payload – troligen initsekvens", Logger.LogLevel.Info);
                return;
            }

            string ascii = Encoding.ASCII.GetString(data);
            Logger.Log($"[DCS] ASCII: '{ascii}'", Logger.LogLevel.Info);

            foreach (var kvp in dcsCommands)
            {
                if (ascii.StartsWith(kvp.Key))
                {
                    kvp.Value.Invoke(data);
                    return;
                }
            }

            errorHandler.Handle("DCS: Okänt kommando – payload dump följer");
            Logger.LogHex(data, data.Length, "DCS Raw");
        }

        private void HandleEmacsLayout(byte[] data)
        {
            emacsLayout = EmacsLayoutModel.Parse(data);
            emacsMode = true;
            Logger.Log($"[EMACS] Layout tolkad – fält: {emacsLayout?.Fields.Count ?? 0}", Logger.LogLevel.Info);
            EmacsLayoutUpdated?.Invoke();
        }

        private void HandleEmacsReset(byte[] data)
        {
            emacsLayout = null;
            emacsMode = false;
            Logger.Log("[DCS] EMACS reset ($0) tolkas", Logger.LogLevel.Info);
            EmacsLayoutUpdated?.Invoke();
        }


        private void HandleEscSequence(char ch)
        {
            switch (ch)
            {
                case '$':
                    return;
                case 'O':
                    Log("[ESC $ O] G1 charset satt till Graphics");
                    return;
                case 'B':
                    Log("[ESC $ B] G0 charset satt till ASCII");
                    return;
                default:
                    Log($"[ESC] Okänd sekvens: ESC {ch}");
                    return;
            }
        }

        private void HandleCsiSequence(string seq)
        {
            char command = seq[^1]; // sista tecknet
            if (seq.Length == 1) Logger.Log($"[CSI] Endast kommandotecken: {command}", LogLevel.Debug);
            if (string.IsNullOrEmpty(seq)) return;

            string parameters = seq.Substring(0, seq.Length - 1);

            if (csiCommandSet.Commands.TryGetValue(command, out var handler))
            {
                Log($"[CSI] {command} ← {parameters}");
                handler.Invoke(parameters);

                // 🟢 Här sätter du flaggan
                if (command == 'J' && parameters == "2")
                {
                    emacsDetectionEnabled = true;
                    Logger.Log("Emacs-detektering aktiverad efter ESC[2J", LogLevel.Debug);
                }
            }
            else
            {
                errorHandler.Handle($"[CSI] Okänt kommando: ESC[{seq} (0x{(int)command:X2})");
            }
        }

        private void HandleEsc0(char ch)
        {
            seqBuffer.Append(ch);

            if (ch == '!')
            {
                string key = seqBuffer.ToString();
                if (esc0CommandSet.Commands.TryGetValue(key, out var action))
                {
                    Log($"[ESC0] {key}");
                    action.Invoke();
                }
                else
                {
                    Log($"[ESC0] Okänd sekvens: {key}");
                }
                state = ParseState.Normal;
            }
            else if (ch == '*')
            {
                string hex = seqBuffer.ToString();
                if (esc0CommandSet.HexCommands.TryGetValue(hex, out var hexAction))
                {
                    Log($"[ESC0 HEX] {hex}*");
                    hexAction.Invoke(hex);
                }
                else
                {
                    Log($"[ESC0 HEX] Okänd hex: {hex}*");
                }
                state = ParseState.Normal;
            }
        }

        private void DetectEmacs(char ch)
        {
            // Enkel EMACS-detektering via textflöde
            // Du kan förbättra detta med en textbuffer om du vill
            if (!emacsMode && ch == 'E')
            {
                emacsMode = true;
                Log("[MODE] EMACS mode ON");
                tcpClient.SendAsync("\x11"); // XON
                emacsDetectionEnabled = true;
                Log("[CTRL-OUT] XON (0x11) skickad vid EMACS-start");
            }
        }

        private string GenerateDcsResponse()
        {
            return "\x1B\\";
        }

        private char MapFromG1(char ch)
        {
            return g1Map.TryGetValue((byte)ch, out var mapped) ? mapped : ch;
        }

      /*private Dictionary<string, Action> InitEsc0Commands()
        {
            return new Dictionary<string, Action>
            {
                // ────── Linjer och hörn (tecken + '!')
                { "#!", () => DrawGraphic('│') }, // vertikal linje
                { "$!", () => DrawGraphic('─') }, // horisontell linje
                { "%!", () => DrawGraphic('┌') }, // hörn uppe vänster
                { "&!", () => DrawGraphic('┐') }, // hörn uppe höger
                { "'!", () => DrawGraphic('└') }, // hörn nere vänster
                { "(!", () => DrawGraphic('┘') }, // hörn nere höger
                { ")!", () => DrawGraphic('├') }, // T‑korsning vänster
                { "*!", () => DrawGraphic('┤') }, // T‑korsning höger
                { "+!", () => DrawGraphic('┬') }, // T‑korsning upp
                { ",!", () => DrawGraphic('┴') }, // T‑korsning ner
                { "-!", () => DrawGraphic('┼') }, // korsning

                // ────── Statusfält / attribut
                { "6!", () => screenBuffer.SetCursorPosition(screenBuffer.Rows - 1, 0) }, // flytta till statusrad
                { "!!", () => { // rensa statusrad
                    int lastRow = screenBuffer.Rows - 1;
                    for (int c = 0; c < screenBuffer.Cols; c++)
                        screenBuffer.WriteChar(lastRow, c, ' ');
                }},
                //{ "\"!", () => { /* Set status field attributes – kan implementeras  } },

                // ────── Symboler (tecken + '!')
                { "A!", () => DrawGraphic('★') },
                { "B!", () => DrawGraphic('☆') },
                { "C!", () => DrawGraphic('●') },
                { "D!", () => DrawGraphic('○') },
                { "E!", () => DrawGraphic('◆') },
                { "F!", () => DrawGraphic('◇') },
                { "G!", () => DrawGraphic('▲') },
                { "H!", () => DrawGraphic('▼') },
                { "I!", () => DrawGraphic('►') },
                { "J!", () => DrawGraphic('◄') },
                { "\"!", () => {
                    // Set status field attributes – i EMACS används den ofta före text på statusraden.
                    // Vi kan välja att ignorera den visuellt, men det kan vara bra att nollställa ev. attribut.
                    screenBuffer.ResetAttributes();
                                }},
            };
        }

        private Dictionary<string, Action<string>> InitEsc0HexCommands()
        {
            // Hex-koderna är de som skickas som två hextecken följt av '*'
            // Vi tar emot själva hexdelen som string (t.ex. "69") och kan använda den
            return new Dictionary<string, Action<string>>
                {
                    { "69", _ => DrawGraphic('█') }, // full block
                    { "6A", _ => DrawGraphic('▄') }, // lower half block
                    { "6B", _ => DrawGraphic('▀') }, // upper half block
                    { "6C", _ => DrawGraphic('▒') }, // medium shade
                    { "6D", _ => DrawGraphic('░') }, // light shade
                    { "6E", _ => DrawGraphic('▓') }, // dark shade
                    // Här kan du fylla på fler hexkoder från PT200-manualen vid behov
                };
        }*/

        private Dictionary<byte, char> InitG1Map()
        {
            return new Dictionary<byte, char>
        {
            { 0x21, '│' }, { 0x22, '─' }, { 0x23, '┌' }, { 0x24, '┐' },
            { 0x25, '└' }, { 0x26, '┘' }, { 0x27, '├' }, { 0x28, '┤' },
            { 0x29, '┬' }, { 0x2A, '┴' }, { 0x2B, '┼' }, { 0x2C, '█' },
            { 0x2D, '▄' }, { 0x2E, '▀' }, { 0x2F, '▒' }, { 0x30, '░' },
            { 0x31, '▓' }, { 0x32, '▲' }, { 0x33, '▼' }, { 0x34, '►' },
            { 0x35, '◄' }, { 0x36, '★' }, { 0x37, '☆' }, { 0x38, '●' },
            { 0x39, '○' }, { 0x3A, '◆' }, { 0x3B, '◇' }
        };
        }
        private void Log(string msg)
        {
            if (EnableParserDebug && Logger.IsEnabled(Logger.LogLevel.Debug))
                Logger.Log(msg, Logger.LogLevel.Debug);
        }

        private void HandleBreak()
        {
            emacsLayout = null;
            emacsMode = false;
            EmacsLayoutUpdated?.Invoke();
            screenBuffer.Clear();
            screenBuffer.SetCursorPosition(0, 0);
            Logger.Log("[BREAK] Terminalen har nollställts", Logger.LogLevel.Info);
        }
    }
}