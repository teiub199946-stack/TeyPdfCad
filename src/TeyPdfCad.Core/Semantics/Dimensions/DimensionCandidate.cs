using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;

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
    public double? RotationRadians { get; init; }

    // Legacy provenance remains diagnostic only. P0 suppression consumes
    // recognizer-owned SourceClaims.
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];

    public IReadOnlyList<RecognizerSourceClaim> SourceClaims { get; init; } = [];
}
