using PT200Emulator.Util;
using System.Text;

namespace PT200Emulator.Core
{
    public class PT200State
    {
        public enum ScreenFormat
        {
            S80x24,
            S80x48,
            S132x27,
            S160x24
        }

        public enum DisplayType
        {
            White,
            Blue,
            Green,
            Amber,
            FullColor
        }

        // =========================
        // Grupp 1 – Terminalstatus
        // =========================
        public bool IsOnline { get; set; } = true;
        public bool IsBlockMode { get; set; } = false;
        public bool IsLineMode { get; set; } = true;
        public bool IsSoftScroll { get; set; } = false;
        public ScreenFormat screenFormat { get; set; } = ScreenFormat.S80x24;

        // =========================
        // Grupp 2 – Tangentbordsinställningar
        // =========================
        public byte KeyboardRepeatRate { get; set; } = 0b010; // 10 cps / kort delay
        public bool KeyboardClick { get; set; } = false;
        public bool ReverseVideo { get; set; } = false;
        public bool ControlRepresentation { get; set; } = false;
        public bool DscMode { get; set; } = false;
        public bool SoftLock { get; set; } = false;
        public bool FunctionTermination { get; set; } = false;
        public bool SendTabs { get; set; } = true;
        public bool FunctionKeypad { get; set; } = false;
        public bool MarginBell { get; set; } = false;

        // =========================
        // Grupp 3 – Kommunikationsparametrar
        // =========================
        public byte HostBaudRate { get; set; } = 0b1110; // 9600 bps
        public byte AuxBaudRate { get; set; } = 0b1110; // 9600 bps
        public bool TwoStopBits { get; set; } = false;
        public bool Rollover { get; set; } = false;
        public byte Parity { get; set; } = 0b100; // None (8-bit)

        // =========================
        // Grupp 4 – Visningsattribut
        // =========================
        public bool IsColor { get; set; } = false;
        public bool CursorBlink { get; set; } = true;

        // =========================
        // Grupp 5 – Statusfält
        // =========================
        public bool StatusBarVisible { get; set; } = true;

        // =========================
        // Grupp 6 – Övriga terminalinställningar
        // =========================
        public bool AutoLineFeed { get; set; } = false;
        public bool LogicalAttributes { get; set; } = false;
        public bool UseSpaceAsPad { get; set; } = false;
        public bool ScreenWrap { get; set; } = false;
        public bool TransmitModifiedOnly { get; set; } = false;
        public bool TwoPageBoundary { get; set; } = false;

        // =========================
        // Övrigt (ej direkt i DCS)
        // =========================
        public DisplayType Display { get; set; } = DisplayType.Green;
        public bool PrintMode { get; set; }

        // =========================
        // Generering av DCS-sträng
        // =========================
        public string GenerateDCSResponse()
        {
            var sb = new StringBuilder();
            sb.Append("\x1B").Append("P");

            var groups = new List<byte[]>
            {
                BuildGroup1(),
                BuildGroup2(),
                BuildGroup3(),
                BuildGroup4(),
                BuildGroup5(),
                BuildGroup6()
            };

            foreach (var group in groups)
            {
                foreach (var b in group)
                    sb.Append((char)b);
                sb.Append("~");
            }

            sb.Length--; // ta bort sista ~
            sb.Append("\x1B\\");
            return sb.ToString();
        }

        // =========================
        // Grupp 1
        // =========================
        public byte[] BuildGroup1()
        {
            byte b1 = 0x00, b2 = 0x00;
            b1 |= (byte)((IsOnline ? 1 : 0) << 0);
            b1 |= (byte)((IsBlockMode ? 1 : 0) << 1);
            b1 |= (byte)((IsLineMode ? 1 : 0) << 2);
            b1 |= (byte)((int)screenFormat << 3);
            b1 |= (byte)((IsSoftScroll ? 1 : 0) << 5);

            b2 |= (0b0001 << 1); // ANSI emulation

            return [(byte)(b1 + 0x20), (byte)(b2 + 0x20)];
        }

        // =========================
        // Grupp 2
        // =========================
        public byte[] BuildGroup2()
        {
            byte b1 = 0x00, b2 = 0x00;

            b1 |= (byte)((KeyboardRepeatRate & 0b111) << 3);
            b1 |= (byte)((KeyboardClick ? 1 : 0) << 2);
            b1 |= (byte)((ReverseVideo ? 1 : 0) << 1);
            b1 |= (byte)((ControlRepresentation ? 1 : 0) << 0);

            b2 |= (byte)((DscMode ? 1 : 0) << 5);
            b2 |= (byte)((SoftLock ? 1 : 0) << 4);
            b2 |= (byte)((FunctionTermination ? 1 : 0) << 3);
            b2 |= (byte)((SendTabs ? 1 : 0) << 2);
            b2 |= (byte)((FunctionKeypad ? 1 : 0) << 1);
            b2 |= (byte)((MarginBell ? 1 : 0) << 0);

            return [(byte)(b1 + 0x20), (byte)(b2 + 0x20)];
        }

        // =========================
        // Grupp 3
        // =========================
        public byte[] BuildGroup3()
        {
            byte b1 = 0x00, b2 = 0x00, b3 = 0x00;

            // Byte 1 – Host baud rate
            b1 |= (byte)((HostBaudRate & 0b1111) << 0);

            // Byte 2 – Aux baud rate
            b2 |= (byte)((AuxBaudRate & 0b1111) << 0);

            // Byte 3 – Stopbitar, rollover, paritet, duplex
            b3 |= (byte)((TwoStopBits ? 1 : 0) << 5);
            b3 |= (byte)((Rollover ? 1 : 0) << 4);
            b3 |= (byte)((Parity & 0b111) << 1);
            b3 |= (byte)(1 << 0); // alltid full duplex

            return [(byte)(b1 + 0x20), (byte)(b2 + 0x20), (byte)(b3 + 0x20)];
        }

