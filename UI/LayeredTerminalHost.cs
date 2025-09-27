using PT200Emulator.Core.Emulator;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PT200Emulator.UI
{
    public class LayeredTerminalHost : Grid
    {
        public FixedUniformGrid TerminalGrid { get; private set; }
        public Canvas OverlayCanvas { get; private set; }
        public TextBox InputOverlay { get; private set; }
        public Rectangle CaretVisual { get; private set; }

        private readonly StyleInfo _defaultStyle;

        public int Rows => TerminalGrid?.Rows ?? 24;
        public int Columns => TerminalGrid?.Columns ?? 80;

        public LayeredTerminalHost(StyleInfo style)
        {
            _defaultStyle = style?.Clone() ?? new StyleInfo();
            InitializeLayers();
            WireEvents();
        }

        private void InitializeLayers()
        {
            Children.Clear();

            TerminalGrid = new FixedUniformGrid(24, 80, _defaultStyle);
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

            OverlayCanvas = new Canvas { IsHitTestVisible = false };
            OverlayCanvas.Children.Add(CaretVisual);
            ZIndexManager.Apply(OverlayCanvas, TerminalLayer.DebugOverlay);

            InputOverlay = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = true,
                IsHitTestVisible = true,
                IsTabStop = false,
                AcceptsReturn = false,
                AcceptsTab = false,
                Opacity = 1.0
            };
            ZIndexManager.Apply(InputOverlay, TerminalLayer.InputOverlay);

            Children.Add(TerminalGrid);
            Children.Add(OverlayCanvas);
            Children.Add(InputOverlay);
        }

        private void WireEvents()
        {
            // Keep overlay sized to the terminal
            SizeChanged += (_, __) => SyncOverlaySize();
            LayoutUpdated += (_, __) => SyncOverlaySize();

            // Route input
            InputOverlay.PreviewKeyDown += InputOverlay_PreviewKeyDown;
            InputOverlay.TextInput += InputOverlay_TextInput;
        }

        private void SyncOverlaySize()
        {
            double w = TerminalGrid.ActualWidth;
            double h = TerminalGrid.ActualHeight;
            if (w > 0 && h > 0)
            {
                InputOverlay.Width = w;
                InputOverlay.Height = h;

                // Ensure caret stays within bounds when size changes
                if (CaretVisual.Visibility == Visibility.Visible)
                    UpdateCaretVisual(CurrentCaretRow, CurrentCaretCol);
            }
        }

        // Simple caret state; replace with your TerminalCaretController if preferred
        public int CurrentCaretRow { get; private set; } = 0;
        public int CurrentCaretCol { get; private set; } = 0;

        public void ShowCaret(int row, int col)
        {
            CurrentCaretRow = Clamp(row, 0, Rows - 1);
            CurrentCaretCol = Clamp(col, 0, Columns - 1);
            CaretVisual.Visibility = Visibility.Visible;
            UpdateCaretVisual(CurrentCaretRow, CurrentCaretCol);
        }

        public void HideCaret()
        {
            CaretVisual.Visibility = Visibility.Collapsed;
        }

        public void MoveCaret(int deltaRow, int deltaCol)
        {
            ShowCaret(CurrentCaretRow + deltaRow, CurrentCaretCol + deltaCol);
        }

        private void UpdateCaretVisual(int row, int col)
        {
            var cell = GetCellRect(row, col);
            // A flat caret at the bottom of the cell
            double height = Math.Max(2, cell.Height * 0.1);
            CaretVisual.Width = Math.Max(2, cell.Width);
            CaretVisual.Height = height;

            Canvas.SetLeft(CaretVisual, cell.X);
            Canvas.SetTop(CaretVisual, cell.Y + cell.Height - height);
        }

        private Rect GetCellRect(int row, int col)
        {
            double w = TerminalGrid.ActualWidth;
            double h = TerminalGrid.ActualHeight;
            if (w <= 0 || h <= 0 || Rows <= 0 || Columns <= 0)
                return new Rect(0, 0, 0, 0);

            double cellW = w / Columns;
            double cellH = h / Rows;
            return new Rect(col * cellW, row * cellH, cellW, cellH);
        }

        private static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        private void InputOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Example navigation; integrate with emulator
            switch (e.Key)
            {
                case Key.Left:
                    MoveCaret(0, -1); e.Handled = true; break;
                case Key.Right:
                    MoveCaret(0, 1); e.Handled = true; break;
                case Key.Up:
                    MoveCaret(-1, 0); e.Handled = true; break;
                case Key.Down:
                    MoveCaret(1, 0); e.Handled = true; break;
                case Key.Back:
                    MoveCaret(0, -1); e.Handled = true; break;
                case Key.Delete:
                    e.Handled = true; break;
                case Key.Tab:
                    e.Handled = true; break;
            }
        }

        private void InputOverlay_TextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;

            // Push to emulator; for now, write into the current cell
            var idx = CurrentCaretRow * Columns + CurrentCaretCol;
            if (idx >= 0 && idx < TerminalGrid.Children.Count)
            {
                if (TerminalGrid.Children[idx] is TerminalGridCell cell)
                {
                    cell.SetContent(e.Text[0], _defaultStyle);
                    MoveCaret(0, 1);
                }
            }
            e.Handled = true;
        }

        public enum TerminalLayer
        {
            Cells = 0,
            InputOverlay = 10,
            Caret = 20,
            DebugOverlay = 30,
            Modal = 100
        }
    }
}