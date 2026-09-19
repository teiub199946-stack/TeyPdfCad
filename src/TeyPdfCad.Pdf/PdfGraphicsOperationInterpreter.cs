using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.General;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;

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
        var transform = AffineTransform.Identity;
        var graphicsStates = new Stack<GraphicsState>();
        var sequence = 0;

        foreach (var operation in operations)
        {
            switch (operation)
            {
                case Push:
                    graphicsStates.Push(new GraphicsState(transform, strokeWidth, dashPattern, rgbColor, fillRgbColor));
                    break;
                case Pop when graphicsStates.Count > 0:
                    var state = graphicsStates.Pop();
                    transform = state.Transform;
                    strokeWidth = state.StrokeWidth;
                    dashPattern = state.DashPattern;
                    rgbColor = state.StrokeColor;
                    fillRgbColor = state.FillColor;
                    break;
                case ModifyCurrentTransformationMatrix matrix:
                    transform = transform.Concat(AffineTransform.FromPdfValues(matrix.Value));
                    break;
                case SetLineWidth width: strokeWidth = width.Width; break;
                case SetLineDashPattern dash: dashPattern = dash.Pattern.Array; break;
                case SetStrokeColorDeviceRgb color: rgbColor = ToRgb(color.R, color.G, color.B); break;
                case SetNonStrokeColorDeviceRgb color: fillRgbColor = ToRgb(color.R, color.G, color.B); break;
                case SetStrokeColorDeviceGray color: rgbColor = ToRgb(color.Gray, color.Gray, color.Gray); break;
                case SetNonStrokeColorDeviceGray color: fillRgbColor = ToRgb(color.Gray, color.Gray, color.Gray); break;
                case SetStrokeColorDeviceCmyk color: rgbColor = CmykToRgb(color.C, color.M, color.Y, color.K); break;
                case SetNonStrokeColorDeviceCmyk color: fillRgbColor = CmykToRgb(color.C, color.M, color.Y, color.K); break;
                case BeginNewSubpath move:
                    currentPoint = ToMillimetres(transform.Apply(move.X, move.Y));
                    subpathStart = currentPoint;
                    activeSubpath = [currentPoint.Value];
                    subpaths.Add(activeSubpath);
                    break;
                case AppendStraightLineSegment line when currentPoint is Point2 start && activeSubpath is not null:
                    var end = ToMillimetres(transform.Apply(line.X, line.Y));
                    segments.Add((start, end));
                    activeSubpath.Add(end);
                    currentPoint = end;
                    break;
                case AppendRectangle rectangle:
                    AppendRectanglePath(rectangle, transform, subpaths, segments, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case AppendDualControlPointBezierCurve curve when currentPoint is Point2 curveStart && activeSubpath is not null:
                    AppendBezier(
                        curveStart,
                        ToMillimetres(transform.Apply(curve.X1, curve.Y1)),
                        ToMillimetres(transform.Apply(curve.X2, curve.Y2)),
                        ToMillimetres(transform.Apply(curve.X3, curve.Y3)),
                        activeSubpath,
                        segments,
                        ref currentPoint);
                    break;
                case AppendEndControlPointBezierCurve curve when currentPoint is Point2 curveStart && activeSubpath is not null:
                    var endControlPoint = ToMillimetres(transform.Apply(curve.X3, curve.Y3));
                    AppendBezier(
                        curveStart,
                        ToMillimetres(transform.Apply(curve.X1, curve.Y1)),
                        endControlPoint,
                        endControlPoint,
                        activeSubpath,
                        segments,
                        ref currentPoint);
                    break;
                case AppendStartControlPointBezierCurve curve when currentPoint is Point2 curveStart && activeSubpath is not null:
                    AppendBezier(
                        curveStart,
                        curveStart,
                        ToMillimetres(transform.Apply(curve.X2, curve.Y2)),
                        ToMillimetres(transform.Apply(curve.X3, curve.Y3)),
                        activeSubpath,
                        segments,
                        ref currentPoint);
                    break;
                case CloseSubpath:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    break;
                case StrokePath:
                    EmitStrokePaths(entities, subpaths, segments, pageNumber, ref sequence, rgbColor, ScaleStrokeWidth(strokeWidth, transform), ScaleDashPattern(dashPattern, transform));
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case CloseAndStrokePath:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    EmitStrokeBoundaries(entities, subpaths, segments, pageNumber, ref sequence, rgbColor, ScaleStrokeWidth(strokeWidth, transform), ScaleDashPattern(dashPattern, transform));
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathNonZeroWinding:
                    EmitFill(entities, subpaths, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathNonZeroWindingCompatibility:
                    EmitFill(entities, subpaths, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathEvenOddRule:
                    EmitFill(entities, subpaths, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor);
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathNonZeroWindingAndStroke:
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor, rgbColor, ScaleStrokeWidth(strokeWidth, transform), ScaleDashPattern(dashPattern, transform));
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case FillPathEvenOddRuleAndStroke:
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor, rgbColor, ScaleStrokeWidth(strokeWidth, transform), ScaleDashPattern(dashPattern, transform));
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case CloseFillPathNonZeroWindingAndStroke:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.NonZero, fillRgbColor, rgbColor, ScaleStrokeWidth(strokeWidth, transform), ScaleDashPattern(dashPattern, transform));
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case CloseFillPathEvenOddRuleAndStroke:
                    CloseCurrentSubpath(segments, ref currentPoint, subpathStart);
                    EmitFillAndStroke(entities, subpaths, segments, pageNumber, ref sequence, VectorFillRule.EvenOdd, fillRgbColor, rgbColor, ScaleStrokeWidth(strokeWidth, transform), ScaleDashPattern(dashPattern, transform));
                    ClearPath(segments, subpaths, ref currentPoint, ref subpathStart, ref activeSubpath);
                    break;
                case EndPath:
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
    private static int CmykToRgb(double cyan, double magenta, double yellow, double black)
        => ToRgb(1d - Math.Min(1d, cyan + black), 1d - Math.Min(1d, magenta + black), 1d - Math.Min(1d, yellow + black));

    private static double ScaleStrokeWidth(double width, AffineTransform transform) => width * transform.LengthScale;

    private static IReadOnlyList<double>? ScaleDashPattern(IReadOnlyList<double>? pattern, AffineTransform transform)
        => pattern?.Select(length => length * transform.LengthScale).ToArray();
    private static Point2 ToMillimetres((double X, double Y) point) => new(point.X * VectorPdfPage.MillimetresPerPoint, point.Y * VectorPdfPage.MillimetresPerPoint);

    private static void AppendRectanglePath(
        AppendRectangle rectangle,
        AffineTransform transform,
        ICollection<List<Point2>> subpaths,
        ICollection<(Point2 Start, Point2 End)> segments,
        ref Point2? currentPoint,
        ref Point2? subpathStart,
        ref List<Point2>? activeSubpath)
    {
        var x = rectangle.LowerLeftX;
        var y = rectangle.LowerLeftY;
        var corners = new[]
        {
            ToMillimetres(transform.Apply(x, y)),
            ToMillimetres(transform.Apply(x + rectangle.Width, y)),
            ToMillimetres(transform.Apply(x + rectangle.Width, y + rectangle.Height)),
            ToMillimetres(transform.Apply(x, y + rectangle.Height))
        };
        activeSubpath = corners.ToList();
        subpaths.Add(activeSubpath);
        for (var index = 0; index < corners.Length; index++)
        {
            segments.Add((corners[index], corners[(index + 1) % corners.Length]));
        }
        subpathStart = corners[0];
        currentPoint = corners[0];
    }

    private static void AppendBezier(
        Point2 start,
        Point2 control1,
        Point2 control2,
        Point2 end,
        ICollection<Point2> vertices,
        ICollection<(Point2 Start, Point2 End)> segments,
        ref Point2? currentPoint)
    {
        const int subdivisions = 16;
        var previous = start;
        for (var index = 1; index <= subdivisions; index++)
        {
            var t = index / (double)subdivisions;
            var oneMinusT = 1d - t;
            var point = new Point2(
                oneMinusT * oneMinusT * oneMinusT * start.X
                + 3d * oneMinusT * oneMinusT * t * control1.X
                + 3d * oneMinusT * t * t * control2.X
                + t * t * t * end.X,
                oneMinusT * oneMinusT * oneMinusT * start.Y
                + 3d * oneMinusT * oneMinusT * t * control1.Y
                + 3d * oneMinusT * t * t * control2.Y
                + t * t * t * end.Y);
            segments.Add((previous, point));
            vertices.Add(point);
            previous = point;
        }
        currentPoint = end;
    }

    private static void EmitStrokePaths(ICollection<VectorEntity> entities, IReadOnlyList<List<Point2>> subpaths, IReadOnlyList<(Point2 Start, Point2 End)> segments, int pageNumber, ref int sequence, int? color, double width, IReadOnlyList<double>? dashes)
    {
        var style = new VectorStyle(RgbColor: color, StrokeWidthPoints: width, DashPatternPoints: dashes);
        foreach (var subpath in subpaths)
        {
            var vertices = NormalizeLoop(subpath);
            if (vertices.Length < 2) continue;
            var isClosed = IsClosedInSegments(vertices, segments);
            sequence++;
            if (vertices.Length == 2 && !isClosed)
                entities.Add(new VectorLine($"page-{pageNumber}-line-{sequence}", vertices[0], vertices[1], style));
            else
                entities.Add(new VectorPolyline($"page-{pageNumber}-path-{sequence}", vertices, isClosed, style));
        }
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

    private sealed record GraphicsState(
        AffineTransform Transform,
        double StrokeWidth,
        IReadOnlyList<double>? DashPattern,
        int? StrokeColor,
        int? FillColor);

    private readonly record struct AffineTransform(double A, double B, double C, double D, double E, double F)
    {
        public static AffineTransform Identity => new(1d, 0d, 0d, 1d, 0d, 0d);

        public static AffineTransform FromPdfValues(IReadOnlyList<double> values)
            => values.Count == 6
                ? new(values[0], values[1], values[2], values[3], values[4], values[5])
                : throw new InvalidDataException("PDF transformation matrix must contain six values.");

        public (double X, double Y) Apply(double x, double y)
            => (A * x + C * y + E, B * x + D * y + F);

        public double LengthScale => Math.Sqrt(Math.Abs(A * D - B * C));

        public AffineTransform Concat(AffineTransform next)
            => new(
                next.A * A + next.C * B,
                next.B * A + next.D * B,
                next.A * C + next.C * D,
                next.B * C + next.D * D,
                next.A * E + next.C * F + next.E,
                next.B * E + next.D * F + next.F);
    }
}
