using System.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;

namespace TeyPdfCad.TestGenerator.Reporting;

public sealed class RegressionRunner
{
    public async Task<RegressionReport> RunAsync(
        TestCorpus corpus,
        ISemanticTestPipeline pipeline,
        TestConfig config,
        RegressionBaseline? baseline = null,
        CancellationToken cancellationToken = default)
    {
        var comparisons = new List<CaseComparison>(corpus.Cases.Count);
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var testCase in corpus.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var caseStopwatch = Stopwatch.StartNew();
            var actual = await pipeline.RunAsync(testCase, cancellationToken);
            caseStopwatch.Stop();

            comparisons.Add(Compare(
                testCase,
                actual,
                config,
                caseStopwatch.Elapsed.TotalMilliseconds));
        }

        totalStopwatch.Stop();
        return BuildReport(corpus, comparisons, totalStopwatch.Elapsed.TotalSeconds, config, baseline);
    }

    private static CaseComparison Compare(
        DimensionCase expected,
        ActualDimensionResult actual,
        TestConfig config,
        double elapsedMilliseconds)
    {
        var reasons = new List<string>();
        var semanticTypeAmbiguity = IsAlignedRotatedSemanticAmbiguity(expected, actual);

        CaseComparison Fail(ComparisonOutcome outcome, string reason)
        {
            reasons.Add(reason);
            return new CaseComparison
            {
                CaseId = expected.Id,
                Outcome = outcome,
                Passed = false,
                SemanticTypeAmbiguity = semanticTypeAmbiguity,
                ElapsedMilliseconds = elapsedMilliseconds,
                Reasons = reasons
            };
        }

        if (actual.IsMissing)
        {
            return Fail(ComparisonOutcome.MissingActual, "Actual result is missing.");
        }

        if (expected.ExpectedResult == ExpectedResult.Rejected)
        {
            if (actual.Result == ExpectedResult.Rejected)
            {
                return Pass(expected.Id, elapsedMilliseconds, semanticTypeAmbiguity);
            }

            if (actual.Result == ExpectedResult.Ambiguous)
            {
                return Fail(
                    ComparisonOutcome.AmbiguousMismatch,
                    "Expected Rejected but actual was Ambiguous.");
            }

            return Fail(
                ComparisonOutcome.FalsePositive,
                $"Expected Rejected but actual was {actual.Result}.");
        }

        if (expected.ExpectedResult == ExpectedResult.Ambiguous)
        {
            if (actual.Result == ExpectedResult.Ambiguous)
            {
                return Pass(expected.Id, elapsedMilliseconds, semanticTypeAmbiguity);
            }

            return Fail(
                ComparisonOutcome.AmbiguousMismatch,
                $"Expected Ambiguous but actual was {actual.Result}.");
        }

        if (actual.Result == ExpectedResult.Rejected)
        {
            return Fail(ComparisonOutcome.FalseNegative, "Expected a dimension but the actual result was Rejected.");
        }

        if (actual.Result == ExpectedResult.Ambiguous)
        {
            return Fail(
                ComparisonOutcome.AmbiguousMismatch,
                "Expected Recognized but actual was Ambiguous.");
        }

        if (actual.DetectedDimensions != expected.ExpectedDimensions)
        {
            return Fail(
                ComparisonOutcome.WrongCount,
                $"Expected {expected.ExpectedDimensions} dimensions, actual {actual.DetectedDimensions}.");
        }

        if (actual.DimensionType is null)
        {
            return Fail(
                ComparisonOutcome.WrongType,
                $"Expected type {expected.DimensionType}, actual <null>.");
        }

        if (actual.DimensionType != expected.DimensionType && !semanticTypeAmbiguity)
        {
            return Fail(
                ComparisonOutcome.WrongType,
                $"Expected type {expected.DimensionType}, actual {actual.DimensionType}.");
        }

        if (actual.Value is null || Math.Abs(actual.Value.Value - expected.ExpectedValue) > config.ValueToleranceAbsolute)
        {
            return Fail(
                ComparisonOutcome.WrongValue,
                $"Expected value {expected.ExpectedValue:0.###}, actual {actual.Value?.ToString("0.###") ?? "<null>"}.");
        }

        if (actual.P1 is null || actual.P2 is null || actual.DimensionLinePoint is null)
        {
            return Fail(ComparisonOutcome.WrongPoints, "Actual geometry points are incomplete.");
        }

        if (!PointsMatch(expected, actual, config.PointTolerance))
        {
            return Fail(
                ComparisonOutcome.WrongPoints,
                $"Definition points exceed tolerance {config.PointTolerance:0.######}.");
        }

        if (actual.DrawingScale is null ||
            Math.Abs(actual.DrawingScale.Value - expected.DrawingScale) > config.ScaleTolerance)
        {
            return Fail(
                ComparisonOutcome.WrongScale,
                $"Expected scale 1:{expected.DrawingScale:0.###}, actual {actual.DrawingScale?.ToString("0.###") ?? "<null>"}.");
        }

        if (actual.ConfidenceClass is null ||
            actual.ConfidenceClass != expected.ExpectedConfidenceClass)
        {
            return Fail(
                ComparisonOutcome.WrongConfidence,
                $"Expected confidence {expected.ExpectedConfidenceClass}, actual {actual.ConfidenceClass?.ToString() ?? "<null>"}.");
        }

        return Pass(expected.Id, elapsedMilliseconds, semanticTypeAmbiguity);
    }

    private static bool IsAlignedRotatedSemanticAmbiguity(
        DimensionCase expected,
        ActualDimensionResult actual)
    {
        if (!actual.IsDimensionTypeAmbiguous || actual.DimensionType is null)
        {
            return false;
        }

        return (expected.DimensionType == DimensionType.Rotated && actual.DimensionType == DimensionType.Aligned)
            || (expected.DimensionType == DimensionType.Aligned && actual.DimensionType == DimensionType.Rotated);
    }

    private static CaseComparison Pass(
        string caseId,
        double elapsedMilliseconds,
        bool semanticTypeAmbiguity = false)
    {
        return new CaseComparison
        {
            CaseId = caseId,
            Outcome = ComparisonOutcome.Correct,
            Passed = true,
            SemanticTypeAmbiguity = semanticTypeAmbiguity,
            ElapsedMilliseconds = elapsedMilliseconds
        };
    }

    private static bool PointsMatch(
        DimensionCase expected,
        ActualDimensionResult actual,
        double tolerance)
    {
        var actualP1 = actual.P1!.Value;
        var actualP2 = actual.P2!.Value;
        var actualDimLine = actual.DimensionLinePoint!.Value;

        var direct = Math.Max(
            expected.P1.DistanceTo(actualP1),
            expected.P2.DistanceTo(actualP2));

        var swapped = Math.Max(
            expected.P1.DistanceTo(actualP2),
            expected.P2.DistanceTo(actualP1));

        var endPointError = Math.Min(direct, swapped);
        var dimLineError = expected.DimensionLinePoint.DistanceTo(actualDimLine);
        return endPointError <= tolerance && dimLineError <= tolerance;
    }

    private static RegressionReport BuildReport(
        TestCorpus corpus,
        IReadOnlyList<CaseComparison> comparisons,
        double totalRuntimeSeconds,
        TestConfig config,
        RegressionBaseline? baseline)
    {
        var byId = comparisons.ToDictionary(x => x.CaseId, StringComparer.Ordinal);

        var expectedPositiveCases = corpus.Cases
            .Where(x => x.ExpectedResult == ExpectedResult.Recognized)
            .ToList();

        var expectedNegativeCases = corpus.Cases
            .Where(x => x.ExpectedResult == ExpectedResult.Rejected)
            .ToList();

        var falsePositive = Count(comparisons, ComparisonOutcome.FalsePositive);
        var falseNegative = Count(comparisons, ComparisonOutcome.FalseNegative);
        var wrongValue = Count(comparisons, ComparisonOutcome.WrongValue);
        var wrongPoints = Count(comparisons, ComparisonOutcome.WrongPoints);
        var wrongCount = Count(comparisons, ComparisonOutcome.WrongCount);
        var wrongType = Count(comparisons, ComparisonOutcome.WrongType);
        var wrongScale = Count(comparisons, ComparisonOutcome.WrongScale);
        var wrongConfidence = Count(comparisons, ComparisonOutcome.WrongConfidence);
        var missingActual = Count(comparisons, ComparisonOutcome.MissingActual);
        var semanticTypeAmbiguity = comparisons.Count(x => x.SemanticTypeAmbiguity);
        var passed = comparisons.Count(x => x.Passed);
        var failed = comparisons.Count - passed;

        var missingPositive = expectedPositiveCases.Count(
            x => byId[x.Id].Outcome == ComparisonOutcome.MissingActual);

        var ambiguousPositive = expectedPositiveCases.Count(
            x => byId[x.Id].Outcome == ComparisonOutcome.AmbiguousMismatch);

        // Detection precision/recall measure whether a dimension was actually recognized.
        // Semantic mismatches (wrong value/points/type/scale/confidence/count) still count
        // as detected positives, but Rejected, Missing and Ambiguous do not.
        var truePositive = expectedPositiveCases.Count - falseNegative - missingPositive - ambiguousPositive;
        var trueNegative = expectedNegativeCases.Count(x => byId[x.Id].Passed);

        var precisionDenominator = truePositive + falsePositive;
        var recallDenominator = truePositive + falseNegative + missingPositive + ambiguousPositive;
        var precision = SafeDivide(truePositive, precisionDenominator);
        var recall = SafeDivide(truePositive, recallDenominator);
        var f1 = precision + recall <= 0 ? 0 : (2 * precision * recall) / (precision + recall);
        var passRate = SafeDivide(passed, comparisons.Count);
        var falsePositiveRate = SafeDivide(falsePositive, expectedNegativeCases.Count);
        var expectedDimensionCount = expectedPositiveCases.Sum(x => x.ExpectedDimensions);
        var wrongMeasurementRate = SafeDivide(wrongValue, expectedDimensionCount);

        var sortedTimes = comparisons
            .Select(x => x.ElapsedMilliseconds)
            .OrderBy(x => x)
            .ToArray();

        var performance = new PerformanceMetrics
        {
            TotalRuntimeSeconds = totalRuntimeSeconds,
            CasesPerSecond = totalRuntimeSeconds <= 0 ? 0 : comparisons.Count / totalRuntimeSeconds,
            MedianCaseMilliseconds = Percentile(sortedTimes, 0.50),
            P95CaseMilliseconds = Percentile(sortedTimes, 0.95),
            P99CaseMilliseconds = Percentile(sortedTimes, 0.99)
        };

        var reportWithoutBaseline = new RegressionReport
        {
            Seed = corpus.Seed,
            Total = comparisons.Count,
            Passed = passed,
            Failed = failed,
            ExpectedDimensions = expectedDimensionCount,
            ExpectedNegatives = expectedNegativeCases.Count,
            Correct = passed,
            TruePositive = truePositive,
            TrueNegative = trueNegative,
            FalsePositive = falsePositive,
            FalseNegative = falseNegative,
            WrongGeometry = wrongPoints + wrongCount,
            WrongValue = wrongValue,
            WrongType = wrongType,
            WrongScale = wrongScale,
            WrongConfidence = wrongConfidence,
            WrongCount = wrongCount,
            MissingActual = missingActual,
            SemanticTypeAmbiguity = semanticTypeAmbiguity,
            Precision = precision,
            Recall = recall,
            F1 = f1,
            PassRate = passRate,
            FalsePositiveRate = falsePositiveRate,
            WrongMeasurementRate = wrongMeasurementRate,
            Performance = performance,
            Breakdowns = BuildBreakdowns(corpus, byId),
            Failures = comparisons.Where(x => !x.Passed).ToList()
        };

        var baselineComparison = BaselineEvaluator.Compare(reportWithoutBaseline, baseline, config);
        var gate = ReleaseGateEvaluator.Evaluate(reportWithoutBaseline, baselineComparison, config);

        return reportWithoutBaseline with
        {
            BaselineComparison = baselineComparison,
            ReleaseGate = gate
        };
    }

    private static List<BreakdownRow> BuildBreakdowns(
        TestCorpus corpus,
        IReadOnlyDictionary<string, CaseComparison> byId)
    {
        (string Category, Func<DimensionCase, string> Selector)[] dimensions =
        {
            ("angle", x => $"{x.Rotation:0.###}°"),
            ("scale", x => $"1:{x.DrawingScale:0.###}"),
            ("dimensionType", x => x.DimensionType.ToString()),
            ("arrowType", x => x.ArrowType.ToString()),
            ("textPosition", x => x.TextPlacement.ToString()),
            ("noiseLevel", x => x.Noise.CoordinateJitter.ToString("0.###")),
            ("dimensionLength", x => LengthBucket(x.ExpectedValue))
        };

        var result = new List<BreakdownRow>();

        foreach (var (category, selector) in dimensions)
        {
            foreach (var group in corpus.Cases.GroupBy(selector, StringComparer.Ordinal))
            {
                var groupComparisons = group.Select(x => byId[x.Id]).ToList();
                var total = groupComparisons.Count;
                var groupPassed = groupComparisons.Count(x => x.Passed);

                result.Add(new BreakdownRow
                {
                    Category = category,
                    Bucket = group.Key,
                    Total = total,
                    Passed = groupPassed,
                    Failed = total - groupPassed,
                    FalsePositive = Count(groupComparisons, ComparisonOutcome.FalsePositive),
                    FalseNegative = Count(groupComparisons, ComparisonOutcome.FalseNegative),
                    WrongValue = Count(groupComparisons, ComparisonOutcome.WrongValue),
                    WrongGeometry =
                        Count(groupComparisons, ComparisonOutcome.WrongPoints) +
                        Count(groupComparisons, ComparisonOutcome.WrongCount),
                    PassRate = SafeDivide(groupPassed, total)
                });
            }
        }

        return result
            .OrderBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.Bucket, StringComparer.Ordinal)
            .ToList();
    }

    private static string LengthBucket(double value)
    {
        return value switch
        {
            < 100 => "<100",
            < 1000 => "100-999",
            < 5000 => "1000-4999",
            < 10000 => "5000-9999",
            _ => ">=10000"
        };
    }

    private static int Count(IEnumerable<CaseComparison> comparisons, ComparisonOutcome outcome)
    {
        return comparisons.Count(x => x.Outcome == outcome);
    }

    private static double SafeDivide(double numerator, double denominator)
    {
        return denominator <= 0 ? 0 : numerator / denominator;
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var position = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var weight = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight);
    }
}

