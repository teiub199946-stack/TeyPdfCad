using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations;

namespace TeyPdfCad.Pdf;

public sealed class PdfGraphicsOperationInterpreter
{
    public IReadOnlyList<VectorEntity> Interpret(IEnumerable<object> operations, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var entities = new List<VectorEntity>();
        Point2? currentPoint = null;
        Point2? subpathStart = null;
        var segments = new List<(Point2 Start, Point2 End)>();
        var pathVertices = new List<Point2>();
        var strokeWidth = 1d;
        IReadOnlyList<double>? dashPattern = null;
        int? rgbColor = null;
        int? fillRgbColor = null;
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
                case SetStrokeColorDeviceRgb color:
                    rgbColor = ToRgb(color.R, color.G, color.B);
                    break;
                case SetNonStrokeColorDeviceRgb color:
                    fillRgbColor = ToRgb(color.R, color.G, color.B);
                    break;
                case BeginNewSubpath move:
                    currentPoint = ToMillimetres(move.X, move.Y);
                    subpathStart = currentPoint;
                    pathVertices.Clear();
                    pathVertices.Add(currentPoint.Value);
                    break;
                case AppendStraightLineSegment line when currentPoint is Point2 start:
                    var end = ToMillimetres(line.X, line.Y);
                    segments.Add((start, end));
                    pathVertices.Add(end);
                    currentPoint = end;
                    break;
                case CloseSubpath when currentPoint is Point2 current && subpathStart is Point2 start:
                    if (current != start)
                    {
                        segments.Add((current, start));
                    }
                    currentPoint = start;
                    break;
                case StrokePath:
                    EmitStroke(entities, segments, pageNumber, ref sequence, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, pathVertices, ref currentPoint, ref subpathStart);
                    break;
                case FillPathNonZeroWinding:
                    EmitFill(entities, pathVertices, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor);
                    ClearPath(segments, pathVertices, ref currentPoint, ref subpathStart);
                    break;
                case FillPathEvenOddRule:
                    EmitFill(entities, pathVertices, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor);
                    ClearPath(segments, pathVertices, ref currentPoint, ref subpathStart);
                    break;
                case FillPathNonZeroWindingAndStroke:
                    EmitFill(entities, pathVertices, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor);
                    EmitStroke(entities, segments, pageNumber, ref sequence, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, pathVertices, ref currentPoint, ref subpathStart);
                    break;
                case FillPathEvenOddRuleAndStroke:
                    EmitFill(entities, pathVertices, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor);
                    EmitStroke(entities, segments, pageNumber, ref sequence, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, pathVertices, ref currentPoint, ref subpathStart);
                    break;
            }
        }

        return entities;
    }

    private static int ToRgb(double red, double green, double blue)
    {
        var r = (int)Math.Round(Math.Clamp(red, 0d, 1d) * 255d);
        var g = (int)Math.Round(Math.Clamp(green, 0d, 1d) * 255d);
        var b = (int)Math.Round(Math.Clamp(blue, 0d, 1d) * 255d);
        return (r << 16) | (g << 8) | b;
    }

    private static Point2 ToMillimetres(double x, double y)
        => new(x * VectorPdfPage.MillimetresPerPoint, y * VectorPdfPage.MillimetresPerPoint);

    private static void EmitStroke(
        ICollection<VectorEntity> entities,
        IReadOnlyList<(Point2 Start, Point2 End)> segments,
        int pageNumber,
        ref int sequence,
        int? rgbColor,
        double strokeWidth,
        IReadOnlyList<double>? dashPattern)
    {
        foreach (var segment in segments)
        {
            sequence++;
            entities.Add(new VectorLine(
                $"page-{pageNumber}-line-{sequence}",
                segment.Start,
                segment.End,
                new VectorStyle(RgbColor: rgbColor, StrokeWidthPoints: strokeWidth, DashPatternPoints: dashPattern)));
        }
    }

    private static void EmitFill(
        ICollection<VectorEntity> entities,
        IReadOnlyList<Point2> vertices,
        int pageNumber,
        ref int sequence,
        VectorFillRule fillRule,
        int? fillRgbColor)
    {
        var boundary = vertices.ToArray();
        if (boundary.Length > 1 && boundary[0] == boundary[^1])
        {
            boundary = boundary[..^1];
        }

        if (boundary.Distinct().Count() < 3)
        {
            return;
        }

        sequence++;
        entities.Add(new VectorFilledPath(
            $"page-{pageNumber}-fill-{sequence}",
            boundary,
            fillRule,
            new VectorStyle(RgbColor: fillRgbColor)));
    }

    private static void ClearPath(
        ICollection<(Point2 Start, Point2 End)> segments,
        ICollection<Point2> vertices,
        ref Point2? currentPoint,
        ref Point2? subpathStart)
    {
        segments.Clear();
        vertices.Clear();
        currentPoint = null;
        subpathStart = null;
    }
}
