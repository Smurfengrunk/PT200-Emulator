using Microsoft.Extensions.Logging;
using PT200Emulator.Core.Emulator;
using PT200Emulator.Core.Input;
using PT200Emulator.Core.Parser;
using PT200Emulator.DummyImplementations;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.IO; // Viktigt för Path
using System.Text;

namespace PT200Emulator.Core.Parser
{
    public class TerminalParser : ITerminalParser
    {
        private readonly CharTableManager charTables;
        private readonly EscapeSequenceHandler escHandler;
        private readonly PercentHandler percentHandler;
        private readonly OscHandler oscHandler;
        private readonly DollarCommandHandler dollarCommandHandler = new();
        public readonly DcsSequenceHandler dcsHandler;
        public readonly IScreenBuffer screenBuffer;
        private List<byte> dcsBuffer = new();
        private readonly TerminalState termState;
        private readonly CsiSequenceHandler _csiHandler;
        private List<byte> _csiBuffer = new();

        public event Action<IReadOnlyList<TerminalAction>> ActionsReady;
        public event Action<byte[]> OnDcsResponse;
        public char Translate(byte code) => charTables.Translate(code);
        enum ParseState
        {
            Normal,
            Escape,
            CSI,
            DCS,
            OSC,
            Esc0,
            EscDollar,
            EscPercent,
            EscOther
        }
        private ParseState state = ParseState.Normal;
        private readonly StringBuilder seqBuffer = new();
        private readonly InputController _controller;

        // Dummy för att undvika idiotiska varningar
        private void SuppressEventWarnings()
        {
            _ = OnDcsResponse;
            _ = ActionsReady;
        }
        public TerminalParser(IDataPathProvider paths, TerminalState state, InputController controller)
        {
            var g0Path = Path.Combine(paths.CharTablesPath, "G0.json");
            var g1Path = Path.Combine(paths.CharTablesPath, "G1.json");

            charTables = new CharTableManager(g0Path, g1Path);
            escHandler = new EscapeSequenceHandler(charTables);
            _csiHandler = new CsiSequenceHandler(new CsiCommandTable(Path.Combine(paths.BasePath, "Data", "CsiCommands.json")));
            dcsHandler = new DcsSequenceHandler(state, (Path.Combine(paths.BasePath, "Data", "DcsBitGroups.json")));
            percentHandler = new PercentHandler();
            oscHandler = new OscHandler();
            dcsHandler = new DcsSequenceHandler(state, (Path.Combine(paths.BasePath, "Data", "DcsBitGroups.json")));
            this.termState = state ?? throw new ArgumentNullException(nameof(state));
            screenBuffer = state.ScreenBuffer;
            _controller = controller;
            dcsHandler.OnDcsResponse += bytes => OnDcsResponse?.Invoke(bytes);
            this.LogDebug($"[TERMINALPARSER] Hashcode = {this.GetHashCode()}, OnDcsResponse is {(OnDcsResponse == null ? "null" : "set")}");
            this.LogDebug($"[TERMINALPARSER] Subscribers = {OnDcsResponse?.GetInvocationList().Length ?? 0}"); 
        }

        private void SetState(ParseState newState)
        {
            this.LogDebug($"[Parser] State → {newState}", LogLevel.Trace);
            state = newState;
        }

        private void ClearSeqBuffer()
        {
            this.LogDebug("[Parser] Sekvensbuffer rensad", LogLevel.Trace);
            seqBuffer.Clear();
        }

        /// <summary>
        /// Tar emot inkommande bytes och tolkar dem.
        /// </summary>
        /// 
        public void Feed(byte[] data) => Feed(data.AsSpan());
        public void Feed(ReadOnlySpan<byte> data)
        {
            this.LogDebug($"[FEED] Parser hash={this.GetHashCode()}, " +
                          $"OnDcsResponse is {(OnDcsResponse == null ? "null" : "set")}, " +
                          $"Subscribers={OnDcsResponse?.GetInvocationList().Length ?? 0}");
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];

