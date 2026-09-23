using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Primitives;

public sealed record TextPrimitive(
    string Value,
    Point2 Position,
    double Height,
    double Rotation,
    string? Layer = null,
    IReadOnlyList<string>? SourceIds = null,
    int? RgbColor = null,
    string? FontName = null,
    double? AdvanceWidth = null,
    Point2? VisualCenter = null,
    double? VisibleWidth = null,
    double? VisibleHeight = null,
    string? FontProgramSha256 = null,
    string? FontProgramSubtype = null,
    string? FontEncodingName = null,
    bool? FontHasToUnicode = null,
    bool? FontIsSubset = null)
{
    public IReadOnlyList<string> ProvenanceIds => SourceIds ?? [];
}
