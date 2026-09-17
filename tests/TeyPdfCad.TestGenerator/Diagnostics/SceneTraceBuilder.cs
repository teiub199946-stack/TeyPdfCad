using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

/// <summary>
/// Captures the exact paper-space evidence passed to Semantic Core.
/// Source/provenance ids are used to read coordinates back from the built scene
/// instead of duplicating DimensionCasePrimitiveSceneBuilder's noise math.
/// </summary>
public sealed class SceneTraceBuilder
{
    public CaseGeometryTrace Build(DimensionCase testCase, PrimitiveScene scene)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        ArgumentNullException.ThrowIfNull(scene);

        var scale = testCase.DrawingScale;
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(testCase), "DrawingScale must be finite and positive.");

        var cleanP1 = ToPaper(testCase.P1, scale);
        var cleanP2 = ToPaper(testCase.P2, scale);
        var cleanDimensionLine = ToPaper(testCase.DimensionLinePoint, scale);
        var cleanText = ToPaper(testCase.TextPosition, scale);

        var p1Input = FindDefinitionPoint(scene, testCase, cleanP1, ":ext:1");
        var p2Input = FindDefinitionPoint(scene, testCase, cleanP2, ":ext:2");
        var dimensionLineInput = FindDimensionLineLocation(scene, testCase, cleanDimensionLine);
        var text = FindText(scene, testCase, cleanText);
        Point2D? textInput = text is null ? null : ToPoint(text.Position);

        var provenance = scene.Lines.SelectMany(x => x.ProvenanceIds)
            .Concat(scene.Texts.SelectMany(x => x.ProvenanceIds))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var primaryDimensionLineIds = scene.Lines
            .SelectMany(line => line.ProvenanceIds)
            .Where(id => IsPrimaryEvidence(testCase, id) && id.Contains(":dimline", StringComparison.Ordinal))
            .ToList();

        return new CaseGeometryTrace
        {
            CaseId = testCase.Id,
            DrawingScale = scale,
            InjectedNoise = testCase.Noise,
            DefinitionPoint1 = CreatePointTrace(testCase.P1, cleanP1, p1Input),
            DefinitionPoint2 = CreatePointTrace(testCase.P2, cleanP2, p2Input),
            DimensionLineLocation = CreatePointTrace(testCase.DimensionLinePoint, cleanDimensionLine, dimensionLineInput),
            TextAnchor = CreatePointTrace(testCase.TextPosition, cleanText, textInput),
            ExpectedRotationDegrees = NormalizeDegrees(testCase.Rotation + (testCase.IsTextFlipped ? 180.0 : 0.0)),
            CoreInputTextRotationDegrees = text?.Rotation,
            ExpectedBrokenDimensionLine = testCase.IsDimensionLineBroken || testCase.Noise.MicroBreak,
            CoreInputBrokenDimensionLine = primaryDimensionLineIds.Any(id =>
                id.Contains(":dimline:left", StringComparison.Ordinal) ||
                id.Contains(":dimline:right", StringComparison.Ordinal)),
            SceneProvenanceIds = provenance
        };
    }

    private static GeometryPointTrace CreatePointTrace(
        Point2D expectedWorld,
        Point2D cleanPaper,
        Point2D? inputPaper)
    {
        var delta = inputPaper is null
            ? new Point2D(0, 0)
            : new Point2D(inputPaper.Value.X - cleanPaper.X, inputPaper.Value.Y - cleanPaper.Y);

        return new GeometryPointTrace
        {
            ExpectedWorld = expectedWorld,
            CleanPaper = cleanPaper,
            CoreInputPaper = inputPaper,
            InjectedPaperDelta = delta
        };
    }

    private static Point2D? FindDefinitionPoint(
        PrimitiveScene scene,
        DimensionCase testCase,
        Point2D expectedClean,
        string suffix)
    {
        var candidates = scene.Lines
            .Where(line => line.ProvenanceIds.Any(id =>
                IsPrimaryEvidence(testCase, id) && id.Contains(suffix, StringComparison.Ordinal)))
            .Select(line => ToPoint(line.Start))
            .ToList();

        return candidates.Count == 0
            ? null
            : candidates.OrderBy(point => point.DistanceTo(expectedClean)).First();
    }

    private static Point2D? FindDimensionLineLocation(
        PrimitiveScene scene,
        DimensionCase testCase,
        Point2D expectedClean)
    {
        var candidates = scene.Lines
            .Where(line => line.ProvenanceIds.Any(id =>
                IsPrimaryEvidence(testCase, id) && id.Contains(":dimline", StringComparison.Ordinal)))
            .Select(line => ProjectOntoInfiniteLine(expectedClean, line.Start, line.End))
            .ToList();

        return candidates.Count == 0
            ? null
            : candidates.OrderBy(point => point.DistanceTo(expectedClean)).First();
    }

    private static TextPrimitive? FindText(
        PrimitiveScene scene,
        DimensionCase testCase,
        Point2D expectedClean)
    {
        return scene.Texts
            .Where(text => text.ProvenanceIds.Any(id =>
                IsPrimaryEvidence(testCase, id) && id.Contains(":text", StringComparison.Ordinal)))
            .OrderBy(text => ToPoint(text.Position).DistanceTo(expectedClean))
            .FirstOrDefault();
    }

    private static bool IsPrimaryEvidence(DimensionCase testCase, string sourceId)
    {
        var prefix = testCase.DimensionType == DimensionType.Chain && testCase.Segments.Count > 0
            ? $"{testCase.Id}:chain:"
            : testCase.NegativePattern != NegativePattern.None
                ? $"{testCase.Id}:negative:"
                : $"{testCase.Id}:primary:";

        return sourceId.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static Point2D ProjectOntoInfiniteLine(Point2D expected, Point2 start, Point2 end)
    {
        var ax = start.X;
        var ay = start.Y;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var denominator = dx * dx + dy * dy;
        if (denominator <= 1e-18)
            return new Point2D(ax, ay);

        var t = ((expected.X - ax) * dx + (expected.Y - ay) * dy) / denominator;
        return new Point2D(ax + t * dx, ay + t * dy);
    }

    private static Point2D ToPaper(Point2D point, double scale)
        => new(point.X / scale, point.Y / scale);

    private static Point2D ToPoint(Point2 point)
        => new(point.X, point.Y);

    private static double NormalizeDegrees(double degrees)
    {
        var value = degrees % 360.0;
        return value < 0 ? value + 360.0 : value;
    }
}
