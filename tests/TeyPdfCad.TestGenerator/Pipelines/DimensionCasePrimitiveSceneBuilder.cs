using System.Globalization;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Pipelines;

/// <summary>
/// Converts TEST-001 drawing-space cases into PDF/PDFIMPORT-like paper-space primitives.
/// The Core sees only visual evidence; expected semantic results are never fed to it.
/// </summary>
public sealed class DimensionCasePrimitiveSceneBuilder
{
    public PrimitiveScene Build(DimensionCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        if (!double.IsFinite(testCase.DrawingScale) || testCase.DrawingScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(testCase), "DrawingScale must be finite and positive.");

        var scene = new PrimitiveScene();

        if (testCase.NegativePattern != NegativePattern.None)
        {
            BuildNegative(scene, testCase);
            return scene;
        }

        if (testCase.DimensionType == DimensionType.Chain && testCase.Segments.Count > 0)
        {
            BuildChain(scene, testCase);
            return scene;
        }

        BuildSingle(scene, testCase, $"{testCase.Id}:primary");

        if (testCase.Tags.Contains("nearby-dimensions", StringComparer.Ordinal))
            AddNearbyDimension(scene, testCase);
        else if (testCase.Tags.Contains("intersecting-dimensions", StringComparer.Ordinal))
            AddIntersectingDimension(scene, testCase);

        return scene;
    }

    private static void BuildSingle(PrimitiveScene scene, DimensionCase testCase, string prefix)
    {
        var scale = testCase.DrawingScale;
        AddDimensionEvidence(
            scene,
            ToPaperObserved(testCase.P1, testCase.ObservedGeometry.P1, scale),
            ToPaperObserved(testCase.P2, testCase.ObservedGeometry.P2, scale),
            ToPaperObserved(testCase.DimensionLinePoint, testCase.ObservedGeometry.DimensionLinePoint, scale),
            ToPaperObserved(testCase.TextPosition, testCase.ObservedGeometry.TextPosition, scale),
            testCase.ExpectedValue,
            testCase.TextHeight / scale,
            testCase.ArrowType,
            testCase.ArrowSize / scale,
            testCase.ExtensionLineExtension / scale,
            testCase.Rotation + (testCase.IsTextFlipped ? 180.0 : 0.0),
            testCase.Noise,
            testCase.IsDimensionLineBroken,
            includeExtensions: true,
            includeArrows: true,
            prefix);
    }

    private static void BuildChain(PrimitiveScene scene, DimensionCase testCase)
    {
        var scale = testCase.DrawingScale;
        var first = ToPaper(testCase.Segments[0].P1, scale);
        var last = ToPaper(testCase.Segments[^1].P2, scale);
        var overallDirection = Normalize(last - first);
        var overallNormal = Perpendicular(overallDirection);
        var offset = Dot(ToPaper(testCase.DimensionLinePoint, scale) - Midpoint(first, last), overallNormal);

        for (var i = 0; i < testCase.Segments.Count; i++)
        {
            var segment = testCase.Segments[i];
            var p1 = AddDeterministicPaperNoise(ToPaper(segment.P1, scale), testCase, i * 4 + 1);
            var p2 = AddDeterministicPaperNoise(ToPaper(segment.P2, scale), testCase, i * 4 + 2);
            var direction = Normalize(p2 - p1);
            var normal = Perpendicular(direction);
            var dimPoint = AddDeterministicPaperNoise(Midpoint(p1, p2) + normal * offset, testCase, i * 4 + 3);
            var textPoint = dimPoint + new PaperPoint(testCase.Noise.TextOffset.X, testCase.Noise.TextOffset.Y);

            AddDimensionEvidence(
                scene,
                p1,
                p2,
                dimPoint,
                textPoint,
                segment.ExpectedValue,
                testCase.TextHeight / scale,
                testCase.ArrowType,
                testCase.ArrowSize / scale,
                testCase.ExtensionLineExtension / scale,
                testCase.Rotation + (testCase.IsTextFlipped ? 180.0 : 0.0),
                testCase.Noise,
                testCase.IsDimensionLineBroken,
                includeExtensions: true,
                includeArrows: true,
                $"{testCase.Id}:chain:{i}");
        }
    }

