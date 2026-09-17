using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class SemanticAmbiguityTests
{
    [Fact]
    public async Task Aligned_Rotated_Visual_Ambiguity_Is_Reported_Separately_Not_As_WrongType()
    {
        var testCase = CreateRotatedCase();
        var actual = CreateMatchingActual(testCase) with
        {
            DimensionType = DimensionType.Aligned,
            IsDimensionTypeAmbiguous = true
        };

        var report = await new RegressionRunner().RunAsync(
            new TestCorpus { Seed = 12345, Cases = new List<DimensionCase> { testCase } },
            new FixedPipeline(actual),
            new TestConfig());

        Assert.Equal(0, report.WrongType);
        Assert.Equal(1, report.SemanticTypeAmbiguity);
        Assert.Equal(1, report.Passed);
    }

    [Fact]
    public async Task NonAmbiguous_Aligned_Rotated_Mismatch_Remains_WrongType()
    {
        var testCase = CreateRotatedCase();
        var actual = CreateMatchingActual(testCase) with
        {
            DimensionType = DimensionType.Aligned,
            IsDimensionTypeAmbiguous = false
        };

        var report = await new RegressionRunner().RunAsync(
            new TestCorpus { Seed = 12345, Cases = new List<DimensionCase> { testCase } },
            new FixedPipeline(actual),
            new TestConfig());

        Assert.Equal(1, report.WrongType);
        Assert.Equal(0, report.SemanticTypeAmbiguity);
        Assert.Equal(0, report.Passed);
    }

    private static DimensionCase CreateRotatedCase() => new()
    {
        Id = "ambiguity_rotated_45",
        Seed = 12345,
        DimensionType = DimensionType.Rotated,
        ExpectedValue = 5200,
        P1 = new Point2D(0, 0),
        P2 = new Point2D(3676.95526217, 3676.95526217),
        DimensionLinePoint = new Point2D(1591.0, 2086.0),
        Rotation = 45,
        TextPosition = new Point2D(1591.0, 2086.0),
        TextHeight = 250,
        DrawingScale = 100,
        ExpectedConfidenceClass = ConfidenceClass.High,
        ExpectedResult = ExpectedResult.Recognized,
        ExpectedDimensions = 1,
        ObservedGeometry = new ObservedGeometry
        {
            P1 = new Point2D(0, 0),
            P2 = new Point2D(3676.95526217, 3676.95526217),
            DimensionLinePoint = new Point2D(1591.0, 2086.0),
            TextPosition = new Point2D(1591.0, 2086.0)
        }
    };

    private static ActualDimensionResult CreateMatchingActual(DimensionCase testCase) => new()
    {
        CaseId = testCase.Id,
        Result = ExpectedResult.Recognized,
        DetectedDimensions = 1,
        Value = testCase.ExpectedValue,
        P1 = testCase.P1,
        P2 = testCase.P2,
        DimensionLinePoint = testCase.DimensionLinePoint,
        DrawingScale = testCase.DrawingScale,
        ConfidenceClass = testCase.ExpectedConfidenceClass
    };

    private sealed class FixedPipeline(ActualDimensionResult actual) : ISemanticTestPipeline
    {
        public ValueTask<ActualDimensionResult> RunAsync(
            DimensionCase testCase,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(actual);
    }
}
