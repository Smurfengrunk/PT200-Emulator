using PT200Emulator.Core.Emulator;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace PT200Emulator.UI
{
    public class FixedUniformGrid : Panel
    {
        public int Rows { get; set; }
        public int Columns { get; set; }
        private readonly StyleInfo _defaultStyle;

        public FixedUniformGrid(int rows, int cols, StyleInfo defaultStyle)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            Rows = rows;
            Columns = cols;
            _defaultStyle = (defaultStyle ?? new StyleInfo()).Clone();

            SizeChanged += (s, e) => InvalidateMeasure();
        }

        public void InitCells()
        {
            Children.Clear();
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    var cell = new TerminalGridCell();
                    cell.SetContent(' ', _defaultStyle);
                    Children.Add(cell);
                }
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double cellW = 0, cellH = 0;

            if (Children.Count > 0)
            {
                var firstCell = Children[0];
                firstCell.Measure(new Size(1000, 1000));
                cellW = double.IsInfinity(firstCell.DesiredSize.Width) || double.IsNaN(firstCell.DesiredSize.Width) ? 0 : firstCell.DesiredSize.Width;
                cellH = double.IsInfinity(firstCell.DesiredSize.Height) || double.IsNaN(firstCell.DesiredSize.Height) ? 0 : firstCell.DesiredSize.Height;
            }

            double totalW = cellW * Columns;
            double totalH = cellH * Rows;

            if (double.IsInfinity(totalW) || double.IsNaN(totalW)) totalW = 0;
            if (double.IsInfinity(totalH) || double.IsNaN(totalH)) totalH = 0;

            if (!double.IsInfinity(availableSize.Width))
                totalW = Math.Min(totalW, availableSize.Width);
            if (!double.IsInfinity(availableSize.Height))
                totalH = Math.Min(totalH, availableSize.Height);

            return new Size(totalW, totalH);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double cellW = Columns > 0 ? finalSize.Width / Columns : 0;
            double cellH = Rows > 0 ? finalSize.Height / Rows : 0;

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                int row = i / Columns;
                int col = i % Columns;
                Rect rect = new Rect(col * cellW, row * cellH, cellW, cellH);
                InternalChildren[i].Arrange(rect);
            }

            return new Size(cellW * Columns, cellH * Rows);
        }
    }
}