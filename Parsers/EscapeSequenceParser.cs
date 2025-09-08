using PT200Emulator.Core;
using PT200Emulator.IO;
using PT200Emulator.Models;
using PT200Emulator.Util;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using static PT200Emulator.Util.Logger;
using static ScreenBuffer;

namespace PT200Emulator.Parser
{
    public class EscapeSequenceParser
    {
        private const bool EnableParserDebug = true;

        internal enum ParseState { Normal, Escape, CSI, DCS, Esc0 }
        internal ParseState state = ParseState.Normal;

        private readonly StringBuilder seqBuffer = new();
        private readonly Dictionary<byte, char> g1Map;
        private bool usingG1 = false;
        private bool emacsMode = false;
        internal bool EmacsMode => emacsMode;
        private EmacsLayoutModel emacsLayout;
        internal EmacsLayoutModel EmacsLayout => emacsLayout;
        internal event Action EmacsLayoutUpdated;

        private readonly IScreenBuffer screenBuffer;

        public event Func<byte[], Task> OutgoingDcs;
        public event Func<byte[], Task> OutgoingRaw;
        public event Action BufferUpdated;

        private readonly Dictionary<string, Action<byte[]>> dcsCommands = new();
        private readonly CsiCommandSet csiCommandSet;
        private readonly Esc0CommandSet esc0CommandSet;
        private readonly ControlCharacterHandler controlHandler;
        private readonly ParserErrorHandler errorHandler = new();
        private readonly EmacsDetector emacsDetector = new();
        private ITerminalClient _client;
        private readonly IScreenBuffer _buffer;
        private readonly TextAttributeState currentAttributes = new();

        public EscapeSequenceParser(IScreenBuffer buffer)
        {
            _buffer = buffer;
            screenBuffer = buffer;
            g1Map = InitG1Map();

            InitDcsCommands();
            csiCommandSet = new CsiCommandSet(screenBuffer);
            esc0CommandSet = new Esc0CommandSet(screenBuffer);
            controlHandler = new ControlCharacterHandler();
            controlHandler.BreakReceived += HandleBreak;
            controlHandler.RawOutput += bytes =>
            {
                Logger.Log($"[TX:OutgoingRaw] {BitConverter.ToString(bytes)}", LogLevel.Info);
                OutgoingRaw?.Invoke(bytes);
            };

        }
        public void SetClient(ITerminalClient client)
        {
            _client = client;
        }


        private static bool IsControlCharacter(char ch)
        {
            return ch < 0x20 || ch == 0x7F;
        }

