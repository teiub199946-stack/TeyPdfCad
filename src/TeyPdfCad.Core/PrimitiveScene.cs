using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core;

public sealed class PrimitiveScene
{
    public List<LinePrimitive> Lines { get; } = new();
    public List<TextPrimitive> Texts { get; } = new();
}
