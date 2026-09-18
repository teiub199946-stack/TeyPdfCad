namespace TeyPdfCad.Core.Primitives;

using TeyPdfCad.Core.Sheets;

public sealed class PrimitiveScene
{
    public List<LinePrimitive> Lines { get; } = [];
    public List<TextPrimitive> Texts { get; } = [];
    public SheetMetadata? Sheet { get; set; }
    public TitleBlockMetadata? TitleBlock { get; set; }
}
