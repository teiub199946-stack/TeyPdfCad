using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Recognition;

public sealed record VectorTextRecognitionResult(
    IReadOnlyList<TextPrimitive> Texts,
    IReadOnlyList<VectorTextRejection> Rejections)
{
    public bool HasText => Texts.Count > 0;
}

public sealed record VectorTextRejection(
    string Reason,
    IReadOnlyList<string> SourcePrimitiveIds,
    Point2 Min,
    Point2 Max,
    int StrokeCount)
{
    public IReadOnlyList<string> ProvenanceIds => SourcePrimitiveIds;
}
