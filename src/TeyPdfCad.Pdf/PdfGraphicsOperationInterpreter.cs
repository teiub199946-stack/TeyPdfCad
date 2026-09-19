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
        => InterpretDetailed(operations, pageNumber, 0, 0d, 0d).Entities;

    public IReadOnlyList<VectorEntity> Interpret(
        IEnumerable<object> operations,
        int pageNumber,
        int pageRotationDegrees,
        double mediaWidthPoints,
        double mediaHeightPoints,
        double mediaLeftPoints = 0d,
        double mediaBottomPoints = 0d)
        => InterpretDetailed(
            operations,
            pageNumber,
            pageRotationDegrees,
            mediaWidthPoints,
            mediaHeightPoints,
            mediaLeftPoints,
            mediaBottomPoints).Entities;

    public PdfGraphicsInterpretationResult InterpretDetailed(
        IEnumerable<object> operations,
        int pageNumber,
        int pageRotationDegrees,
        double mediaWidthPoints,
        double mediaHeightPoints,
        double mediaLeftPoints = 0d,
        double mediaBottomPoints = 0d)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var entities = new List<VectorEntity>();
        var diagnostics = new List<VectorPageDiagnostic>();
        var unsupportedOperationTypes = new HashSet<string>(StringComparer.Ordinal);
        Point2? currentPoint = null;
        Point2? subpathStart = null;
        List<Point2>? activeSubpath = null;
        var subpaths = new List<List<Point2>>();
        var segments = new List<(Point2 Start, Point2 End)>();
        var strokeWidth = 1d;
        IReadOnlyList<double>? dashPattern = null;
        int? rgbColor = null;
        int? fillRgbColor = null;
        var transform = AffineTransform.ForPage(
            pageRotationDegrees,
            mediaWidthPoints,
            mediaHeightPoints,
            mediaLeftPoints,
            mediaBottomPoints);
        var graphicsStates = new Stack<GraphicsState>();
        ClipRectangle? activeClip = null;
        var activePolygonClips = new List<IReadOnlyList<Point2>>();
        string? unsupportedClipDiagnostic = null;
        var sequence = 0;

        foreach (var operation in operations)
        {
            var entityCountBeforeOperation = entities.Count;
            if (unsupportedClipDiagnostic is not null
                && PaintsCurrentPath(operation)
                && unsupportedOperationTypes.Add(unsupportedClipDiagnostic))
            {
                diagnostics.Add(new VectorPageDiagnostic(
                    "unsupported-pdf-path-operation",
                    $"PDF clipping operation '{unsupportedClipDiagnostic}' affected interpreted vector geometry on page {pageNumber}."));
            }

            switch (operation)
            {
                case Push:
                    graphicsStates.Push(new GraphicsState(transform, strokeWidth, dashPattern, rgbColor, fillRgbColor, activeClip, activePolygonClips.ToArray(), unsupportedClipDiagnostic));
                    break;
                case Pop when graphicsStates.Count > 0:
                    var state = graphicsStates.Pop();
                    transform = state.Transform;
                    strokeWidth = state.StrokeWidth;
                    dashPattern = state.DashPattern;
                    rgbColor = state.StrokeColor;
                    fillRgbColor = state.FillColor;
                    activeClip = state.ActiveClip;
                    activePolygonClips = state.PolygonClips.ToList();
                    unsupportedClipDiagnostic = state.UnsupportedClipDiagnostic;
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
                case object clipping when IsClippingOperation(clipping):
                    if (TryGetRectangularClip(subpaths, out var clipRectangle))
                    {
                        activeClip = activeClip is ClipRectangle existing
                            ? existing.Intersect(clipRectangle)
                            : clipRectangle;
                    }
                    else if (TryGetConvexClip(subpaths, out var polygonClip))
                    {
                        activePolygonClips.Add(polygonClip);
                    }
                    else
                    {
                        unsupportedClipDiagnostic = $"{clipping.GetType().Name}{DescribePathBounds(subpaths)}";
                    }
                    break;
                default:
                    var operationType = operation.GetType();
                    if ((operationType.Namespace?.Contains("PathConstruction", StringComparison.Ordinal) == true
                         || operationType.Namespace?.Contains("PathPainting", StringComparison.Ordinal) == true
                         || operationType.Namespace?.Contains("ClippingPaths", StringComparison.Ordinal) == true)
                        && unsupportedOperationTypes.Add(operationType.Name))
                    {
                        diagnostics.Add(new VectorPageDiagnostic(
                            "unsupported-pdf-path-operation",
                            $"PDF path operation '{operationType.Name}' was not interpreted on page {pageNumber}."));
                    }
                    break;
            }

            if (activeClip is ClipRectangle clip && entities.Count > entityCountBeforeOperation)
                ClipNewEntities(entities, entityCountBeforeOperation, clip);
            foreach (var polygonClip in activePolygonClips)
            {
                if (entities.Count > entityCountBeforeOperation)
                    ClipNewEntities(entities, entityCountBeforeOperation, polygonClip);
            }
        }
        return new PdfGraphicsInterpretationResult(entities, diagnostics);
    }

    private static bool IsClippingOperation(object operation)
        => operation.GetType().Namespace?.Contains("ClippingPaths", StringComparison.Ordinal) == true;

    private static bool PaintsCurrentPath(object operation)
        => operation is StrokePath
            or CloseAndStrokePath
            or FillPathNonZeroWinding
            or FillPathNonZeroWindingCompatibility
            or FillPathEvenOddRule
            or FillPathNonZeroWindingAndStroke
            or FillPathEvenOddRuleAndStroke
            or CloseFillPathNonZeroWindingAndStroke
            or CloseFillPathEvenOddRuleAndStroke;

    private static string DescribePathBounds(IReadOnlyList<List<Point2>> subpaths)
    {
        var points = subpaths.SelectMany(path => path).ToArray();
        if (points.Length == 0) return " (empty clipping path)";
        return FormattableString.Invariant(
            $" ({subpaths.Count} subpaths, {points.Length} vertices, bounds {points.Min(point => point.X):0.###},{points.Min(point => point.Y):0.###} to {points.Max(point => point.X):0.###},{points.Max(point => point.Y):0.###} mm)");
    }

    private static bool TryGetRectangularClip(
        IReadOnlyList<List<Point2>> subpaths,
        out ClipRectangle rectangle)
    {
        rectangle = default;
        if (subpaths.Count != 1) return false;
        var vertices = NormalizeLoop(subpaths[0]);
        if (vertices.Length < 4) return false;

        const double tolerance = 0.01d;
        var minimumX = vertices.Min(point => point.X);
        var minimumY = vertices.Min(point => point.Y);
        var maximumX = vertices.Max(point => point.X);
        var maximumY = vertices.Max(point => point.Y);
        var rectangular = vertices.All(point =>
            (Math.Abs(point.X - minimumX) <= tolerance || Math.Abs(point.X - maximumX) <= tolerance)
            && (Math.Abs(point.Y - minimumY) <= tolerance || Math.Abs(point.Y - maximumY) <= tolerance));
        if (!rectangular || maximumX - minimumX <= tolerance || maximumY - minimumY <= tolerance) return false;
        rectangle = new ClipRectangle(minimumX, minimumY, maximumX, maximumY);
        return true;
    }

    private static bool TryGetConvexClip(IReadOnlyList<List<Point2>> subpaths, out IReadOnlyList<Point2> polygon)
    {
        polygon = [];
        if (subpaths.Count != 1) return false;
        var vertices = NormalizeLoop(subpaths[0]);
        if (vertices.Length < 3) return false;
        double? sign = null;
        for (var index = 0; index < vertices.Length; index++)
        {
            var a = vertices[index];
            var b = vertices[(index + 1) % vertices.Length];
            var c = vertices[(index + 2) % vertices.Length];
            var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) <= 1e-9) continue;
            var currentSign = Math.Sign(cross);
            if (sign.HasValue && currentSign != sign.Value) return false;
            sign = currentSign;
        }
        if (!sign.HasValue) return false;
        polygon = vertices;
        return true;
    }

    private static void ClipNewEntities(List<VectorEntity> entities, int startIndex, ClipRectangle clip)
    {
        var additions = new List<VectorEntity>();
        for (var index = startIndex; index < entities.Count; index++)
        {
            var entity = entities[index];
            switch (entity)
            {
                case VectorLine line when TryClipLine(line.Start, line.End, clip, out var start, out var end):
                    additions.Add(line with { Start = start, End = end });
                    break;
                case VectorLine:
                    break;
                case VectorPolyline polyline:
                    ClipPolyline(polyline, clip, additions);
                    break;
                case VectorFilledPath filled:
                    var boundary = ClipPolygon(filled.Boundary, clip);
                    if (boundary.Count >= 3)
                    {
                        var holes = filled.InteriorBoundaries
                            .Select(loop => ClipPolygon(loop, clip))
                            .Where(loop => loop.Count >= 3)
                            .Cast<IReadOnlyList<Point2>>()
                            .ToArray();
                        additions.Add(filled with { Boundary = boundary, InteriorBoundaries = holes });
                    }
                    break;
                default:
                    additions.Add(entity);
                    break;
            }
        }
        entities.RemoveRange(startIndex, entities.Count - startIndex);
        entities.AddRange(additions);
    }

    private static void ClipNewEntities(List<VectorEntity> entities, int startIndex, IReadOnlyList<Point2> clip)
    {
        var additions = new List<VectorEntity>();
        for (var index = startIndex; index < entities.Count; index++)
        {
            var entity = entities[index];
            switch (entity)
            {
                case VectorLine line when TryClipLine(line.Start, line.End, clip, out var start, out var end):
                    additions.Add(line with { Start = start, End = end });
                    break;
                case VectorLine:
                    break;
                case VectorPolyline polyline:
                    var segmentCount = polyline.IsClosed ? polyline.Vertices.Count : polyline.Vertices.Count - 1;
                    for (var segment = 0; segment < segmentCount; segment++)
                    {
                        if (TryClipLine(polyline.Vertices[segment], polyline.Vertices[(segment + 1) % polyline.Vertices.Count], clip, out var segmentStart, out var segmentEnd))
                            additions.Add(new VectorLine($"{polyline.SourceId}-clip-{segment + 1}", segmentStart, segmentEnd, polyline.Style, polyline.Confidence));
                    }
                    break;
                case VectorFilledPath filled:
                    var boundary = ClipPolygon(filled.Boundary, clip);
                    if (boundary.Count >= 3)
                    {
                        var holes = filled.InteriorBoundaries.Select(loop => ClipPolygon(loop, clip)).Where(loop => loop.Count >= 3).Cast<IReadOnlyList<Point2>>().ToArray();
                        additions.Add(filled with { Boundary = boundary, InteriorBoundaries = holes });
                    }
                    break;
                default:
                    additions.Add(entity);
                    break;
            }
        }
        entities.RemoveRange(startIndex, entities.Count - startIndex);
        entities.AddRange(additions);
    }

    private static bool TryClipLine(Point2 start, Point2 end, IReadOnlyList<Point2> polygon, out Point2 clippedStart, out Point2 clippedEnd)
    {
        var directionX = end.X - start.X;
        var directionY = end.Y - start.Y;
        var orientation = SignedArea(polygon) >= 0d ? 1d : -1d;
        var minimumT = 0d;
        var maximumT = 1d;
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            var startDistance = orientation * ((b.X - a.X) * (start.Y - a.Y) - (b.Y - a.Y) * (start.X - a.X));
            var directionDistance = orientation * ((b.X - a.X) * directionY - (b.Y - a.Y) * directionX);
            if (Math.Abs(directionDistance) <= 1e-12)
            {
                if (startDistance < 0d) { clippedStart = default; clippedEnd = default; return false; }
                continue;
            }
            var crossing = -startDistance / directionDistance;
            if (directionDistance > 0d) minimumT = Math.Max(minimumT, crossing);
            else maximumT = Math.Min(maximumT, crossing);
            if (minimumT > maximumT) { clippedStart = default; clippedEnd = default; return false; }
        }
        clippedStart = new Point2(start.X + minimumT * directionX, start.Y + minimumT * directionY);
        clippedEnd = new Point2(start.X + maximumT * directionX, start.Y + maximumT * directionY);
        return clippedStart != clippedEnd;
    }

    private static IReadOnlyList<Point2> ClipPolygon(IReadOnlyList<Point2> polygon, IReadOnlyList<Point2> clip)
    {
        IEnumerable<Point2> result = polygon;
        var orientation = SignedArea(clip) >= 0d ? 1d : -1d;
        for (var index = 0; index < clip.Count; index++)
        {
            var a = clip[index];
            var b = clip[(index + 1) % clip.Count];
            result = ClipPolygonEdge(
                result,
                point => orientation * ((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X)) >= -1e-9,
                (start, end) => IntersectLines(start, end, a, b));
        }
        return result.ToArray();
    }

    private static double SignedArea(IReadOnlyList<Point2> polygon)
        => polygon.Select((point, index) => point.X * polygon[(index + 1) % polygon.Count].Y - polygon[(index + 1) % polygon.Count].X * point.Y).Sum() / 2d;

    private static Point2 IntersectLines(Point2 start, Point2 end, Point2 clipStart, Point2 clipEnd)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var clipDx = clipEnd.X - clipStart.X;
        var clipDy = clipEnd.Y - clipStart.Y;
        var denominator = dx * clipDy - dy * clipDx;
        if (Math.Abs(denominator) <= 1e-12) return end;
        var ratio = ((clipStart.X - start.X) * clipDy - (clipStart.Y - start.Y) * clipDx) / denominator;
        return new Point2(start.X + ratio * dx, start.Y + ratio * dy);
    }

    private static void ClipPolyline(VectorPolyline polyline, ClipRectangle clip, ICollection<VectorEntity> output)
    {
        if (polyline.Vertices.All(clip.Contains))
        {
            output.Add(polyline);
            return;
        }

        var segmentCount = polyline.IsClosed ? polyline.Vertices.Count : polyline.Vertices.Count - 1;
        for (var index = 0; index < segmentCount; index++)
        {
            if (!TryClipLine(polyline.Vertices[index], polyline.Vertices[(index + 1) % polyline.Vertices.Count], clip, out var start, out var end))
                continue;
            output.Add(new VectorLine($"{polyline.SourceId}-clip-{index + 1}", start, end, polyline.Style, polyline.Confidence));
        }
    }

    private static bool TryClipLine(Point2 start, Point2 end, ClipRectangle clip, out Point2 clippedStart, out Point2 clippedEnd)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var minimumT = 0d;
        var maximumT = 1d;
        if (!ClipTest(-deltaX, start.X - clip.MinimumX, ref minimumT, ref maximumT)
            || !ClipTest(deltaX, clip.MaximumX - start.X, ref minimumT, ref maximumT)
            || !ClipTest(-deltaY, start.Y - clip.MinimumY, ref minimumT, ref maximumT)
            || !ClipTest(deltaY, clip.MaximumY - start.Y, ref minimumT, ref maximumT))
        {
            clippedStart = default;
            clippedEnd = default;
            return false;
        }

        clippedStart = new Point2(start.X + minimumT * deltaX, start.Y + minimumT * deltaY);
        clippedEnd = new Point2(start.X + maximumT * deltaX, start.Y + maximumT * deltaY);
        return clippedStart != clippedEnd;
    }

    private static bool ClipTest(double direction, double distance, ref double minimumT, ref double maximumT)
    {
        if (Math.Abs(direction) <= 1e-12) return distance >= 0d;
        var ratio = distance / direction;
        if (direction < 0d)
        {
            if (ratio > maximumT) return false;
            if (ratio > minimumT) minimumT = ratio;
        }
        else
        {
            if (ratio < minimumT) return false;
            if (ratio < maximumT) maximumT = ratio;
        }
        return true;
    }

    private static IReadOnlyList<Point2> ClipPolygon(IReadOnlyList<Point2> polygon, ClipRectangle clip)
    {
        IEnumerable<Point2> result = polygon;
        result = ClipPolygonEdge(result, point => point.X >= clip.MinimumX, (a, b) => IntersectVertical(a, b, clip.MinimumX));
        result = ClipPolygonEdge(result, point => point.X <= clip.MaximumX, (a, b) => IntersectVertical(a, b, clip.MaximumX));
        result = ClipPolygonEdge(result, point => point.Y >= clip.MinimumY, (a, b) => IntersectHorizontal(a, b, clip.MinimumY));
        result = ClipPolygonEdge(result, point => point.Y <= clip.MaximumY, (a, b) => IntersectHorizontal(a, b, clip.MaximumY));
        return result.ToArray();
    }

    private static IEnumerable<Point2> ClipPolygonEdge(IEnumerable<Point2> source, Func<Point2, bool> inside, Func<Point2, Point2, Point2> intersect)
    {
        var input = source.ToArray();
        if (input.Length == 0) return [];
        var output = new List<Point2>();
        var previous = input[^1];
        var previousInside = inside(previous);
        foreach (var current in input)
        {
            var currentInside = inside(current);
            if (currentInside != previousInside) output.Add(intersect(previous, current));
            if (currentInside) output.Add(current);
            previous = current;
            previousInside = currentInside;
        }
        return output;
    }

    private static Point2 IntersectVertical(Point2 start, Point2 end, double x)
    {
        var ratio = (x - start.X) / (end.X - start.X);
        return new Point2(x, start.Y + ratio * (end.Y - start.Y));
    }

    private static Point2 IntersectHorizontal(Point2 start, Point2 end, double y)
    {
        var ratio = (y - start.Y) / (end.Y - start.Y);
        return new Point2(start.X + ratio * (end.X - start.X), y);
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
        int? FillColor,
        ClipRectangle? ActiveClip,
        IReadOnlyList<IReadOnlyList<Point2>> PolygonClips,
        string? UnsupportedClipDiagnostic);

    private readonly record struct ClipRectangle(double MinimumX, double MinimumY, double MaximumX, double MaximumY)
    {
        public bool Contains(Point2 point)
            => point.X >= MinimumX && point.X <= MaximumX && point.Y >= MinimumY && point.Y <= MaximumY;

        public ClipRectangle Intersect(ClipRectangle other)
            => new(
                Math.Max(MinimumX, other.MinimumX),
                Math.Max(MinimumY, other.MinimumY),
                Math.Min(MaximumX, other.MaximumX),
                Math.Min(MaximumY, other.MaximumY));
    }

    private readonly record struct AffineTransform(double A, double B, double C, double D, double E, double F)
    {
        public static AffineTransform Identity => new(1d, 0d, 0d, 1d, 0d, 0d);

        public static AffineTransform ForPage(int rotationDegrees, double width, double height, double left, double bottom)
            => rotationDegrees switch
            {
                0 => new(1d, 0d, 0d, 1d, -left, -bottom),
                90 => new(0d, -1d, 1d, 0d, -bottom, width + left),
                180 => new(-1d, 0d, 0d, -1d, width + left, height + bottom),
                270 => new(0d, 1d, -1d, 0d, height + bottom, -left),
                _ => throw new InvalidDataException($"Unsupported PDF page rotation: {rotationDegrees} degrees.")
            };

        public static AffineTransform FromPdfValues(IReadOnlyList<double> values)
            => values.Count == 6
                ? new(values[0], values[1], values[2], values[3], values[4], values[5])
                : throw new InvalidDataException("PDF transformation matrix must contain six values.");

        public (double X, double Y) Apply(double x, double y)
            => (A * x + C * y + E, B * x + D * y + F);

        public double LengthScale => Math.Sqrt(Math.Abs(A * D - B * C));

        public AffineTransform Concat(AffineTransform next)
            => new(
                A * next.A + C * next.B,
                B * next.A + D * next.B,
                A * next.C + C * next.D,
                B * next.C + D * next.D,
                A * next.E + C * next.F + E,
                B * next.E + D * next.F + F);
    }
}

public sealed record PdfGraphicsInterpretationResult(
    IReadOnlyList<VectorEntity> Entities,
    IReadOnlyList<VectorPageDiagnostic> Diagnostics);