        public async Task Feed(char ch, bool fromUser)
        {
            //Logger.Log($"[FEED] RX byte 0x{(int)ch:X2} → '{(char.IsControl(ch) ? "." : ch)}' | State={state}", LogLevel.Debug); if (IsControlCharacter(ch))
            Logger.Log($"[FEED] '{ch}' (int: {(int)ch}) | IsControl: {IsControlCharacter(ch)} | State: {state}", LogLevel.Debug);
            if (EnableParserDebug)
                Logger.Log($"[CTRL-IN] 0x{(int)ch:X2}", LogLevel.Debug);

            var result = controlHandler.Handle(ch);
            if (IsControlCharacter(ch))
            {
                var cresult = controlHandler.Handle(ch);

                switch (cresult)
                {
                    case ControlCharacterHandler.ControlCharacterResult.LineFeed:
                    case ControlCharacterHandler.ControlCharacterResult.CarriageReturn:
                        HandleNormal(ch);
                        break;
                    case ControlCharacterHandler.ControlCharacterResult.Bell:
                    case ControlCharacterHandler.ControlCharacterResult.Abort:
                        OutgoingRaw?.Invoke(controlHandler.LastRawBytes);
                        break;
                    case ControlCharacterHandler.ControlCharacterResult.NotHandled:
                    case ControlCharacterHandler.ControlCharacterResult.Null:
                    case ControlCharacterHandler.ControlCharacterResult.FormFeed:
                        Logger.Log($"[Feed] Tecken ignorerat: '{ch}' i state {state}", LogLevel.Trace); break;
                    default:
                        // Okänt kontrolltecken
                        Logger.Log($"[CTRL-IN] Okänt kontrolltecken: 0x{(int)ch:X2}", LogLevel.Warning);
                        OutgoingRaw?.Invoke(controlHandler.LastRawBytes);
                        break;
                }
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
                    if (fromUser) OutgoingRaw?.Invoke(new[] { (byte)ch }); // skicka vidare
                    break;
                case ParseState.Escape:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleEscape(ch);
                    state = ParseState.Normal;
                    break;
                case ParseState.CSI:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleCSI(ch);
                    state = ParseState.Normal;
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
                    state = ParseState.Normal;
                    break;
                case ParseState.Esc0:
                    Logger.Log($"[ParserDebugger] State changed → {state}", LogLevel.Debug);
                    HandleEsc0(ch);
                    state = ParseState.Normal;
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
                Logger.Log("[SO] G1 charset aktiv");
                return;
            }

            if (ch == '\x0F') // SI
            {
                usingG1 = false;
                Logger.Log("[SI] G0 charset aktiv");
                return;
            }

            emacsDetector.Feed(ch);

            if (emacsDetector.IsReady && !emacsMode)
            {
                emacsMode = true;
                _client.SendAsync("\x11"); // XON
                Logger.Log("[MODE] EMACS mode ON");
                Logger.Log("[CTRL-OUT] XON (0x11) skickad vid EMACS-start");
            }
            char mapped = usingG1 ? MapFromG1(ch) : ch;
            Logger.Log($"[NORMAL] Mapped '{ch}' → '{mapped}'", LogLevel.Debug);
            if (screenBuffer == null)
            {
                Logger.Log("❌ screenBuffer är null i HandleNormal!", LogLevel.Error);
                return;
            }
            try
            {
                screenBuffer.WriteChar(mapped);
                Logger.Log($"✅ Tecken '{mapped}' matades in i buffer", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Logger.Log($"💥 WriteChar kastade undantag: {ex.Message}", LogLevel.Error);
            }
            Logger.Log($"✅ Tecken '{mapped}' matades in i buffer", LogLevel.Trace);
            BufferUpdated?.Invoke();
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
                    await _client.SendAsync(response);
                }
                catch (Exception ex)
                {
                    errorHandler.Handle(ex, "_client.SendAsync i HandleDCS");
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
            if (string.IsNullOrEmpty(seq)) return;

            char command = seq[^1]; // sista tecknet
            if (seq.Length == 1)
                Logger.Log($"[CSI] Endast kommandotecken: {command}", LogLevel.Debug);

            string parameters = seq.Substring(0, seq.Length - 1);

            if (csiCommandSet.Commands.TryGetValue(command, out var handler))
            {
                Log($"[CSI] {command} ← {parameters}");
                handler.Invoke(parameters);

                if (command == 'J' && parameters == "2")
                {
                    Logger.Log("Emacs-detektering aktiverad efter ESC[2J", LogLevel.Debug);
                }

                if (command == 'm') // SGR – Set Graphics Rendition
                {
                    var codes = parameters.Split(';');
                    foreach (var codeStr in codes)
                    {
                        if (int.TryParse(codeStr, out int code))
                        {
                            switch (code)
                            {
                                case 0:
                                    screenBuffer.CurrentStyle.Reset();
                                    break;
                                case 5:
                                    screenBuffer.CurrentStyle.Blink = true;
                                    break;
                                default:
                                    if (code >= 30 && code <= 37)
                                        screenBuffer.CurrentStyle.Foreground = ColorThemeManager.GetForeground(code);
                                    else if (code >= 40 && code <= 47)
                                        screenBuffer.CurrentStyle.Background = ColorThemeManager.GetBackground(code);
                                    break;
                            }
                        }
                    }

                    Logger.Log($"🎨 Färgsekvens tolkad: ESC[{parameters}m", LogLevel.Debug);
                }
            }
            else
            {
                errorHandler.Handle($"[CSI] Okänt kommando: ESC[{seq} (0x{(int)command:X2}) med parameter '{parameters}'");
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
                _client.SendAsync("\x11"); // XON
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

        public int GetOutgoingRawHandlerCount()
        {
            return OutgoingRaw?.GetInvocationList().Length ?? 0;
        }
        public string CurrentState => state.ToString();
    }
}