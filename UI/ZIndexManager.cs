using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PT200Emulator.UI
{
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
