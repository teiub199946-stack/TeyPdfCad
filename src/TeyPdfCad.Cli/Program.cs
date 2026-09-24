namespace TeyPdfCad.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0
            && string.Equals(
                args[0],
                "compare-dimension-metrics",
                StringComparison.OrdinalIgnoreCase))
        {
            return await DimensionMetricComparisonCommand.RunAsync(args);
        }

        if ((args.Length != 7 && args.Length != 9 && args.Length != 11)
            || !string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                "Usage: TeyPdfCad.Cli convert --input <pdf> --output <dwg> --report <json> " +
                "[--template-manifest <json>] [--probe-output <dwg>]\n" +
                "   or: TeyPdfCad.Cli compare-dimension-metrics --conversion-report <json> " +
                "--autocad-metrics <json> --output <json>");
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }

        Dictionary<string, string> options;
        try
        {
            options = args.Skip(1).Chunk(2).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            await Console.Error.WriteLineAsync("Options must be unique.");
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }
        if (!options.TryGetValue("--input", out var input) || !options.TryGetValue("--output", out var output) || !options.TryGetValue("--report", out var report))
        {
            await Console.Error.WriteLineAsync("Required options: --input, --output, --report.");
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }

        options.TryGetValue("--template-manifest", out var templateManifest);
        options.TryGetValue("--probe-output", out var probeOutput);
        var result = await new ConversionPipeline().ConvertAsync(
            input,
            output,
            report,
            default,
            templateManifest,
            probeOutput);
        await Console.Out.WriteLineAsync(
            $"outcome={result.Outcome}; report={result.ReportPath}; " +
            $"dwg={result.DwgPath ?? "none"}; probe={result.ProbeDwgPath ?? "none"}");
        return (int)result.Outcome;
    }
}
