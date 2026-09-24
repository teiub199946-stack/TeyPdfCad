using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class DiagnosticReportTests
{
    [Fact]
    public void DetectionMetrics_AreIndependentFromPointPrecision()
    {
        var cases = new List<DiagnosticCaseRecord>
        {
            Record("tp", ExpectedResult.Recognized, ExpectedResult.Recognized, DiagnosticCategory.WrongGeometry,
                detectionCorrect: true, geometryEvaluated: true, geometryCorrect: false,
                valueCorrect: true, scaleCorrect: true, typeCorrect: true, countCorrect: true),
            Record("fn", ExpectedResult.Recognized, ExpectedResult.Rejected, DiagnosticCategory.MissedDetection,
                detectionCorrect: false),
            Record("fp", ExpectedResult.Rejected, ExpectedResult.Recognized, DiagnosticCategory.UnexpectedDetection,
                detectionCorrect: false),
            Record("amb", ExpectedResult.Ambiguous, ExpectedResult.Ambiguous, DiagnosticCategory.ExpectedAbstention,
                detectionCorrect: true, abstentionCorrect: true)
        };

        var report = new DiagnosticReportBuilder().Build(12345, cases);

        Assert.Equal(1, report.TruePositive);
        Assert.Equal(1, report.FalsePositive);
        Assert.Equal(1, report.FalseNegative);
        Assert.Equal(0.5, report.DetectionPrecision, 6);
        Assert.Equal(0.5, report.DetectionRecall, 6);
        Assert.Equal(0.5, report.F1, 6);
        Assert.Equal(1, report.CorrectValue.Correct);
        Assert.Equal(1, report.CorrectValue.Total);
        Assert.Equal(0, report.CorrectGeometry.Correct);
        Assert.Equal(1, report.CorrectGeometry.Total);
        Assert.Equal(1.0, report.AbstentionAccuracy.Rate, 6);
    }

    [Fact]
    public void ErrorPercentiles_AreCalculatedSeparatelyForPaperAndWorld()
    {
        var cases = new[] { 1d, 2d, 3d, 4d, 10d }
            .Select((paper, index) => Record(
                "g" + index,
                ExpectedResult.Recognized,
                ExpectedResult.Recognized,
                index == 4 ? DiagnosticCategory.WrongGeometry : DiagnosticCategory.ExpectedNoisePropagation,
                detectionCorrect: true,
                geometryEvaluated: true,
                geometryCorrect: index != 4,
                valueCorrect: true,
                scaleCorrect: true,
                typeCorrect: true,
                countCorrect: true,
                maxPaperError: paper,
                maxWorldError: paper * 100,
                realDefect: index == 4))
            .ToList();

        var report = new DiagnosticReportBuilder().Build(12345, cases);

        Assert.Equal(3, report.AllGeometryErrorPaper.P50, 6);
        Assert.Equal(10, report.AllGeometryErrorPaper.Max, 6);
        Assert.Equal(300, report.AllGeometryErrorWorld.P50, 6);
        Assert.Equal(1000, report.AllGeometryErrorWorld.Max, 6);
        Assert.Equal(10, report.RealDefectGeometryErrorPaper.P50, 6);
        Assert.Equal(1000, report.RealDefectGeometryErrorWorld.P50, 6);
    }

    [Fact]
    public void CohortRows_AlwaysContainRequiredTEST003Groups()
    {
        var clean = Record("clean", ExpectedResult.Recognized, ExpectedResult.Recognized, DiagnosticCategory.Correct,
            detectionCorrect: true, geometryEvaluated: true, geometryCorrect: true,
            valueCorrect: true, scaleCorrect: true, typeCorrect: true, countCorrect: true);
        var noisyExpected = clean.Expected with
        {
            Id = "noisy",
            Noise = new NoiseSpec { CoordinateJitter = 0.1 },
            Tags = new List<string> { "regular" }
        };
        var noisy = clean with
        {
            Expected = noisyExpected,
            Actual = clean.Actual with { CaseId = "noisy" },
            Diagnostic = clean.Diagnostic with { CaseId = "noisy" }
        };

        var report = new DiagnosticReportBuilder().Build(12345, new List<DiagnosticCaseRecord> { clean, noisy });
        var names = report.Cohorts.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var required in new[]
                 {
                     "clean", "noisy", "broken dimension line", "chains", "outside text",
                     "ambiguous", "negative", "non-canonical scale", "multi-scale"
                 })
        {
            Assert.Contains(required, names);
        }

        Assert.Equal(1, report.Cohorts.Single(x => x.Name == "clean").Total);
        Assert.Equal(1, report.Cohorts.Single(x => x.Name == "noisy").Total);
    }

    [Fact]
    public void SystematicScaleAndPointBiasUseFixedDiagnosticThresholds()
    {
        var records = Enumerable.Range(0, 60)
            .Select(i => Record(
                "bias" + i,
                ExpectedResult.Recognized,
                ExpectedResult.Recognized,
                DiagnosticCategory.WrongGeometry,
                detectionCorrect: true,
                geometryEvaluated: true,
                geometryCorrect: false,
                valueCorrect: true,
                scaleCorrect: i >= 5,
                typeCorrect: true,
                countCorrect: true,
                maxPaperError: 0.01,
                maxWorldError: 1,
                realDefect: true,
                signedDx: 0.001,
                signedDy: 0.0,
                expectedScale: 100,
                actualScale: i < 5 ? 50 : 100))
            .ToList();

        var report = new DiagnosticReportBuilder().Build(12345, records);

        Assert.True(report.PointBias.IsSystematic);
        Assert.True(report.PointBias.MagnitudePaper > DiagnosticReportBuilder.SystematicPointBiasThresholdPaper);
        Assert.True(report.ScaleDiagnostics.WrongScaleCases > 0);
        Assert.True(report.ScaleDiagnostics.HasSystematicScaleError);
    }

    private static DiagnosticCaseRecord Record(
        string id,
        ExpectedResult expectedResult,
        ExpectedResult actualResult,
        DiagnosticCategory category,
        bool detectionCorrect,
        bool geometryEvaluated = false,
        bool geometryCorrect = false,
        bool? valueCorrect = null,
        bool? scaleCorrect = null,
        bool? typeCorrect = null,
        bool? countCorrect = null,
        bool? abstentionCorrect = null,
        double maxPaperError = 0,
        double maxWorldError = 0,
        bool realDefect = false,
        double signedDx = 0,
        double signedDy = 0,
        double expectedScale = 100,
        double? actualScale = null)
    {
        var expected = new DimensionCase
        {
            Id = id,
            Seed = 12345,
            ExpectedResult = expectedResult,
            ExpectedDimensions = expectedResult == ExpectedResult.Recognized ? 1 : 0,
            DimensionType = expectedResult == ExpectedResult.Rejected ? DimensionType.Negative : DimensionType.Linear,
            ExpectedValue = 5200,
            DrawingScale = expectedScale,
            Noise = new NoiseSpec(),
            Tags = new List<string> { "regular" }
        };
        var actual = new ActualDimensionResult
        {
            CaseId = id,
            Result = actualResult,
            DetectedDimensions = actualResult == ExpectedResult.Recognized ? 1 : 0,
            Value = actualResult == ExpectedResult.Recognized ? 5200 : null,
            DimensionType = actualResult == ExpectedResult.Recognized ? DimensionType.Linear : null,
            DrawingScale = actualResult == ExpectedResult.Recognized ? actualScale ?? expectedScale : null
        };
        var diagnostic = new CaseDiagnostic
        {
            CaseId = id,
            Seed = 12345,
            Category = category,
            IsRealCoreDefect = realDefect,
            DetectionCorrect = detectionCorrect,
            GeometryEvaluated = geometryEvaluated,
            GeometryCorrect = geometryCorrect,
            ValueCorrect = valueCorrect,
            ScaleCorrect = scaleCorrect,
            TypeCorrect = typeCorrect,
            CountCorrect = countCorrect,
            AbstentionCorrect = abstentionCorrect,
            MaxPaperError = maxPaperError,
            MaxWorldError = maxWorldError,
            SignedPaperDx = signedDx,
            SignedPaperDy = signedDy
        };
        return new DiagnosticCaseRecord
        {
            Expected = expected,
            Actual = actual,
            Trace = new CaseGeometryTrace { CaseId = id, DrawingScale = expectedScale },
            Diagnostic = diagnostic
        };
    }
}
