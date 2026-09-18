using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Sheets;

public sealed record TitleBlockRegion(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    IReadOnlyList<string>? SourceIds = null)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];

    public bool Contains(Point2 point)
        => point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
}

public sealed record TitleBlockField(
    TitleBlockFieldKind Kind,
    string Value,
    Point2 Position,
    double Height,
    double Rotation,
    IReadOnlyList<string>? SourceIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];
}

public sealed record TitleBlockMetadata(
    TitleBlockRegion Region,
    IReadOnlyList<TitleBlockField> Fields,
    bool IsCandidate = true)
{
    public IReadOnlyList<LinePrimitive> Lines { get; init; } = [];
}
