using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig.Graphics.Operations;
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
        Point2? subpathStart = null;
        List<Point2>? activeSubpath = null;
        var subpaths = new List<List<Point2>>();
        var segments = new List<(Point2 Start, Point2 End)>();
        var strokeWidth = 1d;
        IReadOnlyList<double>? dashPattern = null;
        int? rgbColor = null;
        int? fillRgbColor = null;
        var sequence = 0;

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case SetLineWidth width: strokeWidth = width.Width; break;
                case SetLineDashPattern dash: dashPattern = dash.Pattern.Array; break;
                case SetStrokeColorDeviceRgb color: rgbColor = ToRgb(color.R, color.G, color.B); break;
                case SetNonStrokeColorDeviceRgb color: fillRgbColor = ToRgb(color.R, color.G, color.B); break;
                case BeginNewSubpath move:
                    currentPoint = ToMillimetres(move.X, move.Y);
                    subpathStart = currentPoint;
                    activeSubpath = [currentPoint.Value];
                    subpaths.Add(activeSubpath);
                    break;
                case AppendStraightLineSegment line when currentPoint is Point2 start && activeSubpath is not null:
                    var end = ToMillimetres(line.X, line.Y);
                    segments.Add((start, end));
                    activeSubpath.Add(end);
                    currentPoint = end;
                    break;
                case CloseSubpath:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    break;
                case StrokePath:
                    EmitStroke(entities, segments, pageNumber, ref sequence, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathNonZeroWinding:
                    EmitFill(entities, subpaths, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathEvenOddRule:
                    EmitFill(entities, subpaths, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathNonZeroWindingAndStroke:
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathEvenOddRuleAndStroke:
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case CloseFillPathNonZeroWindingAndStroke:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case CloseFillPathEvenOddRuleAndStroke:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor, rgbColor, strokeWidth, dashPattern);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
            }
        }
        return entities;
    }

    private static void EmitFillAndStroke(ICollection<VectorEntity> entities, IReadOnlyList<List<Point2>> subpaths, IReadOnlyList<(Point2 Start, Point2 End)> segments, int pageNumber, ref int sequence, VectorFillRule rule, int? fillColor, int? strokeColor, double width, IReadOnlyList<double>? dashes)
    {
        EmitFill(entities, subpaths, pageNumber, ref sequence, rule, fillColor);
        EmitStrokeBoundaries(entities, subpaths, segments, pageNumber, ref sequence, strokeColor, width, dashes);
    }

    private static void EmitStrokeBoundaries(ICollection<VectorEntity> entities, IReadOnlyList<List<Point2>> subpaths, IReadOnlyList<(Point2 Start, Point2 End)> segments, int pageNumber, ref int sequence, int? color, double width, IReadOnlyList<double>? dashes)
    {
        foreach (var subpath in subpaths)
        {
            var vertices = NormalizeLoop(subpath);
            if (vertices.Length < 2) continue;
            sequence++;
            entities.Add(new VectorPolyline($"page-{pageNumber}-boundary-{sequence}", vertices, IsClosedInSegments(vertices, segments), new VectorStyle(RgbColor: color, StrokeWidthPoints: width, DashPatternPoints: dashes)));
        }
    }

    private static bool IsClosedInSegments(IReadOnlyList<Point2> vertices, IReadOnlyList<(Point2 Start, Point2 End)> segments)
        => vertices.Count >= 3 && segments.Any(segment => segment.Start == vertices[^1] && segment.End == vertices[0]);

    private static void CloseCurrentSubpath(ICollection<(Point2 Start, Point2 End)> segments, ref Point2? currentPoint, Point2? subpathStart)
    {
        if (currentPoint is Point2 current && subpathStart is Point2 start && current != start)
            segments.Add((current, start));
        currentPoint = subpathStart;
    }

    private static int ToRgb(double red, double green, double blue) => ((int)Math.Round(Math.Clamp(red, 0d, 1d) * 255d) << 16) | ((int)Math.Round(Math.Clamp(green, 0d, 1d) * 255d) << 8) | (int)Math.Round(Math.Clamp(blue, 0d, 1d) * 255d);
    private static Point2 ToMillimetres(double x, double y) => new(x * VectorPdfPage.MillimetresPerPoint, y * VectorPdfPage.MillimetresPerPoint);

    private static void EmitStroke(ICollection<VectorEntity> entities, IReadOnlyList<(Point2 Start, Point2 End)> segments, int pageNumber, ref int sequence, int? color, double width, IReadOnlyList<double>? dashes)
    {
        foreach (var segment in segments) { sequence++; entities.Add(new VectorLine($"page-{pageNumber}-line-{sequence}", segment.Start, segment.End, new VectorStyle(RgbColor: color, StrokeWidthPoints: width, DashPatternPoints: dashes))); }
    }

    private static void EmitFill(ICollection<VectorEntity> entities, IReadOnlyList<List<Point2>> subpaths, int pageNumber, ref int sequence, VectorFillRule rule, int? color)
    {
        var loops = subpaths.Select(NormalizeLoop).Where(loop => loop.Distinct().Count() >= 3).Cast<IReadOnlyList<Point2>>().ToArray();
        if (loops.Length == 0) return;
        sequence++;
        entities.Add(new VectorFilledPath($"page-{pageNumber}-fill-{sequence}", loops[0], rule, new VectorStyle(RgbColor: color), InteriorBoundaries: loops.Skip(1).ToArray()));
    }

    private static Point2[] NormalizeLoop(IReadOnlyList<Point2> vertices)
        => vertices.Count > 1 && vertices[0] == vertices[^1] ? vertices.Take(vertices.Count - 1).ToArray() : vertices.ToArray();

    private static void ClearPath(ICollection<(Point2 Start, Point2 End)> segments, ICollection<List<Point2>> subpaths, ref Point2? current, ref Point2? start, ref List<Point2>? active)
    {
        segments.Clear(); subpaths.Clear(); current = null; start = null; active = null;
    }
}
