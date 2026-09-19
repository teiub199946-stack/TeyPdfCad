using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Primitives;

public sealed record LinePrimitive(
    Point2 Start,
    Point2 End,
    string? Layer = null,
    IReadOnlyList<string>? SourceIds = null,
    double? StrokeWidthMm = null,
    IReadOnlyList<double>? DashPatternMm = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];

    public IReadOnlyList<double> StrokeDashPattern => DashPatternMm ?? [];
}
