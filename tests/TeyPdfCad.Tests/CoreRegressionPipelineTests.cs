using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class CoreRegressionPipelineTests
{
    [Fact]
    public async Task Clean_5200_At_1_100_RoundTrips_Through_Real_Core()
    {
        var testCase = CleanHorizontalCase();

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Recognized, actual.Result);
        Assert.Equal(1, actual.DetectedDimensions);
        Assert.Equal(5200, actual.Value!.Value, 3);
        Assert.Equal(100, actual.DrawingScale!.Value, 6);
        Assert.NotNull(actual.P1);
        Assert.NotNull(actual.P2);
        Assert.True(
            (actual.P1!.Value.DistanceTo(testCase.P1) < 0.1 && actual.P2!.Value.DistanceTo(testCase.P2) < 0.1)
            || (actual.P1!.Value.DistanceTo(testCase.P2) < 0.1 && actual.P2!.Value.DistanceTo(testCase.P1) < 0.1));
    }

    [Fact]
    public void SceneBuilder_Converts_Dwg_Coordinates_To_Paper_Coordinates()
    {
        var testCase = CleanHorizontalCase();

        var scene = new DimensionCasePrimitiveSceneBuilder().Build(testCase);

        Assert.Contains(scene.Lines, line => Math.Abs(line.Start.X) < 1e-9 && Math.Abs(line.End.X - 52.0) < 1e-6);
        var text = Assert.Single(scene.Texts);
        Assert.Equal("5200", text.Value);
        Assert.Equal(2.5, text.Height, 6);
        Assert.Equal(26.0, text.Position.X, 6);
        Assert.Equal(3.5, text.Position.Y, 6);
    }

    [Fact]
    public async Task TextNearOrdinaryLine_NegativePattern_Is_Not_Recognized()
    {
        var testCase = CleanHorizontalCase() with
        {
            DimensionType = DimensionType.Negative,
            ExpectedResult = ExpectedResult.Rejected,
            ExpectedDimensions = 0,
            ExpectedConfidenceClass = ConfidenceClass.None,
            NegativePattern = NegativePattern.TextNearOrdinaryLine
        };

        var actual = await new SemanticCoreTestPipeline().RunAsync(testCase);

        Assert.Equal(ExpectedResult.Rejected, actual.Result);
        Assert.Equal(0, actual.DetectedDimensions);
    }

    private static DimensionCase CleanHorizontalCase()
    {
        return new DimensionCase
        {
            Id = "core_contract_5200",
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
    }
}
