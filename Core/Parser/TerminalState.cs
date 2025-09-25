using PT200Emulator.Core.Emulator;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using static PT200Emulator.Core.Parser.VisualAttributeManager;

namespace PT200Emulator.Core.Parser
{
    public class TerminalState
    {
        private VisualAttributeManager visualAttributeManager;
        public enum ScreenFormat
        {
            [Description("80 x 24")]
            S80x24,
            [Description("80 x 48")]
            S80x48,
            [Description("132 x 27")]
            S132x27,
            [Description("160 x 24")]
            S160x24
        }
        public int Cols { get; private set; }
        public int Rows { get; private set; }

        public enum DisplayType
        {
            White,
            Blue,
            Green,
            Amber,
            FullColor
        }

        // Exempel på DCS-egenskaper (lägg till alla som finns i din JSON)
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
        public ScreenBuffer ScreenBuffer { get; set; }

        public Brush DisplayTypeToBrush(DisplayType display)
        {
            switch (display)
            {
                case DisplayType.Green:
                    return Brushes.LimeGreen;
                case DisplayType.White:
                    return Brushes.White;
                case DisplayType.Amber:
                    return Brushes.Orange;
                case DisplayType.Blue:
                    return Brushes.Blue;
                default:
                    return Brushes.White;
            }
        }

        public GlyphMode GlyphMode { get; set; } = GlyphMode.Normal;

        private readonly CharTableManager _charTableManager;

        public TerminalState(CharTableManager charTableManager, string basePath)
        {
            screenFormat = ScreenFormat.S80x24; // eller default från config
            ScreenBuffer = new ScreenBuffer(24, 80, DisplayTypeToBrush(Display), Brushes.Black, basePath); // första instans
            Cols = 80;
            Rows = 24;
            _charTableManager = charTableManager;
        }

        public void SetScreenFormat()
        {
            switch (screenFormat)
            {
                case ScreenFormat.S80x24:
                    ScreenBuffer.Resize(24, 80);
                    Cols = 80;
                    Rows = 24;
                    break;
                case ScreenFormat.S80x48:
                    ScreenBuffer.Resize(48, 80);
                    Cols = 80;
                    Rows = 48;
                    break;
                case ScreenFormat.S132x27:
                    ScreenBuffer.Resize(27, 132);
                    Cols = 132;
                    Rows = 27;
                    break;
                case ScreenFormat.S160x24:
                    ScreenBuffer.Resize(24, 160);
                    Cols = 160;
                    Rows = 24;
                    break;
                default:
                    ScreenBuffer.Resize(24, 80);
                    Cols = 80;
                    Rows = 24;
                    break;
            }
        }


        // Bygger en DCS-sträng: varje gruppbyte + 0x20, separeras med "~"
        public string BuildDcs(string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var root = JsonSerializer.Deserialize<DcsBitGroupRoot>(json);
            if (root?.DCSBitGroups == null || root.DCSBitGroups.Count == 0)
                return string.Empty;

            var groupBytes = new List<byte>();

            foreach (var group in root.DCSBitGroups)
            {
                byte b = 0;

                foreach (var bit in group.Bits)
                {
                    if (string.IsNullOrEmpty(bit.Property))
                        continue;

                    // Försök hantera byte/enum-bitfält
                    if (bit.Property == "screenFormat")
                    {
                        var val = this.TryGetBitfieldByte("screenFormat");
                        if ((val & 1 << bit.Bit - 4) != 0) // skifta ner innan maskning
                            b |= (byte)(1 << bit.Bit);
                    }
                    if (bit.Property == "AuxBaudRate")
                    {
                        var val = this.TryGetBitfieldByte("AuxBaudRate");
                        if ((val & 1 << bit.Bit - 4) != 0) // skifta ner innan maskning
                            b |= (byte)(1 << bit.Bit);
                    }
                    else if (bit.Property == "Parity")
                    {
                        var val = this.TryGetBitfieldByte("Parity");
                        if ((val & 1 << bit.Bit - 2) != 0) // skifta ner 2 steg
                            b |= (byte)(1 << bit.Bit);
                    }
                    if (bit.Property is "KeyboardRepeatRate" or "HostBaudRate")
                    {
                        // Fallback-namn om JSON: “ScreenFormat” men property heter “screenFormat”
                        var propName = this.WithFallbackName(bit.Property, char.ToLowerInvariant(bit.Property[0]) + bit.Property[1..]);
                        byte val = this.TryGetBitfieldByte(propName);
                        if ((val & 1 << bit.Bit) != 0)
                            b |= (byte)(1 << bit.Bit);
                    }
                    else
                    {
                        // Booleans
                        var propName = this.WithFallbackName(bit.Property, char.ToLowerInvariant(bit.Property[0]) + bit.Property[1..]);
                        // Läs bool via reflection
                        bool flag = false;
                        var p = GetType().GetProperty(propName!, BindingFlags.Public | BindingFlags.Instance);
                        if (p != null && p.PropertyType == typeof(bool))
                            flag = (bool)(p.GetValue(this) ?? false);
                        if (flag)
                            b |= (byte)(1 << bit.Bit);
                    }
                }

                // Lägg till som en byte-array per grupp
                groupBytes.Add((byte)(b + 0x20));
            }

            // Nu grupperar vi: 5 grupper à 2 bytes, 1 grupp à 1 byte
            var grouped = new List<string>();
            for (int i = 0; i < groupBytes.Count;)
            {
                int groupSize = (i < 10) ? 2 : 1; // 5 grupper = 10 bytes, sen 1 byte
                var chunk = groupBytes.Skip(i).Take(groupSize).Select(b => (char)b);
                grouped.Add(string.Concat(chunk));
                i += groupSize;
            }

            var payload = string.Join("~", grouped); // 6 grupper, 5 ~
            var response = "\x1B" + "P" + payload + "\x1B" + "\\";

            // Validera längd
            if (response.Length != 20)
                this.LogWarning($"DCS-responsen är {response.Length} byte – måste vara exakt 20!");

            return response;
        }

