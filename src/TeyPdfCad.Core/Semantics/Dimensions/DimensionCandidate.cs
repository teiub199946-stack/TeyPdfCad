using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Core.Semantics.Dimensions;

public enum DimensionKind
{
    Rotated,
    Aligned
}

public sealed record DimensionSourceLineAppearance(
    Point2 Start,
    Point2 End,
    string? Layer,
    int? RgbColor,
    double? StrokeWidthMm,
    IReadOnlyList<double> DashPatternMm,
    IReadOnlyList<string> SourceIds)
{
    public bool IsCompositeObservation => SourceIds.Count != 1;
}

public sealed record DimensionSourceTextAppearance(
    string Value,
    Point2 Position,
    double HeightMm,
    double RotationDegrees,
    string? Layer,
    int? RgbColor,
    IReadOnlyList<string> SourceIds,
    string? FontName = null);

public sealed record DimensionSourceAppearance(
    DimensionSourceTextAppearance Text,
    DimensionSourceLineAppearance DimensionLine,
    IReadOnlyList<DimensionSourceLineAppearance> ExtensionLines,
    IReadOnlyList<DimensionSourceLineAppearance> ArrowLines);

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

    // Source-derived appearance evidence captured before DWG emission.
    // This is evidence only; destructive suppression still requires an
    // independent SourceEquivalenceAssessor verdict.
    public DimensionSourceAppearance? SourceAppearance { get; init; }
}
