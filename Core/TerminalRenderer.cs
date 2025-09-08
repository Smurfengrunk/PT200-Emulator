using PT200Emulator.Core;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PT200Emulator.Core
{
    public class TerminalRenderer
    {
        // Mått för teckenceller (justera till din font/metrics)
        private readonly double cellWidth = 8;
        private readonly double cellHeight = 16;
        private readonly double fontSize = 14;

        private readonly Typeface typeface = new Typeface("Consolas");
        private readonly GlyphTypeface glyphTypeface;

        // Cache per Canvas → en VisualHost som håller vårt DrawingVisual
        private readonly Dictionary<Canvas, DrawingVisualHost> hosts = new();

        public TerminalRenderer()
        {
            if (!typeface.TryGetGlyphTypeface(out glyphTypeface))
                throw new InvalidOperationException("Kunde inte ladda GlyphTypeface för vald font.");
        }

        // Drop-in: samma signatur som din gamla, men ritar allt i ett DrawingVisual
        public void Render(Canvas canvas, ScreenBuffer buffer, PT200State state)
        {
            if (canvas == null || buffer == null) return;

            // Hämta/skap host för canvasen (så vi slipper skapa nya UI-element varje frame)
            if (!hosts.TryGetValue(canvas, out var host))
            {
                host = new DrawingVisualHost();
                hosts[canvas] = host;
                canvas.Children.Clear(); // rensa eventuella gamla barn
                canvas.Children.Add(host);
            }

            // Set canvas size enbart när dimensioner ändras (valfritt)
            double desiredW = buffer.Cols * cellWidth;
            double desiredH = buffer.Rows * cellHeight;
            if (!DoubleUtil.AreClose(canvas.Width, desiredW)) canvas.Width = desiredW;
            if (!DoubleUtil.AreClose(canvas.Height, desiredH)) canvas.Height = desiredH;

            // DPI för HiDPI-stöd
            double pixelsPerDip = VisualTreeHelper.GetDpi(canvas).PixelsPerDip;

            // Rita om visualen
            using (var dc = host.Visual.RenderOpen())
            {
                RenderToDrawingContext(dc, buffer, state, pixelsPerDip);
            }
        }

        // Kärnlogik: rita direkt till ett DrawingContext
        public void RenderToDrawingContext(DrawingContext dc, ScreenBuffer buffer, PT200State state, double pixelsPerDip)
        {
            if (dc == null || buffer == null) return;

            // Valfritt: fyll hela bakgrunden en gång (svart), så slipper man null-koll på bg
            // dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, buffer.Cols * cellWidth, buffer.Rows * cellHeight));

            for (int row = 0; row < buffer.Rows; row++)
            {
                for (int col = 0; col < buffer.Cols; col++)
                {
                    var cell = buffer.GetCell(row, col);
                    // Rita bakgrund om cellen har en (eller om du valt att inte fylla globalt)
                    var bg = buffer.GetBrushForColor(cell.BackgroundColor);
                    if (bg != null)
                    {
                        dc.DrawRectangle(
                            bg,
                            null,
                            new Rect(col * cellWidth, row * cellHeight, cellWidth, cellHeight));
                    }

                    // Hoppa över “tomt” tecken efter bakgrund (bakgrunden syns ändå)
                    if (cell.Character == '\0') continue;

                    // Hämta glyph-index för tecknet
                    if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(cell.Character, out ushort glyphIndex))
                    {
                        // Fallback
                        glyphIndex = glyphTypeface.CharacterToGlyphMap.TryGetValue('?', out var q) ? q : (ushort)0;
                    }

                    // Förgrundsborste från cellens färg
                    var fg = buffer.GetBrushForColor(cell.ForegroundColor) ?? Brushes.White;

                    // Rita en glyphRun för ett tecken (enkel, tydlig variant)
                    var glyphRun = new GlyphRun(
                        glyphTypeface,
                        0,                  // bidiLevel
                        false,              // isSideways
                        fontSize,           // em-size
                        (float)pixelsPerDip,// HiDPI-korrekt
                        new ushort[] { glyphIndex },
                        // Baseline – justera offset (-3) vid behov för din font/metrics
                        new Point(col * cellWidth, row * cellHeight + cellHeight - 3),
                        new double[] { cellWidth },
                        null, null, null, null, null, null);

                    dc.DrawGlyphRun(fg, glyphRun);
                }
            }

            // Cursor-overlay
            if (state != null && state.cursorVisible)
            {
                var cursorRect = new Rect(buffer.CursorCol * cellWidth, buffer.CursorRow * cellHeight, cellWidth, cellHeight);
                var cursorBrush = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128));
                dc.DrawRectangle(cursorBrush, null, cursorRect);
            }
        }

        // Liten hjälpare för double-jämförelse (undvik att uppdatera Canvas-mått i onödan)
        private static class DoubleUtil
        {
            public static bool AreClose(double a, double b)
            {
                const double Epsilon = 0.0001;
                return Math.Abs(a - b) < Epsilon;
            }
        }

        // Host-element som exponerar ett DrawingVisual i en FrameworkElement så att det kan ligga i en Canvas
        private sealed class DrawingVisualHost : FrameworkElement
        {
            public DrawingVisual Visual { get; } = new DrawingVisual();
            private readonly VisualCollection visuals;

            public DrawingVisualHost()
            {
                visuals = new VisualCollection(this) { Visual };
            }

            protected override int VisualChildrenCount => visuals.Count;
            protected override Visual GetVisualChild(int index) => visuals[index];
        }
    }

public class ControlCharacterHandler
    {
        public event Action<byte[]> RawOutput;
        public event Action BreakReceived;
        public event Action BellReceived;

        public enum ControlCharacterResult
        {
            NotHandled,
            RawOutput,
            Break,
            Bell,
            Abort,
            FormFeed,
            Null,
            LineFeed,
            CarriageReturn
        }
        public byte[] LastRawBytes { get; private set; }

        public ControlCharacterResult Handle(char ch)
        {
            LastRawBytes = new byte[] { (byte)ch };

            return ch switch
            {
                (char)0x07 => ControlCharacterResult.Bell,
                (char)0x10 => ControlCharacterResult.Break,
                (char)0x03 => ControlCharacterResult.Abort,
                (char)0x0C => ControlCharacterResult.FormFeed,
                (char)0x00 => ControlCharacterResult.Null,
                (char)0x0A => ControlCharacterResult.LineFeed,     // LF
                (char)0x0D => ControlCharacterResult.CarriageReturn, // CR
                _ => ControlCharacterResult.NotHandled
            };
        }
        // För att undvika varningar om oanvända händelser
        private void TouchEvents()
        {
            _ = RawOutput;
            _ = BreakReceived;
            _ = BellReceived;
        }

    }

    public class TerminalVisualHost : FrameworkElement
    {
        public ScreenBuffer Buffer { get; set; }
        public PT200State State { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            if (Buffer == null) return;
            // Rita hela buffern här med dc.DrawRectangle / dc.DrawGlyphRun
        }
    }

    // Enkel host för att kunna lägga till DrawingVisual i Canvas
    public class DrawingVisualHost : FrameworkElement
    {
        private readonly Visual _visual;
        public DrawingVisualHost(Visual visual) => _visual = visual;
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;
    }
}