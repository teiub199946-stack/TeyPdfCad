using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeyPdfCad.TestGenerator.Generation;
using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;
using TeyPdfCad.TestGenerator.Reporting;

namespace TeyPdfCad.TestGenerator;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
            var cli = new CliArgs(args.Skip(1).ToArray());
            var config = await LoadConfigAsync(cli.Get("--config"));

            return command switch
            {
                "generate" => await GenerateAsync(cli, config),
                "self-check" => await SelfCheckAsync(cli, config),
                "core-run" => await CoreRunAsync(cli, config),
                "compare" => await CompareAsync(cli, config),
                "baseline" => await BaselineAsync(cli),
                "verify" => await VerifyAsync(cli, config),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> GenerateAsync(CliArgs cli, TestConfig config)
    {
        var count = cli.GetInt("--count", config.DefaultCount);
        var seed = cli.GetInt("--seed", config.DefaultSeed);
        var output = cli.Get("--output") ?? "artifacts/generated";
        var split = cli.Has("--split");

        var corpus = new DimensionCaseGenerator().Generate(count, seed);
        await ArtifactWriter.WriteCorpusAsync(corpus, output, split);

        Console.WriteLine($"Generated {corpus.Cases.Count} deterministic cases.");
        Console.WriteLine($"Seed: {seed}");
        Console.WriteLine($"Output: {Path.GetFullPath(output)}");
        return 0;
    }

    private static async Task<int> SelfCheckAsync(CliArgs cli, TestConfig config)
    {
        var count = cli.GetInt("--count", config.DefaultCount);
        var seed = cli.GetInt("--seed", config.DefaultSeed);
        var output = cli.Get("--output") ?? "artifacts/generated/self-check";
        var baseline = await LoadBaselineAsync(cli.Get("--baseline"));

        var corpus = new DimensionCaseGenerator().Generate(count, seed);
        await ArtifactWriter.WriteCorpusAsync(corpus, output, cli.Has("--split"));

        var report = await new RegressionRunner().RunAsync(
            corpus,
            new ExpectedEchoPipeline(),
            config,
            baseline);

        await ArtifactWriter.WriteReportAsync(report, output);

        Console.WriteLine($"Harness self-check: {report.ReleaseGate.Status}");
        Console.WriteLine($"Cases: {report.Total}, pass: {report.Passed}, fail: {report.Failed}");
        Console.WriteLine("NOTE: ExpectedEchoPipeline validates the test harness only; it is not Semantic Core accuracy.");
        return report.ReleaseGate.Status == "PASS" ? 0 : 2;
    }

    private static async Task<int> CoreRunAsync(CliArgs cli, TestConfig config)
    {
        var count = cli.GetInt("--count", config.DefaultCount);
        var seed = cli.GetInt("--seed", config.DefaultSeed);
        var output = cli.Get("--output") ?? "artifacts/generated/core-run";
        var baselinePath = cli.Get("--baseline");
        var baseline = await LoadBaselineAsync(baselinePath);

        var corpus = new DimensionCaseGenerator().Generate(count, seed);
        await ArtifactWriter.WriteCorpusAsync(corpus, output, cli.Has("--split"));

        var report = await new RegressionRunner().RunAsync(
            corpus,
            new SemanticCoreTestPipeline(),
            config,
            baseline);

        await ArtifactWriter.WriteReportAsync(report, output);

        Console.WriteLine($"Semantic Core regression: {report.ReleaseGate.Status}");
        Console.WriteLine($"Cases: {report.Total}; pass: {report.Passed}; fail: {report.Failed}");
        Console.WriteLine($"Precision: {report.Precision:P3}; Recall: {report.Recall:P3}; F1: {report.F1:P3}");
        Console.WriteLine($"FP: {report.FalsePositive}; FN: {report.FalseNegative}; Wrong measurement: {report.WrongValue}");
        Console.WriteLine($"Semantic type ambiguity: {report.SemanticTypeAmbiguity}");
        Console.WriteLine($"Report: {Path.GetFullPath(Path.Combine(output, "report.json"))}");

        // An initial measurement is allowed to be poor: without an accepted baseline this
        // command records the truth and exits successfully so CI can upload the artifacts.
        // Once --baseline is supplied, the existing release gate becomes enforceable.
        if (string.IsNullOrWhiteSpace(baselinePath))
        {
            return 0;
        }

        return report.ReleaseGate.Status == "PASS" ? 0 : 2;
    }

    private static async Task<int> CompareAsync(CliArgs cli, TestConfig config)
    {
        var casesPath = cli.Require("--cases");
        var actualPath = cli.Require("--actual");
        var output = cli.Get("--output") ?? "artifacts/generated/compare";
        var baseline = await LoadBaselineAsync(cli.Get("--baseline"));

        var corpus = JsonSerializer.Deserialize<TestCorpus>(
            await File.ReadAllTextAsync(casesPath),
            JsonDefaults.Options) ?? throw new InvalidDataException("Unable to deserialize test corpus.");

        var actualResults = JsonSerializer.Deserialize<List<ActualDimensionResult>>(
            await File.ReadAllTextAsync(actualPath),
            JsonDefaults.Options) ?? throw new InvalidDataException("Unable to deserialize actual results.");

        var report = await new RegressionRunner().RunAsync(
            corpus,
            new JsonActualResultPipeline(actualResults),
            config,
            baseline);

        await ArtifactWriter.WriteReportAsync(report, output);

        Console.WriteLine($"Regression run: {report.ReleaseGate.Status}");
        Console.WriteLine($"Precision: {report.Precision:P3}; Recall: {report.Recall:P3}; F1: {report.F1:P3}");
        Console.WriteLine($"Report: {Path.GetFullPath(Path.Combine(output, "report.json"))}");
        return report.ReleaseGate.Status == "PASS" ? 0 : 2;
    }

    private static async Task<int> BaselineAsync(CliArgs cli)
    {
        var reportPath = cli.Require("--report");
        var output = cli.Get("--output") ?? "baseline.json";

        var report = JsonSerializer.Deserialize<RegressionReport>(
            await File.ReadAllTextAsync(reportPath),
            JsonDefaults.Options) ?? throw new InvalidDataException("Unable to deserialize report.");

        await ArtifactWriter.SaveBaselineAsync(BaselineEvaluator.FromReport(report), output);
        Console.WriteLine($"Baseline saved: {Path.GetFullPath(output)}");
        return 0;
    }

    private static async Task<int> VerifyAsync(CliArgs cli, TestConfig config)
    {
        var root = cli.Get("--output") ?? "artifacts/generated/verification";
        var generator = new DimensionCaseGenerator();

        foreach (var count in new[] { 100, 1000, 10000 })
        {
            var corpus = generator.Generate(count, config.DefaultSeed);
            var output = Path.Combine(root, count.ToString());
            await ArtifactWriter.WriteCorpusAsync(corpus, output, splitCases: false);
            Console.WriteLine($"Generated verification corpus: {count} cases.");
        }

        const int deterministicSeed = 12345;
        var first = generator.Generate(1000, deterministicSeed);
        var second = generator.Generate(1000, deterministicSeed);
        var firstJson = JsonDefaults.Serialize(first);
        var secondJson = JsonDefaults.Serialize(second);

        if (!string.Equals(firstJson, secondJson, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Determinism check failed for seed 12345.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstJson)));
        var deterministicDirectory = Path.Combine(root, "determinism");
        Directory.CreateDirectory(deterministicDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(deterministicDirectory, "seed-12345.sha256"),
            hash + Environment.NewLine);

        var corpus10000 = generator.Generate(10000, config.DefaultSeed);
        var report = await new RegressionRunner().RunAsync(
            corpus10000,
            new ExpectedEchoPipeline(),
            config);

        var reportDirectory = Path.Combine(root, "10000");
        await ArtifactWriter.WriteReportAsync(report, reportDirectory);

        Console.WriteLine($"Determinism seed 12345: PASS ({hash})");
        Console.WriteLine($"10,000-case harness self-check: {report.ReleaseGate.Status}");
        Console.WriteLine("NOTE: This verifies generator/runner plumbing, not Semantic Core recognition accuracy.");
        return report.ReleaseGate.Status == "PASS" ? 0 : 2;
    }

    private static async Task<TestConfig> LoadConfigAsync(string? path)
    {
        var resolved = path ?? Path.Combine("tests", "TeyPdfCad.TestGenerator", "testconfig.json");
        if (!File.Exists(resolved))
        {
            return new TestConfig();
        }

        return JsonSerializer.Deserialize<TestConfig>(
                   await File.ReadAllTextAsync(resolved),
                   JsonDefaults.Options)
               ?? new TestConfig();
    }

    private static async Task<RegressionBaseline?> LoadBaselineAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<RegressionBaseline>(
            await File.ReadAllTextAsync(path),
            JsonDefaults.Options);
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            """
            TeyPdfCad.TestGenerator

            Commands:
              generate   --count N --seed N --output DIR [--split]
              self-check --count N --seed N --output DIR [--baseline FILE]
              core-run   --count N --seed N --output DIR [--baseline FILE] [--split]
              compare    --cases FILE --actual FILE --output DIR [--baseline FILE]
              baseline   --report FILE --output FILE
              verify     --output DIR

            Common:
              --config FILE   Override testconfig.json.

            self-check/verify use ExpectedEchoPipeline and validate only the test infrastructure.
            core-run uses the real SemanticReconstructionEngine. Without --baseline it records an honest baseline
            even when the current Core does not satisfy the configured release thresholds.
            """);
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private sealed class CliArgs
    {
        private readonly string[] _args;

        public CliArgs(string[] args)
        {
            _args = args;
        }

        public bool Has(string name)
        {
            return _args.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        public string? Get(string name)
        {
            for (var i = 0; i < _args.Length; i++)
            {
                if (!string.Equals(_args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (i + 1 >= _args.Length || _args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return null;
                }

                return _args[i + 1];
            }

            return null;
        }

        public string Require(string name)
        {
            return Get(name) ?? throw new ArgumentException($"Missing required argument {name}.");
        }

        public int GetInt(string name, int defaultValue)
        {
            var value = Get(name);
            if (value is null)
            {
                return defaultValue;
            }

            return int.TryParse(value, out var parsed)
                ? parsed
                : throw new ArgumentException($"Argument {name} must be an integer.");
        }
    }
}
