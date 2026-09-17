using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class SceneTraceTests
{
    [Fact]
    public void SceneTrace_Captures_Clean_Input_And_Injected_Paper_Coordinates_From_Primitives()
    {
        var testCase = NoisyHorizontalCase();
        var scene = new DimensionCasePrimitiveSceneBuilder().Build(testCase);

        var trace = new SceneTraceBuilder().Build(testCase, scene);

        Assert.Equal(testCase.P1, trace.DefinitionPoint1.ExpectedWorld);
        AssertPoint(new Point2D(0, 0), trace.DefinitionPoint1.CleanPaper);
        AssertPoint(new Point2D(0.01, -0.02), trace.DefinitionPoint1.CoreInputPaper!.Value);
        AssertPoint(new Point2D(0.01, -0.02), trace.DefinitionPoint1.InjectedPaperDelta);

        var sceneText = Assert.Single(scene.Texts);
        AssertPoint(new Point2D(sceneText.Position.X, sceneText.Position.Y), trace.TextAnchor.CoreInputPaper!.Value);
        Assert.Contains(trace.SceneProvenanceIds, x => x.Contains(":ext:1", StringComparison.Ordinal));
        Assert.Contains(trace.SceneProvenanceIds, x => x.Contains(":text", StringComparison.Ordinal));
        Assert.Equal(100, trace.DrawingScale);
    }

    [Fact]
    public async Task DetailedCoreRun_Preserves_Core_Paper_Result_And_Provenance()
    {
        var testCase = CleanHorizontalCase();

        var run = await new SemanticCoreTestPipeline().RunDetailedAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, run.Actual.Result);
        Assert.NotNull(run.Trace.CoreResult);
        Assert.NotNull(run.Trace.CoreResult!.DefinitionPoint1Paper);
        Assert.NotEmpty(run.Trace.CoreResult.ProvenanceIds);

        var paper = run.Trace.CoreResult.DefinitionPoint1Paper!.Value;
        var scale = run.Actual.DrawingScale!.Value;
        var world = run.Trace.CoreResult.DefinitionPoint1World!.Value;
        Assert.Equal(paper.X * scale, world.X, 9);
        Assert.Equal(paper.Y * scale, world.Y, 9);
    }

    private static DimensionCase NoisyHorizontalCase()
    {
        var clean = CleanHorizontalCase();
        return clean with
        {
            Id = "trace_noisy_5200",
            Noise = new NoiseSpec
            {
                CoordinateJitter = 0.05,
                TextOffset = new Point2D(0.03, -0.01)
            },
            ObservedGeometry = new ObservedGeometry
            {
                P1 = new Point2D(0.01, -0.02),
                P2 = new Point2D(5200.02, 0.01),
                DimensionLinePoint = new Point2D(2599.99, 350.03),
                TextPosition = new Point2D(2600.02, 349.98)
            }
        };
    }

    private static DimensionCase CleanHorizontalCase() => new()
    {
        Id = "trace_clean_5200",
        Seed = 12345,
        DimensionType = DimensionType.Linear,
        ExpectedValue = 5200,
        P1 = new Point2D(0, 0),
        P2 = new Point2D(5200, 0),
        DimensionLinePoint = new Point2D(2600, 350),
        Rotation = 0,
        TextPosition = new Point2D(2600, 350),
        TextPlacement = TextPlacement.Centered,
        TextHeight = 250,
        ArrowType = ArrowType.ClosedFilled,
        ArrowSize = 250,
        ExtensionLineOffset = 150,
        ExtensionLineExtension = 125,
        DrawingScale = 100,
        ExpectedConfidenceClass = ConfidenceClass.High,
        ExpectedResult = ExpectedResult.Recognized,
        ExpectedDimensions = 1,
        NegativePattern = NegativePattern.None,
        Noise = new NoiseSpec(),
        ObservedGeometry = new ObservedGeometry
        {
            P1 = new Point2D(0, 0),
            P2 = new Point2D(5200, 0),
            DimensionLinePoint = new Point2D(2600, 350),
            TextPosition = new Point2D(2600, 350)
        }
    };

    private static void AssertPoint(Point2D expected, Point2D actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }
}
