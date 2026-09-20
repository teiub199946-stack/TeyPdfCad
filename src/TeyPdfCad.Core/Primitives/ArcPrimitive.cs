using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Primitives;

public sealed record ArcPrimitive(
    Point2 Center,
    double Radius,
    double StartAngleRadians,
    double EndAngleRadians,
    string? Layer = null,
    IReadOnlyList<string>? SourceIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];
}
