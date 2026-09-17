using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics.Dimensions;
using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Pipelines;

/// <summary>
/// Executes TEST-001 cases against the real CAD-neutral Semantic Core.
/// It never returns the expected answer merely because the case says so.
/// </summary>
public sealed class SemanticCoreTestPipeline : ISemanticTestPipeline
{
    private readonly DimensionCasePrimitiveSceneBuilder _sceneBuilder;
    private readonly SemanticReconstructionEngine _engine;

    public SemanticCoreTestPipeline(
        DimensionCasePrimitiveSceneBuilder? sceneBuilder = null,
        SemanticReconstructionEngine? engine = null)
    {
        _sceneBuilder = sceneBuilder ?? new DimensionCasePrimitiveSceneBuilder();
        _engine = engine ?? new SemanticReconstructionEngine();
    }

    public ValueTask<ActualDimensionResult> RunAsync(
        DimensionCase testCase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scene = _sceneBuilder.Build(testCase);
        var semantic = _engine.Analyze(scene);
        var dimensions = semantic.Dimensions;
        var diagnostics = new List<string>
        {
            $"Core dimensions: {dimensions.Count}.",
            $"Core chains: {semantic.DimensionChains.Count}."
        };

        if (semantic.DetectedDrawingScales.Count > 0)
            diagnostics.Add("Detected scales: " + string.Join(", ", semantic.DetectedDrawingScales.Select(x => x.ToString("0.######"))) + ".");

        if (dimensions.Count == 0)
        {
            return ValueTask.FromResult(new ActualDimensionResult
            {
                CaseId = testCase.Id,
                Result = ExpectedResult.Rejected,
                DetectedDimensions = 0,
                ConfidenceClass = ConfidenceClass.None,
                Diagnostics = diagnostics
            });
        }

        var chain = semantic.DimensionChains
            .OrderByDescending(x => x.Dimensions.Count)
            .ThenByDescending(x => x.Confidence)
            .FirstOrDefault();

        var useChain = chain is not null && chain.Dimensions.Count == dimensions.Count && dimensions.Count > 1;
        var representative = dimensions
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.DimensionLinePoint.Y)
            .ThenBy(x => x.DimensionLinePoint.X)
            .First();

        double value;
        double drawingScale;
        Point2D p1;
        Point2D p2;
        Point2D dimensionLinePoint;
        DimensionType dimensionType;
        double confidence;

        if (useChain)
        {
            value = chain!.TotalDisplayedValue;
            drawingScale = chain.DrawingScale;
            confidence = chain.Confidence;
            (p1, p2) = FarthestDefinitionEndpoints(chain.Dimensions, drawingScale);
            dimensionLinePoint = ToDrawing(
                Midpoint(chain.Dimensions[0].DimensionLinePoint, chain.Dimensions[^1].DimensionLinePoint),
                drawingScale);
            dimensionType = DimensionType.Chain;
            diagnostics.Add($"Mapped as chain with {chain.Dimensions.Count} members.");
        }
        else
        {
            value = representative.DisplayedValue;
            drawingScale = representative.DrawingScale;
            confidence = representative.Confidence;
            p1 = ToDrawing(representative.DefinitionPoint1, drawingScale);
            p2 = ToDrawing(representative.DefinitionPoint2, drawingScale);
            dimensionLinePoint = ToDrawing(representative.DimensionLinePoint, drawingScale);
            dimensionType = MapType(representative);
        }

        return ValueTask.FromResult(new ActualDimensionResult
        {
            CaseId = testCase.Id,
            Result = ExpectedResult.Recognized,
            DetectedDimensions = dimensions.Count,
            Value = value,
            P1 = p1,
            P2 = p2,
            DimensionLinePoint = dimensionLinePoint,
            DimensionType = dimensionType,
            DrawingScale = drawingScale,
            ConfidenceClass = MapConfidence(confidence),
            Diagnostics = diagnostics
        });
    }

    private static DimensionType MapType(DimensionCandidate candidate)
        => candidate.Kind == DimensionKind.Aligned ? DimensionType.Aligned : DimensionType.Linear;

    private static ConfidenceClass MapConfidence(double confidence)
    {
        if (confidence >= 0.90) return ConfidenceClass.High;
        if (confidence >= 0.75) return ConfidenceClass.Medium;
        if (confidence > 0) return ConfidenceClass.Low;
        return ConfidenceClass.None;
    }

    private static (Point2D P1, Point2D P2) FarthestDefinitionEndpoints(
        IReadOnlyList<DimensionCandidate> dimensions,
        double drawingScale)
    {
        var points = dimensions
            .SelectMany(x => new[] { x.DefinitionPoint1, x.DefinitionPoint2 })
            .ToArray();

        var bestA = points[0];
        var bestB = points[0];
        var bestDistanceSquared = -1.0;

        for (var i = 0; i < points.Length; i++)
        for (var j = i + 1; j < points.Length; j++)
        {
            var dx = points[i].X - points[j].X;
            var dy = points[i].Y - points[j].Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared <= bestDistanceSquared) continue;
            bestDistanceSquared = distanceSquared;
            bestA = points[i];
            bestB = points[j];
        }

        return (ToDrawing(bestA, drawingScale), ToDrawing(bestB, drawingScale));
    }

    private static Point2 Midpoint(Point2 a, Point2 b)
        => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    private static Point2D ToDrawing(Point2 point, double scale)
        => new(point.X * scale, point.Y * scale);
}
