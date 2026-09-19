using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;

namespace TeyPdfCad.Pdf;

public sealed class PdfGraphicsOperationInterpreter
{
    public IReadOnlyList<VectorEntity> Interpret(IEnumerable<object> operations, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var entities = new List<VectorEntity>();
        Point2? currentPoint = null;
        Point2? segmentStart = null;
        var strokeWidth = 1d;
        IReadOnlyList<double>? dashPattern = null;
        var sequence = 0;

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case SetLineWidth width:
                    strokeWidth = width.Width;
                    break;
                case SetLineDashPattern dash:
                    dashPattern = dash.Pattern.Array;
                    break;
                case BeginNewSubpath move:
                    currentPoint = new Point2(move.X, move.Y);
                    break;
                case AppendStraightLineSegment line when currentPoint is Point2 start:
                    segmentStart = start;
                    currentPoint = new Point2(line.X, line.Y);
                    break;
                case StrokePath when segmentStart is Point2 from && currentPoint is Point2 to:
                    sequence++;
                    entities.Add(new VectorLine(
                        $"page-{pageNumber}-line-{sequence}",
                        from,
                        to,
                        new VectorStyle(StrokeWidthPoints: strokeWidth, DashPatternPoints: dashPattern)));
                    segmentStart = null;
                    break;
            }
        }

        return entities;
    }
}