                switch (state)
                {
                    case ParseState.Normal:
                        if (b == 0x1B) // ESC
                        {
                            state = ParseState.Escape;
                            seqBuffer.Clear();
                            continue;
                        }
                        switch (b)
                        {
                            case 0x08: screenBuffer.Backspace(); break;
                            case 0x09: screenBuffer.Tab(); break;
                            case 0x0A: screenBuffer.LineFeed(); break;
                            case 0x0D: screenBuffer.CarriageReturn(); break;
                            default: screenBuffer.WriteChar(charTables.Translate(b)); break;
                        }
                        break;

                    case ParseState.Escape:
                        if (b == '[')
                        {
                            state = ParseState.CSI;
                            seqBuffer.Clear();
                            seqBuffer.Append((char)b);
                        }
                        else if (b == ']')
                        {
                            state = ParseState.OSC;
                            seqBuffer.Clear();
                            seqBuffer.Append((char)b);
                        }
                        else if (b == 'P')
                        {
                            state = ParseState.DCS;
                            seqBuffer.Clear();
                        }
                        else
                        {
                            seqBuffer.Append((char)b);
                            HandleEscOther(seqBuffer.ToString());
                            state = ParseState.Normal;
                        }
                        break;

                    case ParseState.CSI:
                        seqBuffer.Append((char)b);
                        if (b >= 0x40 && b <= 0x7E)
                        {
                            HandleCsi(seqBuffer.ToString());
                            seqBuffer.Clear();
                            state = ParseState.Normal;
                        }
                        break;

                    case ParseState.OSC:
                        seqBuffer.Append((char)b);
                        if (b == 0x07 || (seqBuffer.Length >= 2 && seqBuffer[^2] == 0x1B && seqBuffer[^1] == '\\'))
                        {
                            HandleOsc(seqBuffer.ToString());
                            seqBuffer.Clear();
                            state = ParseState.Normal;
                        }
                        break;

                    case ParseState.DCS:
                        // Leta efter ESC \
                        if (b == 0x1B && i + 1 < data.Length && data[i + 1] == 0x5C)
                        {
                            this.LogDebug("[Parser Feed] Esc P Esc \\ - statusförfrågan");
                            HandleDcs(seqBuffer.ToString());
                            seqBuffer.Clear();
                            state = ParseState.Normal;
                            i++; // hoppa över '\'
                        }
                        else
                        {
                            seqBuffer.Append((char)b);
                        }
                        break;
                }
            }
        }

        public void Parse(string sequence)
        {
            if (sequence.StartsWith("\x1B["))
                HandleCsi(sequence);
            else if (sequence.StartsWith("\x1B]"))
                HandleOsc(sequence);
            else if (sequence.StartsWith("\x1B$"))
                HandleCharset(sequence);
            else if (sequence.StartsWith("\x1B%"))
                HandleEncoding(sequence);
            else if (sequence.StartsWith("\x1BP"))
                dcsHandler.Handle(Encoding.ASCII.GetBytes(sequence), _controller);
            else
                HandleSingleEsc(sequence);
        }

        private void HandleSingleEsc(string sequence) => escHandler.Handle(sequence);
        private void HandleOsc(string sequence) => oscHandler.Handle(sequence);
        private void HandleEncoding(string sequence) => percentHandler.Handle(sequence);
        private void HandleCsi(string sequence) => _csiHandler.Handle(sequence);
        private void HandleCharset(string sequence) => escHandler.Handle(sequence);
        private void HandleDcs(string sequence) => dcsHandler.Handle(Encoding.ASCII.GetBytes(sequence), _controller);

        private void HandleEscPercent(char ch)
          {
              seqBuffer.Append(ch);
              if (ch >= '@' && ch <= 'G')
              {
                  string percentSeq = seqBuffer.ToString();
                  percentHandler.Handle(percentSeq);
                  seqBuffer.Clear();
                  state = ParseState.Normal;
              }
          }

          private void HandleEscOther(string escSeq)
          {
              escHandler.Handle(escSeq);
              seqBuffer.Clear();
              state = ParseState.Normal;
          }

          private void LogSequence(string label, string seq)
          {
              var hex = BitConverter.ToString(seq.Select(c => (byte)c).ToArray());
              this.LogDebug($"[{label}] \"{seq}\" | HEX: {hex}", LogLevel.Debug);
          }
    }

  /*public class CsiHandler
    {
        public void Handle(string sequence, IScreenBuffer screenBuffer)
        {
            if (sequence == "2J")
            {
                screenBuffer.ClearScreen();
                return;
            }
        }
    }*/

    public class OscHandler
    {
        public void Handle(string sequence)
        {
            this.LogDebug($"[OSC] {sequence}");
        }
    }

    public class DollarCommandHandler
    {
        public void Handle(string sequence)
        {
            this.LogDebug($"[ESC $] {sequence}");
        }
    }

    public class PercentHandler
    {
        public void Handle(string sequence)
        {
            this.LogDebug($"[ESC %] {sequence}");
        }
    }

    /*public class EscHandler
    {
        public void Handle(string sequence)
        {
            this.LogDebug($"[ESC] Okänd sekvens: {sequence}");
        }
    }*/
}