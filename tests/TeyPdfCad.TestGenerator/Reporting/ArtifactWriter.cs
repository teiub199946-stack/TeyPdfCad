using System.Text.Json;
using System.Text.Json.Serialization;
using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Reporting;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class ArtifactWriter
{
    public static async Task WriteCorpusAsync(
        TestCorpus corpus,
        string outputDirectory,
        bool splitCases,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "cases.json"),
            JsonDefaults.Serialize(corpus),
            cancellationToken);

        if (!splitCases)
        {
            return;
        }

        var casesDirectory = Path.Combine(outputDirectory, "cases");
        Directory.CreateDirectory(casesDirectory);

        foreach (var testCase in corpus.Cases)
        {
            await File.WriteAllTextAsync(
                Path.Combine(casesDirectory, $"{testCase.Id}.json"),
                JsonDefaults.Serialize(testCase),
                cancellationToken);
        }
    }

    public static async Task WriteReportAsync(
        RegressionReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "report.json"),
            JsonDefaults.Serialize(report),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "report.md"),
            BuildMarkdownReport(report),
            cancellationToken);
    }

    public static async Task SaveBaselineAsync(
        RegressionBaseline baseline,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            outputPath,
            JsonDefaults.Serialize(baseline),
            cancellationToken);
    }

    public static string BuildMarkdownReport(RegressionReport report)
    {
        var writer = new StringWriter();

        writer.WriteLine("# TEST SUMMARY");
        writer.WriteLine();
        writer.WriteLine($"**{report.ReleaseGate.Status}**");
        writer.WriteLine();
        writer.WriteLine("| Metric | Value |");
        writer.WriteLine("|---|---:|");
        writer.WriteLine($"| Total cases | {report.Total} |");
        writer.WriteLine($"| Passed | {report.Passed} |");
        writer.WriteLine($"| Failed | {report.Failed} |");
        writer.WriteLine($"| Expected dimensions | {report.ExpectedDimensions} |");
        writer.WriteLine($"| Expected negatives | {report.ExpectedNegatives} |");
        writer.WriteLine($"| False positive | {report.FalsePositive} |");
        writer.WriteLine($"| False negative | {report.FalseNegative} |");
        writer.WriteLine($"| Wrong geometry | {report.WrongGeometry} |");
        writer.WriteLine($"| Wrong value | {report.WrongValue} |");
        writer.WriteLine($"| Wrong type | {report.WrongType} |");
        writer.WriteLine($"| Wrong scale | {report.WrongScale} |");
        writer.WriteLine($"| Wrong confidence | {report.WrongConfidence} |");
        writer.WriteLine($"| Missing actual | {report.MissingActual} |");
        writer.WriteLine($"| Precision | {report.Precision:P3} |");
        writer.WriteLine($"| Recall | {report.Recall:P3} |");
        writer.WriteLine($"| F1 | {report.F1:P3} |");
        writer.WriteLine($"| Pass rate | {report.PassRate:P3} |");
        writer.WriteLine();

        writer.WriteLine("## Performance");
        writer.WriteLine();
        writer.WriteLine($"- total runtime: {report.Performance.TotalRuntimeSeconds:0.###} s");
        writer.WriteLine($"- cases/sec: {report.Performance.CasesPerSecond:0.##}");
        writer.WriteLine($"- median: {report.Performance.MedianCaseMilliseconds:0.######} ms");
        writer.WriteLine($"- p95: {report.Performance.P95CaseMilliseconds:0.######} ms");
        writer.WriteLine($"- p99: {report.Performance.P99CaseMilliseconds:0.######} ms");
        writer.WriteLine();

        writer.WriteLine("## Worst categories");
        writer.WriteLine();
        writer.WriteLine("| Category | Bucket | Failed | Total | Pass rate |");
        writer.WriteLine("|---|---|---:|---:|---:|");

        foreach (var row in report.Breakdowns
                     .Where(x => x.Total > 0)
                     .OrderBy(x => x.PassRate)
                     .ThenByDescending(x => x.Total)
                     .Take(15))
        {
            writer.WriteLine(
                $"| {Escape(row.Category)} | {Escape(row.Bucket)} | {row.Failed} | {row.Total} | {row.PassRate:P2} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Top 20 failures");
        writer.WriteLine();

        if (report.Failures.Count == 0)
        {
            writer.WriteLine("No failures.");
        }
        else
        {
            foreach (var failure in report.Failures.Take(20))
            {
                writer.WriteLine(
                    $"- `{failure.CaseId}` — **{failure.Outcome}** — {string.Join("; ", failure.Reasons)}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("## Regression compared with baseline");
        writer.WriteLine();

        if (!report.BaselineComparison.HasBaseline)
        {
            writer.WriteLine("No baseline supplied.");
        }
        else
        {
            writer.WriteLine(report.BaselineComparison.IsRegression ? "**REGRESSION**" : "No regression.");
            writer.WriteLine();

            foreach (var delta in report.BaselineComparison.Deltas)
            {
                var marker = delta.IsRegression ? "REGRESSION" : "ok";
                writer.WriteLine(
                    $"- {delta.Metric}: {delta.Baseline:P3} -> {delta.Current:P3} " +
                    $"(delta {delta.Delta:+0.000%;-0.000%;0.000%}) [{marker}]");
            }
        }

        if (report.ReleaseGate.Reasons.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Release gate reasons");
            writer.WriteLine();

            foreach (var reason in report.ReleaseGate.Reasons)
            {
                writer.WriteLine($"- {reason}");
            }
        }

        return writer.ToString();
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
