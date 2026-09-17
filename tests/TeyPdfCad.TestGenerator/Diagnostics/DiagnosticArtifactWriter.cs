using System.Globalization;
using System.Text;
using TeyPdfCad.TestGenerator.Reporting;

namespace TeyPdfCad.TestGenerator.Diagnostics;

public static class DiagnosticArtifactWriter
{
    public static async Task WriteAsync(
        DiagnosticRegressionReport report,
        IReadOnlyList<WorstCaseRecord> worstCases,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(worstCases);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "report.json"),
            JsonDefaults.Serialize(report),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "report.md"),
            BuildMarkdown(report, worstCases),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "worst_cases.json"),
            JsonDefaults.Serialize(worstCases),
            cancellationToken);
    }

    public static string BuildMarkdown(
        DiagnosticRegressionReport report,
        IReadOnlyList<WorstCaseRecord> worstCases)
    {
        var b = new StringBuilder();
        b.AppendLine("# TEST-003 Semantic Core diagnostic report");
        b.AppendLine();
        b.AppendLine($"Seed: `{report.Seed}`  ");
        b.AppendLine($"Cases: **{report.Total}**");
        b.AppendLine();

        b.AppendLine("## Detection metrics");
        b.AppendLine();
        b.AppendLine("| Metric | Value |");
        b.AppendLine("|---|---:|");
        b.AppendLine($"| Detection Precision | {report.DetectionPrecision:P4} |");
        b.AppendLine($"| Detection Recall | {report.DetectionRecall:P4} |");
        b.AppendLine($"| F1 | {report.F1:P4} |");
        b.AppendLine($"| FP | {report.FalsePositive} |");
        b.AppendLine($"| FN | {report.FalseNegative} |");
        b.AppendLine();
        b.AppendLine("Classification does not alter expected labels or Core detections, therefore the Precision/Recall change caused only by honest point-error reclassification is **0.000 percentage points**.");
        b.AppendLine();

        b.AppendLine("## Semantic correctness separated from point precision");
        b.AppendLine();
        b.AppendLine("| Metric | Correct | Evaluated | Rate |");
        b.AppendLine("|---|---:|---:|---:|");
        AppendRate(b, "Correct Value", report.CorrectValue);
        AppendRate(b, "Correct Scale", report.CorrectScale);
        AppendRate(b, "Correct Type", report.CorrectType);
        AppendRate(b, "Correct Count", report.CorrectCount);
        AppendRate(b, "Correct Geometry", report.CorrectGeometry);
        AppendRate(b, "Abstention accuracy", report.AbstentionAccuracy);
        b.AppendLine();

        b.AppendLine("## Legacy WrongPoints split");
        b.AppendLine();
        b.AppendLine($"The legacy fixed-`0.05 DWG` rule marked **{report.LegacyWrongPoints.Total}** cases as `WrongPoints`.");
        b.AppendLine();
        b.AppendLine("| Classification | Cases |");
        b.AppendLine("|---|---:|");
        b.AppendLine($"| Real Core defects | {report.LegacyWrongPoints.RealCoreDefects} |");
        b.AppendLine($"| Expected noise propagation | {report.LegacyWrongPoints.ExpectedNoisePropagation} |");
        b.AppendLine($"| Numeric tolerance | {report.LegacyWrongPoints.NumericTolerance} |");
        b.AppendLine($"| Other/unclassified | {report.LegacyWrongPoints.Other} |");
        b.AppendLine();

        b.AppendLine("## Error percentiles");
        b.AppendLine();
        b.AppendLine("### Paper-space error percentiles");
        AppendPercentiles(b, report.AllGeometryErrorPaper);
        b.AppendLine();
        b.AppendLine("### World-space error percentiles");
        AppendPercentiles(b, report.AllGeometryErrorWorld);
        b.AppendLine();

        b.AppendLine("## Systematic diagnostics");
        b.AppendLine();
        b.AppendLine($"- **Scale systematic error:** {(report.ScaleDiagnostics.HasSystematicScaleError ? "YES" : "NO")}; wrong scale {report.ScaleDiagnostics.WrongScaleCases}/{report.ScaleDiagnostics.EvaluatedCases} ({report.ScaleDiagnostics.WrongScaleRate:P4}); mean signed relative scale error {report.ScaleDiagnostics.MeanSignedRelativeError:+0.000000%;-0.000000%;0.000000%}.");
        b.AppendLine($"- **Signed point bias:** dx={Format(report.PointBias.MeanDxPaper)}, dy={Format(report.PointBias.MeanDyPaper)} paper units; magnitude={Format(report.PointBias.MagnitudePaper)}; systematic={(report.PointBias.IsSystematic ? "YES" : "NO")} over {report.PointBias.EvaluatedCases} evaluated cases.");
        b.AppendLine();

        b.AppendLine("## Most frequent real Core defect classes");
        b.AppendLine();
        var defectClasses = report.Cases
            .Where(x => x.Diagnostic.IsRealCoreDefect)
            .GroupBy(x => x.Diagnostic.Category)
            .Select(x => (Category: x.Key, Count: x.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Category)
            .Take(5)
            .ToList();
        if (defectClasses.Count == 0)
        {
            b.AppendLine("No real Core defects were classified.");
        }
        else
        {
            foreach (var row in defectClasses)
                b.AppendLine($"- **{row.Category}** — {row.Count}");
        }
        b.AppendLine();

        b.AppendLine("## Cohorts");
        b.AppendLine();
        b.AppendLine("| Cohort | Total | Real Core defects | Noise/numeric explained | FP | FN | Correct geometry |");
        b.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
        foreach (var cohort in report.Cohorts)
        {
            b.AppendLine($"| {Escape(cohort.Name)} | {cohort.Total} | {cohort.RealCoreDefects} | {cohort.NoiseOrNumericExplained} | {cohort.FalsePositive} | {cohort.FalseNegative} | {cohort.CorrectGeometry.Rate:P3} ({cohort.CorrectGeometry.Correct}/{cohort.CorrectGeometry.Total}) |");
        }
        b.AppendLine();

        b.AppendLine("## Top 20 real Core defects");
        b.AppendLine();
        if (worstCases.Count == 0)
        {
            b.AppendLine("No eligible real Core defects.");
        }
        else
        {
            foreach (var row in worstCases.Take(20))
            {
                b.AppendLine($"- `{row.CaseId}` — **{row.Category}** — scale 1:{Format(row.DrawingScale)} — paper={Format(row.MaxPaperError)}, world={Format(row.MaxWorldError)} — {row.Explanation}");
            }
        }
        b.AppendLine();

        b.AppendLine("## Interpretation");
        b.AppendLine();
        b.AppendLine($"- Of the legacy `WrongPoints` set, **{report.LegacyWrongPoints.RealCoreDefects}** are classified as real Core defects, **{report.LegacyWrongPoints.ExpectedNoisePropagation}** as expected injected-noise propagation, **{report.LegacyWrongPoints.NumericTolerance}** as numeric tolerance, and **{report.LegacyWrongPoints.Other}** remain other/unclassified.");
        b.AppendLine($"- Systematic scale error: **{(report.ScaleDiagnostics.HasSystematicScaleError ? "detected" : "not detected")}**.");
        b.AppendLine($"- Systematic point displacement: **{(report.PointBias.IsSystematic ? "detected" : "not detected")}**.");
        b.AppendLine("- Detection Precision/Recall/F1 are reported independently from geometry precision; noise reclassification never converts a detection into a TP/FN/FP.");

        return b.ToString();
    }

    private static void AppendRate(StringBuilder b, string name, RateMetric metric)
        => b.AppendLine($"| {name} | {metric.Correct} | {metric.Total} | {metric.Rate:P4} |");

    private static void AppendPercentiles(StringBuilder b, ErrorPercentiles p)
    {
        b.AppendLine("| Count | P50 | P90 | P95 | P99 | Max |");
        b.AppendLine("|---:|---:|---:|---:|---:|---:|");
        b.AppendLine($"| {p.Count} | {Format(p.P50)} | {Format(p.P90)} | {Format(p.P95)} | {Format(p.P99)} | {Format(p.Max)} |");
    }

    private static string Format(double value)
        => value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal);
}
