using System.Text.Json;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Dwg;
using TeyPdfCad.Pdf;

namespace TeyPdfCad.Cli;

public enum ConversionOutcome
{
    Complete = 0,
    UnsupportedVectorContent = 2,
    Partial = 3,
    InvalidArgumentsOrIo = 4
}

public sealed record ConversionResult(ConversionOutcome Outcome, string ReportPath, string? DwgPath);

public sealed class ConversionPipeline
{
    public async Task<ConversionResult> ConvertAsync(string inputPdfPath, string outputDwgPath, string reportPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPdfPath)) throw new ArgumentException("Input PDF path is required.", nameof(inputPdfPath));
        if (string.IsNullOrWhiteSpace(outputDwgPath)) throw new ArgumentException("Output DWG path is required.", nameof(outputDwgPath));
        if (string.IsNullOrWhiteSpace(reportPath)) throw new ArgumentException("Report path is required.", nameof(reportPath));

        try
        {
            await using var input = File.OpenRead(inputPdfPath);
            var document = await new PdfPigVectorDocumentReader().ReadAsync(input, cancellationToken);
            var pagesWithVectors = document.Pages.Count(page => page.Entities.Count > 0);
            if (pagesWithVectors == 0)
            {
                await WriteReportAsync(reportPath, complete: false, document.PageCount, 0, 0, ["PDF has no usable vector entities; raster/scanned input is unsupported in this release."], cancellationToken);
                return new ConversionResult(ConversionOutcome.UnsupportedVectorContent, reportPath, null);
            }

            var plan = new DocumentLayoutPlanner().Create(document);
            var bytes = new AcadSharpDwgWriter().Write(document, plan);
            var readBack = DwgReader.Read(new MemoryStream(bytes));
            var layoutsReadBack = readBack.Layouts.Count(layout => layout.Name.StartsWith("Лист-", StringComparison.Ordinal));
            if (layoutsReadBack != document.PageCount)
            {
                throw new InvalidDataException($"DWG read-back found {layoutsReadBack} layouts for {document.PageCount} PDF pages.");
            }
            await WriteAtomicallyAsync(outputDwgPath, bytes, cancellationToken);
            var warnings = pagesWithVectors == document.PageCount
                ? Array.Empty<string>()
                : ["One or more PDF pages contained no usable vector entities; DWG is partial."];
            var complete = warnings.Length == 0;
            await WriteReportAsync(reportPath, complete, document.PageCount, pagesWithVectors, layoutsReadBack, warnings, cancellationToken);
            return new ConversionResult(complete ? ConversionOutcome.Complete : ConversionOutcome.Partial, reportPath, outputDwgPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await WriteReportAsync(reportPath, complete: false, 0, 0, 0, [exception.Message], cancellationToken);
            return new ConversionResult(ConversionOutcome.InvalidArgumentsOrIo, reportPath, null);
        }
    }

    private static Task WriteReportAsync(string path, bool complete, int pagesRead, int pagesProcessed, int layoutsReadBack, IReadOnlyList<string> warnings, CancellationToken cancellationToken)
        => WriteAtomicallyAsync(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            complete,
            pagesRead,
            pagesProcessed,
            layoutsReadBack,
            warnings
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
