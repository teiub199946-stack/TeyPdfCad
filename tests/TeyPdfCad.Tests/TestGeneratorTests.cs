using TeyPdfCad.TestGenerator.Generation;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class TestGeneratorTests
{
    [Fact]
    public void SameSeedProducesByteEquivalentCorpusJson()
    {
        var generator = new DimensionCaseGenerator();

        var first = generator.Generate(1000, 12345);
        var second = generator.Generate(1000, 12345);

        Assert.Equal(JsonDefaults.Serialize(first), JsonDefaults.Serialize(second));
    }

    [Fact]
    public void CorpusContainsRequiredCoverageClasses()
    {
        var corpus = new DimensionCaseGenerator().Generate(2000, 12345);

        Assert.Contains(corpus.Cases, x => x.ExpectedResult == ExpectedResult.Rejected);
        Assert.Contains(corpus.Cases, x => x.ExpectedResult == ExpectedResult.Ambiguous);
        Assert.Contains(corpus.Cases, x => x.DimensionType == DimensionType.Chain && x.ExpectedDimensions == 20);
        Assert.Contains(corpus.Cases, x => x.DimensionType == DimensionType.Chain && x.Tags.Contains("equal-length"));
        Assert.Contains(corpus.Cases, x => x.DimensionType == DimensionType.Chain && x.Tags.Contains("mixed-length"));
        Assert.Contains(corpus.Cases, x => x.Noise.CoordinateJitter >= 0.5);
        Assert.Contains(corpus.Cases, x => x.Tags.Contains("broken-dimension-line"));
        Assert.Contains(corpus.Cases, x => x.TextPlacement == TextPlacement.OutsideRight);

        foreach (var arrowType in Enum.GetValues<ArrowType>())
        {
            Assert.Contains(corpus.Cases, x => x.ArrowType == arrowType);
        }
    }

    [Fact]
    public void LargeCorpusHasSustainedNegativeChainAndAmbiguousCoverage()
    {
        var corpus = new DimensionCaseGenerator().Generate(10000, 12345);
        var negatives = corpus.Cases.Count(x => x.ExpectedResult == ExpectedResult.Rejected);
        var chains = corpus.Cases.Count(x => x.DimensionType == DimensionType.Chain);
        var ambiguous = corpus.Cases.Count(x => x.ExpectedResult == ExpectedResult.Ambiguous);

        Assert.True(negatives >= 2400, $"Expected at least 2400 negative cases, got {negatives}.");
        Assert.True(chains >= 950, $"Expected at least 950 chain cases, got {chains}.");
        Assert.True(ambiguous >= 450, $"Expected at least 450 ambiguous cases, got {ambiguous}.");
    }

    [Fact]
    public async Task RunnerClassifiesFalsePositiveAndFalseNegative()
    {
        var source = new DimensionCaseGenerator().Generate(100, 9876);
        var positive = source.Cases.First(x => x.ExpectedResult == ExpectedResult.Recognized);
        var negative = source.Cases.First(x => x.ExpectedResult == ExpectedResult.Rejected);

        var corpus = new TestCorpus
        {
            Seed = source.Seed,
            Cases = new List<DimensionCase> { positive, negative }
        };

        var actual = new List<ActualDimensionResult>
        {
            new()
            {
                CaseId = positive.Id,
                Result = ExpectedResult.Rejected
            },
            new()
            {
                CaseId = negative.Id,
                Result = ExpectedResult.Recognized,
                DetectedDimensions = 1
            }
        };

        var report = await new RegressionRunner().RunAsync(
            corpus,
            new JsonActualResultPipeline(actual),
            new TestConfig());

        Assert.Equal(1, report.FalseNegative);
        Assert.Equal(1, report.FalsePositive);
        Assert.Equal("FAIL", report.ReleaseGate.Status);
    }

    [Fact]
    public async Task AmbiguousActualDoesNotInflateTruePositiveOrFalsePositiveMetrics()
    {
        var source = new DimensionCaseGenerator().Generate(100, 24680);
        var positive = source.Cases.First(x => x.ExpectedResult == ExpectedResult.Recognized);
        var negative = source.Cases.First(x => x.ExpectedResult == ExpectedResult.Rejected);

        var corpus = new TestCorpus
        {
            Seed = source.Seed,
            Cases = new List<DimensionCase> { positive, negative }
        };

        var actual = new List<ActualDimensionResult>
        {
            new()
            {
                CaseId = positive.Id,
                Result = ExpectedResult.Ambiguous
            },
            new()
            {
                CaseId = negative.Id,
                Result = ExpectedResult.Ambiguous
            }
        };

        var report = await new RegressionRunner().RunAsync(
            corpus,
            new JsonActualResultPipeline(actual),
            new TestConfig());

        Assert.Equal(0, report.TruePositive);
        Assert.Equal(0, report.TrueNegative);
        Assert.Equal(0, report.FalsePositive);
        Assert.Equal(0, report.FalseNegative);
        Assert.Equal(0, report.Precision);
        Assert.Equal(0, report.Recall);
        Assert.Equal(2, report.Failed);
        Assert.All(report.Failures, x => Assert.Equal(ComparisonOutcome.AmbiguousMismatch, x.Outcome));
    }

    [Fact]
    public void BaselineEvaluatorDetectsRecallRegression()
    {
        var report = new RegressionReport
        {
            Precision = 0.99,
            Recall = 0.95,
            F1 = 0.969,
            PassRate = 0.95,
            FalsePositiveRate = 0.001,
            WrongMeasurementRate = 0
        };

        var baseline = new RegressionBaseline
        {
            Precision = 0.99,
            Recall = 0.98,
            F1 = 0.985,
            PassRate = 0.98,
            FalsePositiveRate = 0.001,
            WrongMeasurementRate = 0
        };

        var comparison = BaselineEvaluator.Compare(
            report,
            baseline,
            new TestConfig
            {
                MaxRecallDrop = 0.001,
                MaxPrecisionDrop = 0.001,
                MaxPassRateDrop = 0.001
            });

        Assert.True(comparison.IsRegression);
        Assert.Contains(comparison.Deltas, x => x.Metric == "recall" && x.IsRegression);
    }
}
