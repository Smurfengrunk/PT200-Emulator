using PT200Emulator.Core;
using PT200Emulator.IO;
using PT200Emulator.Models;
using PT200Emulator.Protocol;
using PT200Emulator.Util;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static PT200Emulator.Protocol.ControlCharacterHandler;
using static PT200Emulator.Util.Logger;

namespace PT200Emulator.Parser
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
        private readonly EmacsDetector emacsDetector = new();

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
            Logger.Log($"[FEED] RX byte 0x{(int)ch:X2} → '{(char.IsControl(ch) ? "." : ch)}' | State={state}", LogLevel.Debug); if (IsControlCharacter(ch))
            {
                if (EnableParserDebug)
                    Logger.Log($"[CTRL-IN] 0x{(int)ch:X2}");

                var result = controlHandler.Handle(ch);
                if (IsControlCharacter(ch))
                {
                    if (EnableParserDebug)
                        Logger.Log($"[CTRL-IN] 0x{(int)ch:X2}", LogLevel.Debug);

                    // Hanterade kontrolltecken
                    // Hantera specialfall
                    if (result is ControlCharacterResult.LineFeed or ControlCharacterResult.CarriageReturn)
                    {
                        HandleNormal(ch); // eller en särskild metod för radslut
                    }
                    else if (result is ControlCharacterResult.Bell or ControlCharacterResult.Abort)
                    {
                        OutgoingRaw?.Invoke(controlHandler.LastRawBytes);
                    }
                    return;

                }

                // Okänt kontrolltecken
                Logger.Log($"[CTRL-IN] Okänt kontrolltecken: 0x{(int)ch:X2}", LogLevel.Warning);
                    OutgoingRaw?.Invoke(controlHandler.LastRawBytes);
                    return;
                }

            if (EnableParserDebug)
            {
                Logger.Log($"[ParserDebugger] Feed('{(char.IsControl(ch) ? $"CTRL({(int)ch:X2})" : ch.ToString())}') | State={state}", LogLevel.Debug);
            }

            switch (state)
            {
                case ParseState.Normal:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleNormal(ch);
                    break;
                case ParseState.Escape:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleEscape(ch);
                    break;
                case ParseState.CSI:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleCSI(ch);
                    break;
                case ParseState.DCS:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
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
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleEsc0(ch);
                    break;
            }
        }

        private void HandleNormal(char ch)
        {
            Logger.Log($"[NORMAL] '{ch}' → WriteChar", LogLevel.Debug);
            if (EnableParserDebug)
                Logger.Log($"[ParserDebugger] Normal → '{ch}'", LogLevel.Debug);

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

            emacsDetector.Feed(ch);

            if (emacsDetector.IsReady && !emacsMode)
            {
                emacsMode = true;
                tcpClient.SendAsync("\x11"); // XON
                Logger.Log("[MODE] EMACS mode ON");
                Logger.Log("[CTRL-OUT] XON (0x11) skickad vid EMACS-start");
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
            dcsCommands["$G"] = data => Logger.Log("[DCS] Set graphics mode ($G) – ignoreras", LogLevel.Warning);
            dcsCommands["$B"] = data => Logger.Log("[DCS] Load character set ($B) – ignoreras", LogLevel.Warning);
            dcsCommands["$0"] = HandleEmacsReset;
            dcsCommands["$X"] = data => Logger.Log("[DCS] Specialkommandot $X tolkas", LogLevel.Info);
        }
        internal void ParseDcsPayload(byte[] data)
        {
            Logger.Log($"[DCS-IN] {BitConverter.ToString(data)}", LogLevel.Debug);
            LogHex(data, data.Length, "DCS Payload");

            if (data.Length == 0)
            {
                Logger.Log("[DCS] Tom payload – troligen initsekvens", LogLevel.Info);
                return;
            }

            string ascii = Encoding.ASCII.GetString(data);
            Logger.Log($"[DCS] ASCII: '{ascii}'", LogLevel.Info);

            foreach (var kvp in dcsCommands)
            {
                if (ascii.StartsWith(kvp.Key))
                {
                    kvp.Value.Invoke(data);
                    return;
                }
            }

            errorHandler.Handle("DCS: Okänt kommando – payload dump följer");
            LogHex(data, data.Length, "DCS Raw");
        }

        private void HandleEmacsLayout(byte[] data)
        {
            emacsLayout = EmacsLayoutModel.Parse(data);
            emacsMode = true;
            Logger.Log($"[EMACS] Layout tolkad – fält: {emacsLayout?.Fields.Count ?? 0}", LogLevel.Info);
            EmacsLayoutUpdated?.Invoke();
        }

        private void HandleEmacsReset(byte[] data)
        {
            emacsLayout = null;
            emacsMode = false;
            Logger.Log("[DCS] EMACS reset ($0) tolkas", LogLevel.Info);
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
            if (EnableParserDebug && IsEnabled(LogLevel.Debug))
                Logger.Log(msg, LogLevel.Debug);
        }

        private void HandleBreak()
        {
            emacsLayout = null;
            emacsMode = false;
            EmacsLayoutUpdated?.Invoke();
            screenBuffer.Clear();
            screenBuffer.SetCursorPosition(0, 0);
            Logger.Log("[BREAK] Terminalen har nollställts", LogLevel.Info);
        }
    }
}