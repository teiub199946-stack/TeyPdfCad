using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Generation;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Reporting;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class Test003DoDTests
{
    [Fact]
    public void WorstCaseSelector_ExcludesNoiseAndNumericCases()
    {
        var report = new DiagnosticRegressionReport
        {
            Cases = new List<DiagnosticCaseRecord>
            {
                Record("noise", DiagnosticCategory.ExpectedNoisePropagation, false, 99),
                Record("numeric", DiagnosticCategory.NumericTolerance, false, 98),
                Record("geometry", DiagnosticCategory.WrongGeometry, true, 10),
                Record("scale", DiagnosticCategory.WrongScale, true, 0),
                Record("missed", DiagnosticCategory.MissedDetection, true, 0)
            }
        };

        var selected = new WorstCaseSelector().Select(report, 20);

        Assert.DoesNotContain(selected, x => x.CaseId is "noise" or "numeric");
        Assert.Equal(new[] { "missed", "scale", "geometry" }, selected.Select(x => x.CaseId));
    }

    [Fact]
    public async Task SameSeed_ProducesByteStableDiagnosticReportAndWorstCases()
    {
        var generator = new DimensionCaseGenerator();
        var firstCorpus = generator.Generate(40, 12345);
        var secondCorpus = generator.Generate(40, 12345);
        var runner = new DiagnosticRunner();
        var selector = new WorstCaseSelector();
        var config = new TestConfig();

        var first = await runner.RunAsync(firstCorpus, config);
        var second = await runner.RunAsync(secondCorpus, config);

        Assert.Equal(JsonDefaults.Serialize(first), JsonDefaults.Serialize(second));
        Assert.Equal(
            JsonDefaults.Serialize(selector.Select(first, 20)),
            JsonDefaults.Serialize(selector.Select(second, 20)));
    }

    [Fact]
    public async Task DiagnosticRunner_DoesNotMutateExpectedLabelsOrCaseCount()
    {
        var corpus = new DimensionCaseGenerator().Generate(100, 12345);
        var before = JsonDefaults.Serialize(corpus);
        var expectedLabels = corpus.Cases
            .Select(x => (x.Id, x.ExpectedResult, x.ExpectedDimensions, x.ExpectedValue, x.DimensionType))
            .ToArray();

        var report = await new DiagnosticRunner().RunAsync(corpus, new TestConfig());

        Assert.Equal(100, report.Total);
        Assert.Equal(100, report.Cases.Count);
        Assert.Equal(before, JsonDefaults.Serialize(corpus));
        Assert.Equal(expectedLabels, corpus.Cases
            .Select(x => (x.Id, x.ExpectedResult, x.ExpectedDimensions, x.ExpectedValue, x.DimensionType))
            .ToArray());
    }

    [Fact]
    public void GeometryTolerance_DoesNotSoftenAtScaleOneOrFiveHundred()
    {
        var scale1 = GeometryTolerance.Calculate(Input(scale: 1));
        var scale500 = GeometryTolerance.Calculate(Input(scale: 500));

        Assert.Equal(DiagnosticCategory.WrongGeometry, scale1.Category);
        Assert.Equal(DiagnosticCategory.WrongGeometry, scale500.Category);
        Assert.True(scale500.AllowedPaperError <= scale1.AllowedPaperError);
        Assert.InRange(scale1.AllowedPaperError, 0.0100059, 0.0100061);
        Assert.InRange(scale500.AllowedPaperError, 0.0100049, 0.0100051);
    }

    private static PointDiagnosticInput Input(double scale)
    {
        const double injected = 0.01;
        const double actual = 0.01002;
        return new PointDiagnosticInput
        {
            ExpectedWorld = new Point2D(0, 0),
            CleanPaper = new Point2D(0, 0),
            CoreInputPaper = new Point2D(injected, 0),
            ActualCorePaper = new Point2D(actual, 0),
            ActualWorld = new Point2D(actual * scale, 0),
            DrawingScale = scale
        };
    }

    private static DiagnosticCaseRecord Record(
        string id,
        DiagnosticCategory category,
        bool realDefect,
        double paperError)
    {
        return new DiagnosticCaseRecord
        {
            Expected = new DimensionCase
            {
                Id = id,
                Seed = 12345,
                ExpectedResult = ExpectedResult.Recognized,
                ExpectedValue = 100,
                DrawingScale = 100,
                DimensionType = DimensionType.Linear,
                Noise = new NoiseSpec()
            },
            Actual = new ActualDimensionResult
            {
                CaseId = id,
                Result = ExpectedResult.Recognized,
                Value = 100,
                DrawingScale = 100,
                DimensionType = DimensionType.Linear,
                DetectedDimensions = 1
            },
            Diagnostic = new CaseDiagnostic
            {
                CaseId = id,
                Seed = 12345,
                Category = category,
                IsRealCoreDefect = realDefect,
                MaxPaperError = paperError,
                MaxWorldError = paperError * 100,
                Reasons = new List<string> { $"{category} synthetic reason" }
            }
        };
    }
}
