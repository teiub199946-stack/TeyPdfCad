using System.Text.Json;

namespace TeyPdfCad.Cli;

internal static class DimensionMetricComparisonCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 7)
        {
            await Console.Error.WriteLineAsync(
                "Usage: TeyPdfCad.Cli compare-dimension-metrics " +
                "--conversion-report <json> --autocad-metrics <json> --output <json>");
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }

        Dictionary<string, string> options;
        try
        {
            options = args.Skip(1)
                .Chunk(2)
                .ToDictionary(
                    pair => pair[0],
                    pair => pair[1],
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            await Console.Error.WriteLineAsync("Options must be unique.");
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }

        if (!options.TryGetValue("--conversion-report", out var conversionReport)
            || !options.TryGetValue("--autocad-metrics", out var autoCadMetrics)
            || !options.TryGetValue("--output", out var output))
        {
            await Console.Error.WriteLineAsync(
                "Required options: --conversion-report, --autocad-metrics, --output.");
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }

        try
        {
            var sourceJson = await File.ReadAllTextAsync(conversionReport);
            var nativeJson = await File.ReadAllTextAsync(autoCadMetrics);
            var comparison = DimensionMetricComparator.Compare(sourceJson, nativeJson);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                comparison,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

            var fullOutput = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
            var temporary = fullOutput + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, fullOutput, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }

            await Console.Out.WriteLineAsync(
                $"dimension-metric-comparison={fullOutput}; " +
                $"candidates={comparison.Candidates.Count}; equivalence-proven=0");
            return (int)ConversionOutcome.Complete;
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is JsonException
            || exception is ArgumentException)
        {
            await Console.Error.WriteLineAsync(
                "Dimension metric comparison failed: " + exception.Message);
            return (int)ConversionOutcome.InvalidArgumentsOrIo;
        }
    }
}
