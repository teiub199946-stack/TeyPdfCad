using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Semantics.Dimensions;

public enum DimensionKind
{
    Rotated,
    Aligned
}

public sealed record DimensionCandidate(
    DimensionKind Kind,
    Point2 DefinitionPoint1,
    Point2 DefinitionPoint2,
    Point2 DimensionLinePoint,
    double DisplayedValue,
    double ReconstructedMeasurement,
    double DrawingScale,
    double Confidence,
    string SourceText,
    double ArrowEvidence,
    IReadOnlyList<string>? SourcePrimitiveIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];
}