public static class BaselineEvaluator
{
    public static BaselineComparison Compare(
        RegressionReport report,
        RegressionBaseline? baseline,
        TestConfig config)
    {
        if (baseline is null)
        {
            return new BaselineComparison();
        }

        var deltas = new List<MetricDelta>
        {
            CreateDropDelta("precision", baseline.Precision, report.Precision, config.MaxPrecisionDrop),
            CreateDropDelta("recall", baseline.Recall, report.Recall, config.MaxRecallDrop),
            CreateDropDelta("f1", baseline.F1, report.F1, Math.Max(config.MaxRecallDrop, config.MaxPrecisionDrop)),
            CreateDropDelta("passRate", baseline.PassRate, report.PassRate, config.MaxPassRateDrop),
            CreateIncreaseDelta(
                "falsePositiveRate",
                baseline.FalsePositiveRate,
                report.FalsePositiveRate,
                config.MaxFalsePositiveRate),
            CreateIncreaseDelta(
                "wrongMeasurementRate",
                baseline.WrongMeasurementRate,
                report.WrongMeasurementRate,
                config.MaxWrongMeasurementRate)
        };

        return new BaselineComparison
        {
            HasBaseline = true,
            IsRegression = deltas.Any(x => x.IsRegression),
            Deltas = deltas
        };
    }