    private static void BuildNegative(PrimitiveScene scene, DimensionCase testCase)
    {
        var scale = testCase.DrawingScale;
        var p1 = ToPaperObserved(testCase.P1, testCase.ObservedGeometry.P1, scale);
        var p2 = ToPaperObserved(testCase.P2, testCase.ObservedGeometry.P2, scale);
        var dimPoint = ToPaperObserved(testCase.DimensionLinePoint, testCase.ObservedGeometry.DimensionLinePoint, scale);
        var textPoint = ToPaperObserved(testCase.TextPosition, testCase.ObservedGeometry.TextPosition, scale);
        var textHeight = testCase.TextHeight / scale;
        var arrowSize = testCase.ArrowSize / scale;
        var extension = testCase.ExtensionLineExtension / scale;
        var prefix = $"{testCase.Id}:negative:{testCase.NegativePattern}";

        switch (testCase.NegativePattern)
        {
            case NegativePattern.LinesTextNoArrows:
                AddDimensionEvidence(scene, p1, p2, dimPoint, textPoint, testCase.ExpectedValue,
                    textHeight, testCase.ArrowType, arrowSize, extension, testCase.Rotation,
                    testCase.Noise, testCase.IsDimensionLineBroken, true, false, prefix);
                break;

            case NegativePattern.ArrowsLineNoExtensionLines:
                AddDimensionEvidence(scene, p1, p2, dimPoint, textPoint, testCase.ExpectedValue,
                    textHeight, testCase.ArrowType, arrowSize, extension, testCase.Rotation,
                    testCase.Noise, false, false, true, prefix);
                break;

            case NegativePattern.TextNearOrdinaryLine:
                AddLine(scene, p1, p2, $"{prefix}:ordinary");
                AddText(scene, testCase.ExpectedValue, textPoint, textHeight, testCase.Rotation, $"{prefix}:text");
                break;

            case NegativePattern.ArrowsNearWall:
            {
                var direction = Normalize(p2 - p1);
                var normal = Perpendicular(direction);
                var wallOffset = Math.Max(textHeight * 0.8, 0.5);
                AddLine(scene, p1, p2, $"{prefix}:wall:1");
                AddLine(scene, p1 + normal * wallOffset, p2 + normal * wallOffset, $"{prefix}:wall:2");
                AddArrowEvidence(scene, p1, direction, normal, arrowSize, testCase.ArrowType, true, $"{prefix}:arrow:1");
                AddArrowEvidence(scene, p2, direction, normal, arrowSize, testCase.ArrowType, false, $"{prefix}:arrow:2");
                AddText(scene, testCase.ExpectedValue, textPoint, textHeight, testCase.Rotation, $"{prefix}:text");
                break;
            }

            case NegativePattern.TableLineNumber:
                AddTableLikeEvidence(scene, textPoint, textHeight, testCase.ExpectedValue, testCase.Rotation, prefix);
                break;

            case NegativePattern.AxisText:
            {
                var direction = Normalize(p2 - p1);
                var normal = Perpendicular(direction);
                AddLine(scene, p1, p2, $"{prefix}:axis");
                AddLine(scene, textPoint - normal * textHeight, textPoint + normal * textHeight, $"{prefix}:tick");
                AddText(scene, testCase.ExpectedValue, textPoint + normal * textHeight, textHeight, testCase.Rotation, $"{prefix}:text");
                break;
            }

            case NegativePattern.RandomLinesNumber:
                AddRandomLookalike(scene, textPoint, textHeight, testCase.ExpectedValue, testCase.Rotation, prefix);
                break;

            case NegativePattern.NumberInsideBlock:
                AddBlockLikeEvidence(scene, textPoint, textHeight, testCase.ExpectedValue, testCase.Rotation, prefix);
                break;

            case NegativePattern.NumberNearPolyline:
                AddPolylineLikeEvidence(scene, textPoint, textHeight, testCase.ExpectedValue, testCase.Rotation, prefix);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(testCase.NegativePattern), testCase.NegativePattern, "Unsupported negative pattern.");
        }
    }

    private static void AddDimensionEvidence(
        PrimitiveScene scene,
        PaperPoint p1,
        PaperPoint p2,
        PaperPoint dimensionLinePoint,
        PaperPoint textPosition,
        double displayedValue,
        double textHeight,
        ArrowType arrowType,
        double arrowSize,
        double extensionLineExtension,
        double textRotation,
        NoiseSpec noise,
        bool isBroken,
        bool includeExtensions,
        bool includeArrows,
        string prefix)
    {
        var rawDirection = Normalize(p2 - p1);
        var direction = Rotate(rawDirection, noise.AngularSkewDegrees);
        var normal = Perpendicular(direction);
        var midpoint = Midpoint(p1, p2);
        var halfLength = Distance(p1, p2) / 2.0;
        var signedOffset = Dot(dimensionLinePoint - midpoint, normal);
        var dimStart = midpoint - direction * halfLength + normal * signedOffset;
        var dimEnd = midpoint + direction * halfLength + normal * signedOffset;

        if (noise.EndpointMismatch)
        {
            var mismatch = Math.Max(noise.CoordinateJitter, 0.01) * 0.5;
            dimStart += direction * mismatch;
            dimEnd -= direction * mismatch;
        }

        AddDimensionLine(scene, dimStart, dimEnd, textPosition, textHeight, arrowSize,
            isBroken, noise.MicroBreak, noise.CoordinateJitter, prefix);

        if (includeExtensions)
        {
            AddExtensionLine(scene, p1, dimStart, extensionLineExtension, $"{prefix}:ext:1");
            AddExtensionLine(scene, p2, dimEnd, extensionLineExtension, $"{prefix}:ext:2");
        }

        if (includeArrows)
        {
            AddArrowEvidence(scene, dimStart, direction, normal, arrowSize, arrowType, true, $"{prefix}:arrow:1");
            AddArrowEvidence(scene, dimEnd, direction, normal, arrowSize, arrowType, false, $"{prefix}:arrow:2");
        }

        AddText(scene, displayedValue, textPosition, textHeight, textRotation, $"{prefix}:text");
    }

    private static void AddDimensionLine(PrimitiveScene scene, PaperPoint start, PaperPoint end,
        PaperPoint textPosition, double textHeight, double arrowSize, bool isBroken, bool microBreak,
        double jitter, string prefix)
    {
        if (!isBroken && !microBreak)
        {
            AddLine(scene, start, end, $"{prefix}:dimline");
            return;
        }

        var direction = Normalize(end - start);
        var length = Distance(start, end);
        var t = Math.Clamp(ProjectionParameter(textPosition, start, end), 0.15, 0.85);
        var center = start + (end - start) * t;
        var desiredGap = isBroken
            ? Math.Max(textHeight * 2.5, arrowSize * 1.25)
            : Math.Max(0.02, jitter * 0.5);
        var gap = Math.Min(desiredGap, length * 0.6);
        var leftEnd = center - direction * (gap / 2.0);
        var rightStart = center + direction * (gap / 2.0);

        if (Distance(start, leftEnd) > 1e-9)
            AddLine(scene, start, leftEnd, $"{prefix}:dimline:left");
        if (Distance(rightStart, end) > 1e-9)
            AddLine(scene, rightStart, end, $"{prefix}:dimline:right");
    }

    private static void AddExtensionLine(PrimitiveScene scene, PaperPoint definitionPoint,
        PaperPoint dimensionEndpoint, double extension, string sourceId)
    {
        var vector = dimensionEndpoint - definitionPoint;
        var length = Math.Sqrt(Dot(vector, vector));
        if (length <= 1e-9)
        {
            AddLine(scene, definitionPoint, definitionPoint + new PaperPoint(0, Math.Max(extension, 0.1)), sourceId);
            return;
        }

        var unit = vector * (1.0 / length);
        AddLine(scene, definitionPoint, dimensionEndpoint + unit * Math.Max(extension, 0.0), sourceId);
    }

    private static void AddArrowEvidence(PrimitiveScene scene, PaperPoint endpoint, PaperPoint direction,
        PaperPoint normal, double requestedSize, ArrowType type, bool first, string sourceId)
    {
        var size = Math.Max(Math.Abs(requestedSize), 0.05);
        var inward = first ? direction : direction * -1.0;

        switch (type)
        {
            case ArrowType.ArchitecturalTick:
            case ArrowType.Oblique:
                AddLine(scene,
                    endpoint - inward * (size * 0.35) - normal * (size * 0.35),
                    endpoint + inward * (size * 0.35) + normal * (size * 0.35),
                    sourceId);
                break;

            case ArrowType.Dot:
                AddLine(scene,
                    endpoint - inward * (size * 0.25) - normal * (size * 0.25),
                    endpoint + inward * (size * 0.25) + normal * (size * 0.25),
                    sourceId + ":a");
                AddLine(scene,
                    endpoint - inward * (size * 0.25) + normal * (size * 0.25),
                    endpoint + inward * (size * 0.25) - normal * (size * 0.25),
                    sourceId + ":b");
                break;

            default:
                AddLine(scene, endpoint, endpoint + inward * (size * 0.80) + normal * (size * 0.45), sourceId + ":a");
                AddLine(scene, endpoint, endpoint + inward * (size * 0.80) - normal * (size * 0.45), sourceId + ":b");
                break;
        }
    }

    private static void AddNearbyDimension(PrimitiveScene scene, DimensionCase testCase)
    {
        var scale = testCase.DrawingScale;
        var p1 = ToPaper(testCase.P1, scale);
        var p2 = ToPaper(testCase.P2, scale);
        var normal = Perpendicular(Normalize(p2 - p1));
        var shift = normal * Math.Max(testCase.TextHeight / scale * 6.0, 8.0);

        AddDimensionEvidence(
            scene,
            p1 + shift,
            p2 + shift,
            ToPaper(testCase.DimensionLinePoint, scale) + shift,
            ToPaper(testCase.TextPosition, scale) + shift,
            testCase.ExpectedValue,
            testCase.TextHeight / scale,
            testCase.ArrowType,
            testCase.ArrowSize / scale,
            testCase.ExtensionLineExtension / scale,
            testCase.Rotation,
            new NoiseSpec(),
            false,
            true,
            true,
            $"{testCase.Id}:nearby");
    }

    private static void AddIntersectingDimension(PrimitiveScene scene, DimensionCase testCase)
    {
        var scale = testCase.DrawingScale;
        var p1 = ToPaper(testCase.P1, scale);
        var p2 = ToPaper(testCase.P2, scale);
        var center = Midpoint(p1, p2);
        var halfLength = Distance(p1, p2) / 2.0;
        var direction = Perpendicular(Normalize(p2 - p1));
        var normal = Perpendicular(direction);
        var q1 = center - direction * halfLength;
        var q2 = center + direction * halfLength;
        var offset = Distance(ToPaper(testCase.DimensionLinePoint, scale), center);
        var dimPoint = center + normal * offset;

        AddDimensionEvidence(scene, q1, q2, dimPoint, dimPoint, testCase.ExpectedValue,
            testCase.TextHeight / scale, testCase.ArrowType, testCase.ArrowSize / scale,
            testCase.ExtensionLineExtension / scale, testCase.Rotation + 90.0, new NoiseSpec(),
            false, true, true, $"{testCase.Id}:intersecting");
    }

    private static void AddTableLikeEvidence(PrimitiveScene scene, PaperPoint center, double height,
        double value, double rotation, string prefix)
    {
        var w = Math.Max(height * 6.0, 6.0);
        var h = Math.Max(height * 3.0, 3.0);
        var left = center.X - w / 2.0;
        var right = center.X + w / 2.0;
        var bottom = center.Y - h / 2.0;
        var top = center.Y + h / 2.0;
        AddLine(scene, new PaperPoint(left, bottom), new PaperPoint(right, bottom), $"{prefix}:row:1");
        AddLine(scene, new PaperPoint(left, top), new PaperPoint(right, top), $"{prefix}:row:2");
        AddLine(scene, new PaperPoint(left, bottom), new PaperPoint(left, top), $"{prefix}:col:1");
        AddLine(scene, new PaperPoint(right, bottom), new PaperPoint(right, top), $"{prefix}:col:2");
        AddLine(scene, new PaperPoint(center.X, bottom), new PaperPoint(center.X, top), $"{prefix}:col:mid");
        AddText(scene, value, center, height, rotation, $"{prefix}:text");
    }

    private static void AddBlockLikeEvidence(PrimitiveScene scene, PaperPoint center, double height,
        double value, double rotation, string prefix)
    {
        var w = Math.Max(height * 5.0, 5.0);
        var h = Math.Max(height * 2.5, 2.5);
        var a = new PaperPoint(center.X - w / 2.0, center.Y - h / 2.0);
        var b = new PaperPoint(center.X + w / 2.0, center.Y - h / 2.0);
        var c = new PaperPoint(center.X + w / 2.0, center.Y + h / 2.0);
        var d = new PaperPoint(center.X - w / 2.0, center.Y + h / 2.0);
        AddLine(scene, a, b, $"{prefix}:block:1");
        AddLine(scene, b, c, $"{prefix}:block:2");
        AddLine(scene, c, d, $"{prefix}:block:3");
        AddLine(scene, d, a, $"{prefix}:block:4");
        AddText(scene, value, center, height, rotation, $"{prefix}:text");
    }

    private static void AddPolylineLikeEvidence(PrimitiveScene scene, PaperPoint center, double height,
        double value, double rotation, string prefix)
    {
        var p0 = center + new PaperPoint(-height * 5, -height * 0.5);
        var p1 = center + new PaperPoint(-height * 2, height * 0.8);
        var p2 = center + new PaperPoint(height, -height * 0.7);
        var p3 = center + new PaperPoint(height * 4, height * 0.5);
        AddLine(scene, p0, p1, $"{prefix}:poly:1");
        AddLine(scene, p1, p2, $"{prefix}:poly:2");
        AddLine(scene, p2, p3, $"{prefix}:poly:3");
        AddText(scene, value, center + new PaperPoint(0, height * 1.5), height, rotation, $"{prefix}:text");
    }

    private static void AddRandomLookalike(PrimitiveScene scene, PaperPoint center, double height,
        double value, double rotation, string prefix)
    {
        AddLine(scene, center + new PaperPoint(-height * 5, -height * 2), center + new PaperPoint(height * 4, height * 1.2), $"{prefix}:random:1");
        AddLine(scene, center + new PaperPoint(-height * 3, height * 3), center + new PaperPoint(height * 2, height * 4), $"{prefix}:random:2");
        AddLine(scene, center + new PaperPoint(height * 3, -height * 4), center + new PaperPoint(height * 4, -height), $"{prefix}:random:3");
        AddText(scene, value, center, height, rotation, $"{prefix}:text");
    }

    private static void AddText(PrimitiveScene scene, double value, PaperPoint position, double height,
        double rotation, string sourceId)
    {
        scene.Texts.Add(new TextPrimitive(
            value.ToString("0.###", CultureInfo.InvariantCulture),
            position.ToCore(),
            Math.Max(height, 1e-6),
            rotation,
            SourceIds: new[] { sourceId }));
    }

    private static void AddLine(PrimitiveScene scene, PaperPoint start, PaperPoint end, string sourceId)
        => scene.Lines.Add(new LinePrimitive(start.ToCore(), end.ToCore(), SourceIds: new[] { sourceId }));

    private static PaperPoint ToPaper(Point2D point, double scale) => new(point.X / scale, point.Y / scale);

    // TEST-001 stores PDFIMPORT-like deltas. First scale the clean DWG point to paper,
    // then apply the observed delta directly in paper units so noise is not divided twice.
    private static PaperPoint ToPaperObserved(Point2D expected, Point2D observed, double scale)
        => new(expected.X / scale + (observed.X - expected.X), expected.Y / scale + (observed.Y - expected.Y));

    private static PaperPoint AddDeterministicPaperNoise(PaperPoint point, DimensionCase testCase, int salt)
    {
        var level = testCase.Noise.CoordinateJitter;
        if (level <= 0) return point;
        return new PaperPoint(
            point.X + StableNoise(testCase.Seed, salt) * level,
            point.Y + StableNoise(testCase.Seed, salt + 1) * level);
    }

    private static double StableNoise(int seed, int salt)
    {
        unchecked
        {
            uint x = (uint)seed ^ ((uint)salt * 0x9E3779B9u);
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return (x / (double)uint.MaxValue) * 2.0 - 1.0;
        }
    }

    private static PaperPoint Rotate(PaperPoint vector, double degrees)
    {
        if (Math.Abs(degrees) <= 1e-12) return vector;
        var radians = degrees * Math.PI / 180.0;
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return Normalize(new PaperPoint(vector.X * c - vector.Y * s, vector.X * s + vector.Y * c));
    }

    private static PaperPoint Normalize(PaperPoint vector)
    {
        var length = Math.Sqrt(Dot(vector, vector));
        return length <= 1e-12 ? new PaperPoint(1, 0) : vector * (1.0 / length);
    }

    private static PaperPoint Perpendicular(PaperPoint vector) => new(-vector.Y, vector.X);
    private static double Dot(PaperPoint a, PaperPoint b) => a.X * b.X + a.Y * b.Y;
    private static double Distance(PaperPoint a, PaperPoint b) => Math.Sqrt(Dot(a - b, a - b));
    private static PaperPoint Midpoint(PaperPoint a, PaperPoint b) => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    private static double ProjectionParameter(PaperPoint p, PaperPoint a, PaperPoint b)
    {
        var ab = b - a;
        var denominator = Dot(ab, ab);
        return denominator <= 1e-12 ? 0.5 : Dot(p - a, ab) / denominator;
    }

    private readonly record struct PaperPoint(double X, double Y)
    {
        public Point2 ToCore() => new(X, Y);
        public static PaperPoint operator +(PaperPoint a, PaperPoint b) => new(a.X + b.X, a.Y + b.Y);
        public static PaperPoint operator -(PaperPoint a, PaperPoint b) => new(a.X - b.X, a.Y - b.Y);
        public static PaperPoint operator *(PaperPoint a, double scalar) => new(a.X * scalar, a.Y * scalar);
    }
}
