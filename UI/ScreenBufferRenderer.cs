using PT200Emulator.Core.Emulator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PT200Emulator.UI
{
    public class ScreenBufferRenderer
    {
        private readonly FixedUniformGrid _grid;
        private readonly ScreenBuffer _buffer;

        public ScreenBufferRenderer(FixedUniformGrid grid, ScreenBuffer buffer)
        {
            _grid = grid;
            _buffer = buffer;
        }

        public void Render()
        {
            var cells = _grid.Children.OfType<TerminalGridCell>().ToArray();

            for (int r = 0; r < _buffer.Rows; r++)
            {
                for (int c = 0; c < _buffer.Cols; c++)
                {
                    int index = r * _buffer.Cols + c;
                    var cell = cells[index];
                    var data = _buffer[r, c];
                    
                    cell.SetContent(data.Char, data.Style);
                }
            }
        }
    }
}
