using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Semantics;

public sealed record HatchCandidate(
    IReadOnlyList<Point2> Boundary,
    bool IsSolid,
    double? PatternAngleRadians,
    double? PatternSpacingMillimetres,
    VectorStyle Style,
    double Confidence,
    IReadOnlyList<string>? SourceEntityIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceEntityIds ?? [];
}
