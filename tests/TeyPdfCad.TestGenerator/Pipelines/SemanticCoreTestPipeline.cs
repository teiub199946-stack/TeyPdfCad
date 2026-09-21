using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Pipelines;

/// <summary>
/// Executes TEST-001 cases against the real CAD-neutral Semantic Core.
/// It never returns the expected answer merely because the case says so.
/// TEST-003 additionally exposes a test-only paper/world geometry trace.
/// </summary>
public sealed class SemanticCoreTestPipeline : ISemanticTestPipeline
{
    private readonly DimensionCasePrimitiveSceneBuilder _sceneBuilder;
    private readonly SemanticReconstructionEngine _engine;
    private readonly SceneTraceBuilder _traceBuilder;

    public SemanticCoreTestPipeline(
        DimensionCasePrimitiveSceneBuilder? sceneBuilder = null,
        SemanticReconstructionEngine? engine = null,
        SceneTraceBuilder? traceBuilder = null)
    {
        _sceneBuilder = sceneBuilder ?? new DimensionCasePrimitiveSceneBuilder();
        _engine = engine ?? new SemanticReconstructionEngine();
        _traceBuilder = traceBuilder ?? new SceneTraceBuilder();
    }

    public async ValueTask<ActualDimensionResult> RunAsync(
        DimensionCase testCase,
        CancellationToken cancellationToken = default)
    {
        var run = await RunDetailedAsync(testCase, cancellationToken);
        return run.Actual;
    }