    public static RegressionBaseline FromReport(RegressionReport report)
    {
        return new RegressionBaseline
        {
            Seed = report.Seed,
            Precision = report.Precision,
            Recall = report.Recall,
            F1 = report.F1,
            PassRate = report.PassRate,
            FalsePositiveRate = report.FalsePositiveRate,
            WrongMeasurementRate = report.WrongMeasurementRate
        };
    }

    private static MetricDelta CreateDropDelta(
        string metric,
        double baseline,
        double current,
        double allowedDrop)
    {
        var delta = current - baseline;
        return new MetricDelta
        {
            Metric = metric,
            Baseline = baseline,
            Current = current,
            Delta = delta,
            IsRegression = delta < -allowedDrop
        };
    }

    private static MetricDelta CreateIncreaseDelta(
        string metric,
        double baseline,
        double current,
        double allowedIncrease)
    {
        var delta = current - baseline;
        return new MetricDelta
        {
            Metric = metric,
            Baseline = baseline,
            Current = current,
            Delta = delta,
            IsRegression = delta > allowedIncrease
        };
    }
}

public static class ReleaseGateEvaluator
{
    public static ReleaseGateResult Evaluate(
        RegressionReport report,
        BaselineComparison baselineComparison,
        TestConfig config)
    {
        var reasons = new List<string>();

        if (report.FalsePositiveRate > config.MaxFalsePositiveRate)
        {
            reasons.Add(
                $"False-positive rate {report.FalsePositiveRate:P3} exceeds {config.MaxFalsePositiveRate:P3}.");
        }

        if (report.WrongMeasurementRate > config.MaxWrongMeasurementRate)
        {
            reasons.Add(
                $"Wrong-measurement rate {report.WrongMeasurementRate:P3} exceeds {config.MaxWrongMeasurementRate:P3}.");
        }

        if (report.MissingActual > 0)
        {
            reasons.Add($"{report.MissingActual} actual result(s) are missing.");
        }

        if (config.FailOnBaselineRegression && baselineComparison.IsRegression)
        {
            reasons.Add("REGRESSION against baseline.");
        }

        return new ReleaseGateResult
        {
            Status = reasons.Count == 0 ? "PASS" : "FAIL",
            Reasons = reasons
        };
    }
}
