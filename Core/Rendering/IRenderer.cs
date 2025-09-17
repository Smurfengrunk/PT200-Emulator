using System.Collections.Generic;

namespace PT200Emulator.Core.Rendering
{
    public interface IRenderer
    {
        void Render(IReadOnlyList<RenderDiff> diffs);
    }
}