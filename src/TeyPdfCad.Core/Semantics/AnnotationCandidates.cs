using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Semantics;

public sealed record SemanticWarning(
    string Code,
    string Message,
    IReadOnlyList<string>? SourcePrimitiveIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];
}

public sealed record AxisCandidate(
    Point2 Start,
    Point2 End,
    double Confidence,
    IReadOnlyList<string>? SourcePrimitiveIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];
}

public sealed record LeaderCandidate(
    Point2 ArrowPoint,
    Point2 TextPoint,
    string Text,
    double Confidence,
    IReadOnlyList<string>? SourcePrimitiveIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];
}

public sealed record LevelCandidate(
    Point2 MarkerPoint,
    Point2 TextPoint,
    string Value,
    double Confidence,
    IReadOnlyList<string>? SourcePrimitiveIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];
}

public sealed record ArcDimensionCandidate(
    Point2 Center,
    double Radius,
    double StartAngleRadians,
    double EndAngleRadians,
    Point2 TextPoint,
    string SourceText,
    double Confidence,
    IReadOnlyList<string>? SourcePrimitiveIds = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds ?? [];
}