        // =========================
        // Grupp 4
        // =========================
        public byte[] BuildGroup4()
        {
            byte b1 = 0x00, b2 = 0x00; // b2 används inte
            if (IsColor) b1 |= (1 << 2);
            if (CursorBlink) b1 |= (1 << 3);

            return [(byte)(b1 + 0x20), (byte)(b2 + 0x20)];
        }

        // =========================
        // Grupp 5
        // =========================
        public byte[] BuildGroup5()
        {
            byte b1 = 0x00, b2 = 0x00; // b2 behövs inte
            b1 = (byte)((StatusBarVisible ? 1 : 0) << 0);
            return [(byte)(b1 + 0x20), (byte)(b2 + 0x20)];
        }

        // =========================
        // Grupp 6
        // =========================
        public byte[] BuildGroup6()
        {
            byte b1 = 0x00, b2 = 0x00;
            b1 |= (byte)((AutoLineFeed ? 1 : 0) << 0);
            b1 |= (byte)((LogicalAttributes ? 1 : 0) << 1);
            b1 |= (byte)((UseSpaceAsPad ? 1 : 0) << 2);
            b1 |= (byte)((ScreenWrap ? 1 : 0) << 3);
            b1 |= (byte)((TransmitModifiedOnly ? 1 : 0) << 4);
            b1 |= (byte)((TwoPageBoundary ? 1 : 0) << 5);

            return [(byte)(b1 + 0x20), (byte)(b2 + 0x20)];
        }

        public void DumpDcsDebug()
        {
            var groups = new List<byte[]>
            {
                BuildGroup1(),
                BuildGroup2(),
                BuildGroup3(),
                BuildGroup4(),
                BuildGroup5(),
                BuildGroup6()
            };

            Logger.Log("=== DCS DEBUG ===", Logger.LogLevel.Debug);

            for (int g = 0; g < groups.Count; g++)
            {
                Logger.Log($"Grupp {g + 1}:", Logger.LogLevel.Debug);

                for (int b = 0; b < groups[g].Length; b++)
                {
                    byte raw = (byte)(groups[g][b] - 0x20); // ta bort offset
                    string bin = Convert.ToString(raw, 2).PadLeft(8, '0');
                    Logger.Log($"  Byte {b + 1}: 0x{raw:X2} ({bin})", Logger.LogLevel.Debug);

                    // Bit-tolkning per grupp
                    switch (g + 1)
                    {
                        case 1:
                            if (b == 1)
                            {
                                Logger.Log($"    Bit0: IsOnline = {IsOnline}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit1: IsBlockMode = {IsBlockMode}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit2: IsLineMode = {IsLineMode}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit3-4: ScreenFormat = {screenFormat}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit5: IsSoftScroll = {IsSoftScroll}", Logger.LogLevel.Debug);
                            }
                            break;

                        case 2:
                            if (b == 1)
                            {
                                Logger.Log($"    Bit3-5: KeyboardRepeatRate = {KeyboardRepeatRate}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit2: KeyboardClick = {KeyboardClick}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit1: ReverseVideo = {ReverseVideo}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit0: ControlRepresentation = {ControlRepresentation}", Logger.LogLevel.Debug);
                            }
                            else if (b == 2)
                            {
                                Logger.Log($"    Bit5: DscMode = {DscMode}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit4: SoftLock = {SoftLock}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit3: FunctionTermination = {FunctionTermination}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit2: SendTabs = {SendTabs}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit1: FunctionKeypad = {FunctionKeypad}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit0: MarginBell = {MarginBell}", Logger.LogLevel.Debug);
                            }
                            break;

                        case 3:
                            if (b == 1)
                                Logger.Log($"    HostBaudRate = {HostBaudRate}", Logger.LogLevel.Debug);
                            else if (b == 2)
                                Logger.Log($"    AuxBaudRate = {AuxBaudRate}", Logger.LogLevel.Debug);
                            else if (b == 3)
                            {
                                Logger.Log($"    Bit5: TwoStopBits = {TwoStopBits}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit4: Rollover = {Rollover}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit1-3: Parity = {Parity}", Logger.LogLevel.Debug);
                                Logger.Log("    Bit0: Duplex = Full", Logger.LogLevel.Debug);
                            }
                            break;

                        case 4:
                            if (b == 1)
                            {
                                Logger.Log($"    Bit2: IsColor = {IsColor}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit3: CursorBlink = {CursorBlink}", Logger.LogLevel.Debug);
                            }
                            break;

                        case 5:
                            if (b == 1)
                                Logger.Log($"    Bit0: StatusBarVisible = {StatusBarVisible}", Logger.LogLevel.Debug);
                            break;

                        case 6:
                            if (b == 1)
                            {
                                Logger.Log($"    Bit0: AutoLineFeed = {AutoLineFeed}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit1: LogicalAttributes = {LogicalAttributes}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit2: UseSpaceAsPad = {UseSpaceAsPad}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit3: ScreenWrap = {ScreenWrap}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit4: TransmitModifiedOnly = {TransmitModifiedOnly}", Logger.LogLevel.Debug);
                                Logger.Log($"    Bit5: TwoPageBoundary = {TwoPageBoundary}", Logger.LogLevel.Debug);
                            }
                            break;
                    }
                }
            }

            Logger.Log("=================", Logger.LogLevel.Debug);

            // Logga hela DCS-strängen i hex + ASCII
            var dcsBytes = Encoding.ASCII.GetBytes(GenerateDCSResponse());
            Logger.LogHex(dcsBytes, dcsBytes.Length, "TX");

        }
    }
}