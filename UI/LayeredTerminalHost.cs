using PT200Emulator.Core.Emulator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PT200Emulator.UI
{
    public class LayeredTerminalHost : Grid
    {
        public FixedUniformGrid TerminalGrid { get; private set; }
        public Canvas OverlayCanvas { get; private set; }
        public TextBox InputOverlay { get; private set; }
        public Rectangle CaretVisual { get; private set; }
        private StyleInfo _defaultStyle;

        public enum TerminalLayer
        {
            Cells = 0,
            InputOverlay = 10,
            Caret = 20,
            DebugOverlay = 30,
            Modal = 100
        }

        public LayeredTerminalHost(StyleInfo style)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            _defaultStyle = style.Clone();
            // Default initialization with 24 rows and 80 columns
            Initialize(24, 80);
        }

        public void Initialize(int rows, int columns)
        {
            Children.Clear();
            TerminalGrid = new FixedUniformGrid(rows, columns, _defaultStyle);

            ZIndexManager.Apply(TerminalGrid, TerminalLayer.Cells);

            CaretVisual = new Rectangle
            {
                Width = 8,
                Height = 2,
                Fill = Brushes.LimeGreen,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            ZIndexManager.Apply(CaretVisual, TerminalLayer.Caret);

            OverlayCanvas = new Canvas
            {
                IsHitTestVisible = false
            };
            OverlayCanvas.Children.Add(CaretVisual);
            ZIndexManager.Apply(OverlayCanvas, TerminalLayer.DebugOverlay);

            InputOverlay = new TextBox
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                BorderBrush = Brushes.Red,
                BorderThickness = new Thickness(1),
                Focusable = true,
                IsHitTestVisible = true,
                Opacity = 10
            };
            ZIndexManager.Apply(InputOverlay, TerminalLayer.InputOverlay);

            Children.Add(TerminalGrid);
            Children.Add(OverlayCanvas);
            Children.Add(InputOverlay);
        }
    }

    public static class ZIndexManager
    {
        public const int Cells = 0;
        public const int InputOverlay = 10;
        public const int Caret = 20;
        public const int DebugOverlay = 30;
        public const int Modal = 100;

        public static void Apply(UIElement element, LayeredTerminalHost.TerminalLayer layer)
        {
            Panel.SetZIndex(element, (int)layer);
        }

        public static void ApplyNamed(UIElement element, string name)
        {
            int layer = name switch
            {
                "Cells" => Cells,
                "InputOverlay" => InputOverlay,
                "Caret" => Caret,
                "DebugOverlay" => DebugOverlay,
                "Modal" => Modal,
                _ => 0
            };

            Panel.SetZIndex(element, layer);
        }
    }
}
