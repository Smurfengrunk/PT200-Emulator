using PT200Emulator.Core.Emulator;
using PT200Emulator.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PT200Emulator.UI
{
    public class FixedUniformGrid : Panel
    {
        public int Rows { get; set; } = 24;
        public int Columns { get; set; } = 80;
        public Grid TerminalGrid { get; } = new Grid();

        private readonly StyleInfo _defaultStyle;

        public FixedUniformGrid(int rows, int cols, StyleInfo defaultStyle)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            if (defaultStyle == null)
            {
                this.LogWarning("Default style is null, using a fallback style.");
                defaultStyle = new StyleInfo();
            }
            Rows = rows;
            Columns = cols;
            _defaultStyle = defaultStyle.Clone();

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    var cell = new TerminalGridCell();
                    cell.SetContent(' ', _defaultStyle);
                    Children.Add(cell); // ❗️lägg till i InternalChildren, inte TerminalGrid
                }
            }

            SizeChanged += (s, e) => InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double cellW = 0, cellH = 0;

            if (Children.Count > 0)
            {
                var first = Children[0];
                // Use a finite max probe to avoid Infinity
                first.Measure(new Size(1000, 1000));

                cellW = double.IsInfinity(first.DesiredSize.Width) || double.IsNaN(first.DesiredSize.Width) ? 0 : first.DesiredSize.Width;
                cellH = double.IsInfinity(first.DesiredSize.Height) || double.IsNaN(first.DesiredSize.Height) ? 0 : first.DesiredSize.Height;
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
                var rect = new Rect(col * cellW, row * cellH, cellW, cellH);
                InternalChildren[i].Arrange(rect);
            }

            return new Size(cellW * Columns, cellH * Rows);
        }
    }
}
