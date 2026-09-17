using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Generation;
using TeyPdfCad.TestGenerator.Pipelines;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class Core002ATraceAndBrokenLineReproTests
{
    [Fact]
    public async Task Case_000032_TraceFlagsWrongGeometry_AlthoughCoreReturnsExactSelectedLineMidpoint()
    {
        var testCase = new DimensionCaseGenerator().Generate(33, 12345).Cases.Single(x => x.Id == "case_000032");
        var scene = new DimensionCasePrimitiveSceneBuilder().Build(testCase);
        var run = await new SemanticCoreTestPipeline().RunDetailedAsync(testCase);
        var diagnostic = new ErrorClassifier().Classify(testCase, run.Actual, run.Trace);

        Assert.Equal(DiagnosticCategory.WrongGeometry, diagnostic.Category);
        Assert.NotNull(run.Trace.CoreResult);
        Assert.NotNull(run.Trace.CoreResult!.DimensionLinePointPaper);
        Assert.NotNull(run.Trace.DimensionLineLocation.CoreInputPaper);

        var selectedProvenance = run.Trace.CoreResult.ProvenanceIds.ToHashSet(StringComparer.Ordinal);
        var selectedDimensionLine = scene.Lines.Single(line =>
            line.ProvenanceIds.Any(selectedProvenance.Contains) &&
            line.ProvenanceIds.Any(id => id.Contains(":dimline", StringComparison.Ordinal)));

        var midpoint = new Models.Point2D(
            (selectedDimensionLine.Start.X + selectedDimensionLine.End.X) / 2.0,
            (selectedDimensionLine.Start.Y + selectedDimensionLine.End.Y) / 2.0);

        var corePoint = run.Trace.CoreResult.DimensionLinePointPaper!.Value;
        var tracedInputPoint = run.Trace.DimensionLineLocation.CoreInputPaper!.Value;

        // Production Core reports the exact midpoint of the dimension-line evidence it selected.
        Assert.True(midpoint.DistanceTo(corePoint) <= 1e-12,
            $"Core point must equal selected line midpoint. distance={midpoint.DistanceTo(corePoint):G17}");

        // TEST-003's current trace stores a projection of the clean expected point instead.
        // Both points lie on the same supplied line, but their along-line coordinates differ.
        Assert.True(midpoint.DistanceTo(tracedInputPoint) > GeometryTolerance.NumericPaperEpsilon,
            "Repro requires the TEST-003 trace point to differ from the actual selected-line midpoint.");

        var wrongComponents = diagnostic.GeometryChecks
            .Where(x => x.Status == GeometryCheckStatus.Wrong)
            .Select(x => x.Component)
            .ToArray();

        Assert.Equal(new[] { "dimension-line location" }, wrongComponents);
    }

    [Fact]
    public async Task Case_000150_MicroBreak_ShouldUseMergedDimensionLineEvidence()
    {
        var testCase = new DimensionCaseGenerator().Generate(151, 12345).Cases.Single(x => x.Id == "case_000150");
        Assert.True(testCase.Noise.MicroBreak || testCase.IsDimensionLineBroken);

        var run = await new SemanticCoreTestPipeline().RunDetailedAsync(testCase);
        Assert.Equal(Models.ExpectedResult.Recognized, run.Actual.Result);
        Assert.NotNull(run.Trace.CoreResult);

        var provenance = run.Trace.CoreResult!.ProvenanceIds;

        // RED on the baseline Core: it currently selects one fragment only (":dimline:right")
        // even though DimensionLineCandidates can construct a merged line from both fragments.
        Assert.Contains(provenance, id => id.Contains(":dimline:left", StringComparison.Ordinal));
        Assert.Contains(provenance, id => id.Contains(":dimline:right", StringComparison.Ordinal));
    }
}