    public ValueTask<SemanticCoreDiagnosticRun> RunDetailedAsync(
        DimensionCase testCase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scene = _sceneBuilder.Build(testCase);
        var trace = _traceBuilder.Build(testCase, scene);
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
            var rejected = new ActualDimensionResult
            {
                CaseId = testCase.Id,
                Result = ExpectedResult.Rejected,
                DetectedDimensions = 0,
                ConfidenceClass = ConfidenceClass.None,
                SuppressionEvidenceEligible = false,
                Diagnostics = diagnostics
            };

            return ValueTask.FromResult(new SemanticCoreDiagnosticRun
            {
                Actual = rejected,
                Trace = trace with { CoreResult = new CoreGeometrySnapshot() }
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
        Point2 paperP1;
        Point2 paperP2;
        Point2 paperDimensionLinePoint;
        DimensionType dimensionType;
        double confidence;
        var isDimensionTypeAmbiguous = false;
        List<string> provenance;

        if (useChain)
        {
            value = chain!.TotalDisplayedValue;
            drawingScale = chain.DrawingScale;
            confidence = chain.Confidence;
            (paperP1, paperP2) = FarthestDefinitionEndpointsPaper(chain.Dimensions);
            paperDimensionLinePoint = Midpoint(chain.Dimensions[0].DimensionLinePoint, chain.Dimensions[^1].DimensionLinePoint);
            dimensionType = DimensionType.Chain;
            provenance = chain.Dimensions
                .SelectMany(x => x.ProvenanceIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            diagnostics.Add($"Mapped as chain with {chain.Dimensions.Count} members.");
        }
        else
        {
            value = representative.DisplayedValue;
            drawingScale = representative.DrawingScale;
            confidence = representative.Confidence;
            paperP1 = representative.DefinitionPoint1;
            paperP2 = representative.DefinitionPoint2;
            paperDimensionLinePoint = representative.DimensionLinePoint;
            dimensionType = MapType(representative);
            provenance = representative.ProvenanceIds
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            isDimensionTypeAmbiguous = IsAlignedRotatedVisuallyAmbiguous(scene, representative);

            if (isDimensionTypeAmbiguous)
            {
                diagnostics.Add(
                    "Semantic type ambiguity: exploded non-axis primitives are compatible with both aligned and rotated native dimensions.");
            }
        }

        var p1 = ToDrawing(paperP1, drawingScale);
        var p2 = ToDrawing(paperP2, drawingScale);
        var dimensionLinePoint = ToDrawing(paperDimensionLinePoint, drawingScale);
        var sourceText = useChain ? null : representative.SourceText;
        var coreText = FindCoreText(scene, provenance, sourceText, paperDimensionLinePoint);
        var textPaper = coreText?.Position;
        Point2D? textWorld = textPaper is null ? null : ToDrawing(textPaper.Value, drawingScale);
        var broken = provenance.Any(id =>
            id.Contains(":dimline:left", StringComparison.Ordinal) ||
            id.Contains(":dimline:right", StringComparison.Ordinal));

        var actual = new ActualDimensionResult
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
            IsDimensionTypeAmbiguous = isDimensionTypeAmbiguous,
            SuppressionEvidenceEligible = dimensions.Any(HasP0SuppressionEvidence),
            Diagnostics = diagnostics
        };

        var snapshot = new CoreGeometrySnapshot
        {
            DefinitionPoint1Paper = ToDiagnostic(paperP1),
            DefinitionPoint2Paper = ToDiagnostic(paperP2),
            DimensionLinePointPaper = ToDiagnostic(paperDimensionLinePoint),
            TextAnchorPaper = textPaper is null ? null : ToDiagnostic(textPaper.Value),
            DefinitionPoint1World = p1,
            DefinitionPoint2World = p2,
            DimensionLinePointWorld = dimensionLinePoint,
            TextAnchorWorld = textWorld,
            TextRotationDegrees = coreText?.Rotation,
            BrokenDimensionLine = broken,
            ProvenanceIds = provenance
        };

        return ValueTask.FromResult(new SemanticCoreDiagnosticRun
        {
            Actual = actual,
            Trace = trace with { CoreResult = snapshot }
        });
    }

    private static TextPrimitive? FindCoreText(
        PrimitiveScene scene,
        IReadOnlyCollection<string> provenance,
        string? sourceText,
        Point2 reference)
    {
        var provenanceSet = provenance.ToHashSet(StringComparer.Ordinal);
        var candidates = scene.Texts
            .Where(text => text.ProvenanceIds.Any(provenanceSet.Contains))
            .ToList();

        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(sourceText))
        {
            candidates = scene.Texts
                .Where(text => string.Equals(text.Value, sourceText, StringComparison.Ordinal))
                .ToList();
        }

        return candidates
            .OrderBy(text => Distance(text.Position, reference))
            .FirstOrDefault();
    }

    private static bool IsAlignedRotatedVisuallyAmbiguous(
        PrimitiveScene scene,
        DimensionCandidate candidate)
    {
        if (candidate.Kind != DimensionKind.Aligned)
            return false;

        var definitionVector = Subtract(candidate.DefinitionPoint2, candidate.DefinitionPoint1);
        var definitionLength = Length(definitionVector);
        if (definitionLength <= 1e-9)
            return false;

        var provenance = candidate.ProvenanceIds.ToHashSet(StringComparer.Ordinal);
        var dimensionLine = scene.Lines
            .Where(line => line.ProvenanceIds.Any(id =>
                provenance.Contains(id) && id.Contains(":dimline", StringComparison.Ordinal)))
            .OrderByDescending(line => Distance(line.Start, line.End))
            .FirstOrDefault();

        if (dimensionLine is null)
            return false;

        var lineVector = Subtract(dimensionLine.End, dimensionLine.Start);
        var lineLength = Length(lineVector);
        if (lineLength <= 1e-9)
            return false;

        var cosine = Math.Abs(Dot(
            Scale(definitionVector, 1.0 / definitionLength),
            Scale(lineVector, 1.0 / lineLength)));

        return cosine >= Math.Cos(Math.PI / 180.0);
    }

    private static bool HasP0SuppressionEvidence(DimensionCandidate candidate)
    {
        var declaredSourceIds = candidate.ProvenanceIds
            .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
            .ToArray();
        if (declaredSourceIds.Length == 0)
            return false;

        // Use the real production planner as the authority for claim/role
        // readiness. Synthetic source geometry is sufficient here because
        // this TEST-003 metric asks whether the candidate's identity/claim
        // contract could reach planner eligibility, not whether source/native
        // appearance equivalence is complete.
        var sources = declaredSourceIds
            .Select((sourceId, index) => (VectorEntity)new VectorLine(
                sourceId,
                new Point2(index * 10d, 0d),
                new Point2(index * 10d + 1d, 0d),
                new VectorStyle()))
            .ToArray();
        var semantics = new SemanticReconstructionResult(
            [candidate],
            [],
            candidate.DrawingScale,
            candidate.Confidence);
        var plan = new SourceReplacementPlanner().BuildPlan(
            sources,
            semantics,
            hatchRecognition: null,
            pageNumber: 1);
        var candidateId = SourceReplacementPlanner.GetCandidateKey(
            candidate,
            pageNumber: 1);

        return !plan.DeferredCandidateKeys.Contains(candidateId)
            && plan.SourceCoverageMap.Values.Any(candidateIds =>
                candidateIds.Contains(candidateId, StringComparer.Ordinal))
            && plan.EligibleSourceIds.Count > 0;
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

    private static (Point2 P1, Point2 P2) FarthestDefinitionEndpointsPaper(
        IReadOnlyList<DimensionCandidate> dimensions)
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

        return (bestA, bestB);
    }

    private static Point2 Midpoint(Point2 a, Point2 b)
        => new((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);

    private static Point2 Subtract(Point2 a, Point2 b)
        => new(a.X - b.X, a.Y - b.Y);

    private static Point2 Scale(Point2 p, double scalar)
        => new(p.X * scalar, p.Y * scalar);

    private static double Dot(Point2 a, Point2 b)
        => a.X * b.X + a.Y * b.Y;

    private static double Length(Point2 p)
        => Math.Sqrt(Dot(p, p));

    private static double Distance(Point2 a, Point2 b)
        => Length(Subtract(a, b));

    private static Point2D ToDrawing(Point2 point, double scale)
        => new(point.X * scale, point.Y * scale);

    private static Point2D ToDiagnostic(Point2 point)
        => new(point.X, point.Y);
}
