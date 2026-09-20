using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Primitives;

public sealed record ClosedPathPrimitive(
    IReadOnlyList<Point2> Vertices,
    VectorStyle Style,
    IReadOnlyList<string>? SourceIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];
}
