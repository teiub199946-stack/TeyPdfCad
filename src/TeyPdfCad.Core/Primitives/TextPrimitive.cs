using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Primitives;

public sealed record TextPrimitive(
    string Value,
    Point2 Position,
    double Height,
    double Rotation,
    string? Layer = null,
    IReadOnlyList<string>? SourceIds = null,
    int? RgbColor = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];
}
