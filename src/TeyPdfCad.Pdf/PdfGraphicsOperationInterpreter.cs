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
        var segments = new List<(Point2 Start, Point2 End)>();
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
                    var end = new Point2(line.X, line.Y);
                    segments.Add((start, end));
                    currentPoint = end;
                    break;
                case StrokePath:
                    foreach (var segment in segments)
                    {
                        sequence++;
                        entities.Add(new VectorLine(
                            $"page-{pageNumber}-line-{sequence}",
                            segment.Start,
                            segment.End,
                            new VectorStyle(StrokeWidthPoints: strokeWidth, DashPatternPoints: dashPattern)));
                    }
                    segments.Clear();
                    break;
            }
        }

        return entities;
    }
}
