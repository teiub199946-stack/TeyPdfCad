using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

public sealed class DiagnosticReportBuilder
{
    // Diagnostic thresholds are fixed before looking at TEST-003 baseline results.
    // They do not alter Core recognition or geometry acceptance.
    public const double SystematicPointBiasThresholdPaper = 0.00005;
    public const int MinimumBiasSampleCount = 50;
    public const double SystematicScaleErrorRateThreshold = 0.05;
    public const int MinimumScaleSampleCount = 20;

    private static readonly HashSet<double> CanonicalScales = new()
    {
        1, 2, 5, 10, 20, 25, 50, 100, 200, 500
    };

    private static readonly string[] RequiredCohorts =
    {
        "clean",
        "noisy",
        "broken dimension line",
        "chains",
        "outside text",
        "ambiguous",
        "negative",
        "non-canonical scale",
        "multi-scale"
    };

    public DiagnosticRegressionReport Build(int seed, IReadOnlyList<DiagnosticCaseRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var stableRecords = records
            .OrderBy(x => x.Expected.Id, StringComparer.Ordinal)
            .ToList();

        var tp = stableRecords.Count(x =>
            x.Expected.ExpectedResult == ExpectedResult.Recognized &&
            x.Actual.Result == ExpectedResult.Recognized);
        var fn = stableRecords.Count(x =>
            x.Expected.ExpectedResult == ExpectedResult.Recognized &&
            x.Actual.Result != ExpectedResult.Recognized);
        var fp = stableRecords.Count(x =>
            x.Expected.ExpectedResult == ExpectedResult.Rejected &&
            x.Actual.Result == ExpectedResult.Recognized);
        var tn = stableRecords.Count(x =>
            x.Expected.ExpectedResult == ExpectedResult.Rejected &&
            x.Actual.Result == ExpectedResult.Rejected);

        var precision = Divide(tp, tp + fp);
        var recall = Divide(tp, tp + fn);
        var f1 = precision + recall <= 0 ? 0 : 2 * precision * recall / (precision + recall);

        var geometryEvaluated = stableRecords.Where(x => x.Diagnostic.GeometryEvaluated).ToList();
        var realGeometryDefects = geometryEvaluated
            .Where(x => x.Diagnostic.Category == DiagnosticCategory.WrongGeometry && x.Diagnostic.IsRealCoreDefect)
            .ToList();

        var ambiguous = stableRecords.Where(x => x.Expected.ExpectedResult == ExpectedResult.Ambiguous).ToList();
        var abstentionCorrect = ambiguous.Count(x => x.Actual.Result == ExpectedResult.Ambiguous);

        var scaleEvaluated = stableRecords.Where(x => x.Diagnostic.ScaleCorrect.HasValue).ToList();
        var wrongScale = scaleEvaluated.Count(x => x.Diagnostic.ScaleCorrect == false);
        var relativeScaleErrors = scaleEvaluated
            .Where(x => x.Actual.DrawingScale is not null && x.Expected.DrawingScale > 0)
            .Select(x => (x.Actual.DrawingScale!.Value - x.Expected.DrawingScale) / x.Expected.DrawingScale)
            .ToList();
        var wrongScaleRate = Divide(wrongScale, scaleEvaluated.Count);

        var biasRecords = geometryEvaluated.ToList();
        var meanDx = biasRecords.Count == 0 ? 0 : biasRecords.Average(x => x.Diagnostic.SignedPaperDx);
        var meanDy = biasRecords.Count == 0 ? 0 : biasRecords.Average(x => x.Diagnostic.SignedPaperDy);
        var biasMagnitude = Math.Sqrt(meanDx * meanDx + meanDy * meanDy);

        var legacy = stableRecords.Where(x => x.Diagnostic.WasLegacyWrongPoints).ToList();
        var legacyReal = legacy.Count(x => x.Diagnostic.IsRealCoreDefect);
        var legacyExplained = legacy.Count(x => x.Diagnostic.Category is
            DiagnosticCategory.ExpectedNoisePropagation or DiagnosticCategory.NumericTolerance);

        return new DiagnosticRegressionReport
        {
            Seed = seed,
            Total = stableRecords.Count,
            TruePositive = tp,
            TrueNegative = tn,
            FalsePositive = fp,
            FalseNegative = fn,
            DetectionPrecision = precision,
            DetectionRecall = recall,
            F1 = f1,
            CorrectValue = Rate(stableRecords, x => x.Diagnostic.ValueCorrect),
            CorrectScale = Rate(stableRecords, x => x.Diagnostic.ScaleCorrect),
            CorrectType = Rate(stableRecords, x => x.Diagnostic.TypeCorrect),
            CorrectCount = Rate(stableRecords, x => x.Diagnostic.CountCorrect),
            CorrectGeometry = new RateMetric
            {
                Correct = geometryEvaluated.Count(x => x.Diagnostic.GeometryCorrect),
                Total = geometryEvaluated.Count,
                Rate = Divide(geometryEvaluated.Count(x => x.Diagnostic.GeometryCorrect), geometryEvaluated.Count)
            },
            AbstentionAccuracy = new RateMetric
            {
                Correct = abstentionCorrect,
                Total = ambiguous.Count,
                Rate = Divide(abstentionCorrect, ambiguous.Count)
            },
            AllGeometryErrorPaper = Percentiles(geometryEvaluated.Select(x => x.Diagnostic.MaxPaperError)),
            AllGeometryErrorWorld = Percentiles(geometryEvaluated.Select(x => x.Diagnostic.MaxWorldError)),
            RealDefectGeometryErrorPaper = Percentiles(realGeometryDefects.Select(x => x.Diagnostic.MaxPaperError)),
            RealDefectGeometryErrorWorld = Percentiles(realGeometryDefects.Select(x => x.Diagnostic.MaxWorldError)),
            PointBias = new PointBiasSummary
            {
                EvaluatedCases = biasRecords.Count,
                MeanDxPaper = meanDx,
                MeanDyPaper = meanDy,
                MagnitudePaper = biasMagnitude,
                IsSystematic = biasRecords.Count >= MinimumBiasSampleCount &&
                               biasMagnitude > SystematicPointBiasThresholdPaper
            },
            ScaleDiagnostics = new ScaleDiagnosticSummary
            {
                EvaluatedCases = scaleEvaluated.Count,
                WrongScaleCases = wrongScale,
                WrongScaleRate = wrongScaleRate,
                MeanSignedRelativeError = relativeScaleErrors.Count == 0 ? 0 : relativeScaleErrors.Average(),
                HasSystematicScaleError = scaleEvaluated.Count >= MinimumScaleSampleCount &&
                                          wrongScaleRate >= SystematicScaleErrorRateThreshold
            },
            LegacyWrongPoints = new LegacyWrongPointsSummary
            {
                Total = legacy.Count,
                RealCoreDefects = legacyReal,
                NoiseOrNumericExplained = legacyExplained,
                Other = legacy.Count - legacyReal - legacyExplained
            },
            Categories = stableRecords
                .GroupBy(x => x.Diagnostic.Category)
                .Select(x => new CategoryCount { Category = x.Key, Count = x.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Category)
                .ToList(),
            Cohorts = BuildCohorts(stableRecords),
            Cases = stableRecords
        };
    }

    private static RateMetric Rate(
        IReadOnlyList<DiagnosticCaseRecord> records,
        Func<DiagnosticCaseRecord, bool?> selector)
    {
        var values = records.Select(selector).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var correct = values.Count(x => x);
        return new RateMetric
        {
            Correct = correct,
            Total = values.Count,
            Rate = Divide(correct, values.Count)
        };
    }

    private static List<CohortStats> BuildCohorts(IReadOnlyList<DiagnosticCaseRecord> records)
    {
        return RequiredCohorts
            .Select(name => BuildCohort(name, records.Where(x => InCohort(name, x.Expected)).ToList()))
            .ToList();
    }

    private static CohortStats BuildCohort(string name, IReadOnlyList<DiagnosticCaseRecord> rows)
    {
        var geometry = rows.Where(x => x.Diagnostic.GeometryEvaluated).ToList();
        var correctGeometry = geometry.Count(x => x.Diagnostic.GeometryCorrect);

        return new CohortStats
        {
            Name = name,
            Total = rows.Count,
            RealCoreDefects = rows.Count(x => x.Diagnostic.IsRealCoreDefect),
            NoiseOrNumericExplained = rows.Count(x => x.Diagnostic.Category is
                DiagnosticCategory.ExpectedNoisePropagation or DiagnosticCategory.NumericTolerance),
            FalsePositive = rows.Count(x =>
                x.Expected.ExpectedResult == ExpectedResult.Rejected &&
                x.Actual.Result == ExpectedResult.Recognized),
            FalseNegative = rows.Count(x =>
                x.Expected.ExpectedResult == ExpectedResult.Recognized &&
                x.Actual.Result != ExpectedResult.Recognized),
            CorrectGeometry = new RateMetric
            {
                Correct = correctGeometry,
                Total = geometry.Count,
                Rate = Divide(correctGeometry, geometry.Count)
            }
        };
    }

    private static bool InCohort(string name, DimensionCase testCase)
    {
        return name switch
        {
            "clean" => IsClean(testCase),
            "noisy" => !IsClean(testCase),
            "broken dimension line" => testCase.IsDimensionLineBroken || testCase.Noise.MicroBreak,
            "chains" => testCase.DimensionType == DimensionType.Chain || HasTag(testCase, "chain"),
            "outside text" => testCase.TextPlacement is TextPlacement.OutsideLeft or TextPlacement.OutsideRight ||
                              HasTag(testCase, "text-far") || HasTag(testCase, "dimension-outside-geometry"),
            "ambiguous" => testCase.ExpectedResult == ExpectedResult.Ambiguous,
            "negative" => testCase.ExpectedResult == ExpectedResult.Rejected,
            "non-canonical scale" => !CanonicalScales.Contains(testCase.DrawingScale),
            "multi-scale" => HasTag(testCase, "multi-scale"),
            _ => false
        };
    }

    private static bool IsClean(DimensionCase testCase)
    {
        return testCase.Noise.CoordinateJitter <= 0 &&
               !testCase.Noise.MicroBreak &&
               !testCase.Noise.EndpointMismatch &&
               Math.Abs(testCase.Noise.AngularSkewDegrees) <= 1e-15 &&
               Math.Abs(testCase.Noise.TextOffset.X) <= 1e-15 &&
               Math.Abs(testCase.Noise.TextOffset.Y) <= 1e-15 &&
               !testCase.IsDimensionLineBroken;
    }

    private static bool HasTag(DimensionCase testCase, string tag)
        => testCase.Tags.Contains(tag, StringComparer.Ordinal);

    private static ErrorPercentiles Percentiles(IEnumerable<double> values)
    {
        var sorted = values
            .Where(double.IsFinite)
            .OrderBy(x => x)
            .ToArray();

        if (sorted.Length == 0)
            return new ErrorPercentiles();

        return new ErrorPercentiles
        {
            Count = sorted.Length,
            P50 = Percentile(sorted, 0.50),
            P90 = Percentile(sorted, 0.90),
            P95 = Percentile(sorted, 0.95),
            P99 = Percentile(sorted, 0.99),
            Max = sorted[^1]
        };
    }

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        var position = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sorted[lower];
        var weight = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * weight;
    }

    private static double Divide(double numerator, double denominator)
        => denominator <= 0 ? 0 : numerator / denominator;
}
