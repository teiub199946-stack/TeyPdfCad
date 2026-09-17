namespace TeyPdfCad.Core.Primitives;

public sealed class PrimitiveScene
{
    public List<LinePrimitive> Lines { get; } = [];
    public List<TextPrimitive> Texts { get; } = [];
}
