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