        // Läser en DCS-sträng: subtraherar 0x20 och applicerar på state
        public void ReadDcs(string jsonPath, string dcsString)
        {
            var json = File.ReadAllText(jsonPath);
            var root = JsonSerializer.Deserialize<DcsBitGroupRoot>(json);
            if (root?.DCSBitGroups == null) return;

            var groups = dcsString.Split('~');
            var bitCache = new Dictionary<string, int>();

            for (int i = 0; i < root.DCSBitGroups.Count && i < groups.Length; i++)
            {
                if (string.IsNullOrEmpty(groups[i]))
                    continue;

                // Här definierar vi readable
                byte readable = (byte)groups[i][0]; // första tecknet i gruppen
                if (readable < 0x20)
                    continue; // skydd mot ogiltiga värden

                // Ta bort offset
                byte b = (byte)(readable - 0x20);

                foreach (var bit in root.DCSBitGroups[i].Bits)
                {
                    if (string.IsNullOrEmpty(bit.Property))
                        continue;

                    bool isSet = (b & 1 << bit.Bit) != 0;

                    // Hantera bitfält (byte/enum) separat
                    if (bit.Property == "screenFormat")
                    {
                        if (!bitCache.ContainsKey("screenFormat"))
                            bitCache["screenFormat"] = 0;

                        if (isSet)
                            bitCache["screenFormat"] |= 1 << bit.Bit - 4; // skifta ner
                    }
                    if (bit.Property == "AuxBaudRate")
                    {
                        if (!bitCache.ContainsKey("AuxBaudRate"))
                            bitCache["AuxBaudRate"] = 0;

                        if (isSet)
                            bitCache["AuxBaudRate"] |= 1 << bit.Bit - 4; // skifta ner
                    }
                    else if (bit.Property == "Parity")
                    {
                        if (!bitCache.ContainsKey("Parity"))
                            bitCache["Parity"] = 0;

                        if (isSet)
                            bitCache["Parity"] |= 1 << bit.Bit - 2; // skifta ner 2 steg
                    }
                    if (bit.Property is "KeyboardRepeatRate" or "HostBaudRate")
                    {
                        if (!bitCache.ContainsKey(bit.Property))
                            bitCache[bit.Property] = 0;

                        // För screenFormat i ditt JSON-exempel börjar bitarna vid 4
                        if (bit.Property == "screenFormat")
                            bitCache[bit.Property] |= isSet ? 1 << bit.Bit - 4 : 0;
                        else
                            bitCache[bit.Property] |= isSet ? 1 << bit.Bit : 0;
                    }
                    else
                    {
                        this.TrySetBool(bit.Property, isSet);
                    }
                }
            }

            // Sätt alla bitfält efter att vi läst klart
            foreach (var kvp in bitCache)
            {
                this.TrySetBitfieldByte(kvp.Key, (byte)kvp.Value);
            }
        }

        public string GetCurrentDcsStringForDebug(string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var root = JsonSerializer.Deserialize<DcsBitGroupRoot>(json);
            if (root?.DCSBitGroups == null || root.DCSBitGroups.Count == 0)
                return string.Empty;

            var parts = new List<string>();

            foreach (var group in root.DCSBitGroups)
            {
                byte b = 0;

                foreach (var bit in group.Bits)
                {
                    if (string.IsNullOrEmpty(bit.Property))
                        continue;

                    if (bit.Property is "ScreenFormat" or "KeyboardRepeatRate" or "HostBaudRate" or "AuxBaudRate" or "Parity")
                    {
                        byte val = this.TryGetPropertyValue<byte>(bit.Property);
                        if ((val & 1 << bit.Bit) != 0)
                            b |= (byte)(1 << bit.Bit);
                    }
                    else
                    {
                        bool val = this.TryGetPropertyValue<bool>(bit.Property);
                        if (val)
                            b |= (byte)(1 << bit.Bit);
                    }
                }

                // Gör byte läsbar genom att addera 0x20
                byte readable = (byte)(b + 0x20);
                parts.Add(((char)readable).ToString());
            }

            return string.Join("~", parts);
        }

        public (int Columns, int Rows) GetDimensions()
        {
            return screenFormat switch
            {
                ScreenFormat.S80x24 => (80, 24),
                ScreenFormat.S80x48 => (80, 48),
                ScreenFormat.S132x27 => (132, 27),
                ScreenFormat.S160x24 => (160, 24),
                _ => (80, 24)
            };
        }

        public char TranslateGlyph(byte code)
        {
            return GlyphMode switch
            {
                GlyphMode.LineDrawing => _charTableManager.GetGlyphFromTable("G1", code),
                GlyphMode.BlockDrawing => _charTableManager.GetGlyphFromTable("G1", code), // eller annan tabell
                _ => _charTableManager.Translate(code)
            };
        }
    }

    public static class ReflectionExtensions
    {
        public static void TrySetPropertyValue<T>(this object target, string propertyName, T value)
        {
            if (target == null || string.IsNullOrEmpty(propertyName))
                return;

            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(T))
            {
                prop.SetValue(target, value);
            }
        }

        public static T TryGetPropertyValue<T>(this object target, string propertyName)
        {
            if (target == null || string.IsNullOrEmpty(propertyName))
                return default!;

            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead && prop.PropertyType == typeof(T))
            {
                var value = prop.GetValue(target);
                if (value is T typedValue)
                    return typedValue;
            }

            return default!;
        }

        // Sätter bool direkt
        public static void TrySetBool(this object target, string propertyName, bool value)
        {
            if (target == null || string.IsNullOrEmpty(propertyName)) return;
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
                prop.SetValue(target, value);
        }

        // Läser ett bitfält som byte, oavsett om prop är byte eller enum
        public static byte TryGetBitfieldByte(this object target, string propertyName)
        {
            if (target == null || string.IsNullOrEmpty(propertyName)) return 0;
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanRead) return 0;

            var t = prop.PropertyType;
            var value = prop.GetValue(target);
            if (value == null) return 0;

            if (t == typeof(byte)) return (byte)value;
            if (t.IsEnum) return Convert.ToByte(value);
            return 0;
        }

        // Sätter ett bitfält från byte, oavsett om prop är byte eller enum
        public static void TrySetBitfieldByte(this object target, string propertyName, byte value)
        {
            if (target == null || string.IsNullOrEmpty(propertyName)) return;
            var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) return;

            var t = prop.PropertyType;
            if (t == typeof(byte))
            {
                prop.SetValue(target, value);
            }
            else if (t.IsEnum)
            {
                var enumValue = Enum.ToObject(t, value);
                prop.SetValue(target, enumValue);
            }
        }

        // Hjälpare: fallback till annat namn (t.ex. “ScreenFormat” -> “screenFormat”)
        public static string WithFallbackName(this object target, string primaryName, string fallbackName = null)
        {
            if (string.IsNullOrEmpty(primaryName)) return fallbackName;
            var prop = target.GetType().GetProperty(primaryName, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) return primaryName;
            return fallbackName;
        }
    }

    public class DcsBitGroupRoot
    {
        public List<DcsBitGroup> DCSBitGroups { get; set; }
    }

    public class DcsBitMapping
    {
        public int Bit { get; set; }
        public string Property { get; set; }
        public string Description { get; set; }
    }

    public class DcsBitGroup
    {
        public int Group { get; set; }
        public int ByteIndex { get; set; }
        public List<DcsBitMapping> Bits { get; set; } = new();
    }

    public static class EnumHelper
    {
        public static string GetDescription(Enum value)
        {
            var fi = value.GetType().GetField(value.ToString());
            var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes.Length > 0 ? attributes[0].Description : value.ToString();
        }
    }
}