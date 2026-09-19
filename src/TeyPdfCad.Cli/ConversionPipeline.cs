using System.Text.Json;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Recognition;
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
            ValidatePaths(inputPdfPath, outputDwgPath, reportPath);
            await using var input = File.OpenRead(inputPdfPath);
            var document = await new PdfPigVectorDocumentReader().ReadAsync(input, cancellationToken);
            var hatchRecognition = document.Pages.ToDictionary(
                page => page.Number,
                page => new HatchRecognizer().Recognize(page.Entities));
            var pagesWithVectors = document.Pages.Count(page => page.Entities.Count > 0);
            if (pagesWithVectors == 0)
            {
                await WriteReportAsync(reportPath, complete: false, document.PageCount, 0, 0, ["PDF has no usable vector entities; raster/scanned input is unsupported in this release."], CreatePageReports(document, hatchRecognition), cancellationToken);
                return new ConversionResult(ConversionOutcome.UnsupportedVectorContent, reportPath, null);
            }

            var plan = new DocumentLayoutPlanner().Create(document);
            var bytes = new AcadSharpDwgWriter().Write(document, plan, hatchRecognition);
            var readBack = DwgReader.Read(new MemoryStream(bytes));
            var layoutsReadBack = readBack.Layouts.Count(layout => layout.Name.StartsWith("Лист-", StringComparison.Ordinal));
            if (layoutsReadBack != document.PageCount)
            {
                throw new InvalidDataException($"DWG read-back found {layoutsReadBack} layouts for {document.PageCount} PDF pages.");
            }
            ValidateReadBack(readBack, plan, document);
            await WriteAtomicallyAsync(outputDwgPath, bytes, cancellationToken);
            var warnings = pagesWithVectors == document.PageCount
                ? Array.Empty<string>()
                : ["One or more PDF pages contained no usable vector entities; DWG is partial."];
            var complete = warnings.Length == 0;
            await WriteReportAsync(reportPath, complete, document.PageCount, pagesWithVectors, layoutsReadBack, warnings, CreatePageReports(document, hatchRecognition), cancellationToken);
            return new ConversionResult(complete ? ConversionOutcome.Complete : ConversionOutcome.Partial, reportPath, outputDwgPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            await TryWriteFailureReportAsync(reportPath, exception.Message, cancellationToken);
            return new ConversionResult(ConversionOutcome.InvalidArgumentsOrIo, reportPath, null);
        }
    }

    private static void ValidatePaths(string inputPdfPath, string outputDwgPath, string reportPath)
    {
        var input = Path.GetFullPath(inputPdfPath);
        var output = Path.GetFullPath(outputDwgPath);
        var report = Path.GetFullPath(reportPath);
        if (!string.Equals(Path.GetExtension(input), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input file must use the .pdf extension.");
        if (!string.Equals(Path.GetExtension(output), ".dwg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Output file must use the .dwg extension.");
        if (!string.Equals(Path.GetExtension(report), ".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Report file must use the .json extension.");
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, report, StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, report, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Input PDF, output DWG and JSON report must use distinct paths.");
    }

    private static async Task TryWriteFailureReportAsync(string reportPath, string message, CancellationToken cancellationToken)
    {
        try
        {
            await WriteReportAsync(reportPath, complete: false, 0, 0, 0, [message], [], cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // The requested report destination itself may be invalid or unwritable.
        }
    }

    private static void ValidateReadBack(ACadSharp.CadDocument drawing, DwgDocumentPlan plan, TeyPdfCad.Core.Documents.VectorPdfDocument source)
    {
        foreach (var sheet in plan.Sheets)
        {
            var layout = drawing.Layouts.SingleOrDefault(candidate => candidate.Name == sheet.LayoutName)
                ?? throw new InvalidDataException($"DWG read-back did not find layout '{sheet.LayoutName}'.");
            if (Math.Abs(layout.PaperWidth - sheet.PaperWidthMillimetres) > 0.01d
                || Math.Abs(layout.PaperHeight - sheet.PaperHeightMillimetres) > 0.01d)
                throw new InvalidDataException($"DWG layout '{sheet.LayoutName}' paper size changed during write/read-back.");
            if (!layout.AssociatedBlock.Entities.OfType<ACadSharp.Entities.Viewport>().Any(viewport => !viewport.RepresentsPaper))
                throw new InvalidDataException($"DWG layout '{sheet.LayoutName}' has no model-space viewport.");
        }

        if (source.Pages.Any(page => page.Entities.Count > 0) && !drawing.Entities.Any())
            throw new InvalidDataException("DWG read-back found no model-space entities for a non-empty vector PDF.");
    }

    private static IReadOnlyList<PageReport> CreatePageReports(
        TeyPdfCad.Core.Documents.VectorPdfDocument document,
        IReadOnlyDictionary<int, HatchRecognitionResult> hatchRecognition)
        => document.Pages.Select(page => new PageReport(
            page.Number,
            page.WidthMillimetres,
            page.HeightMillimetres,
            page.RotationDegrees,
            page.Entities.Count,
            hatchRecognition[page.Number].NativeHatches.Count(candidate => candidate.IsSolid),
            hatchRecognition[page.Number].NativeHatches.Count(candidate => !candidate.IsSolid),
            hatchRecognition[page.Number].Warnings.Select(warning => warning.Code).Distinct().ToArray(),
            page.Entities.Count > 0)).ToArray();

    private static Task WriteReportAsync(string path, bool complete, int pagesRead, int pagesProcessed, int layoutsReadBack, IReadOnlyList<string> warnings, IReadOnlyList<PageReport> pages, CancellationToken cancellationToken)
        => WriteAtomicallyAsync(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            complete,
            pagesRead,
            pagesProcessed,
            layoutsReadBack,
            warnings,
            pages
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken);

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

    private sealed record PageReport(
        int PageNumber,
        double WidthMillimetres,
        double HeightMillimetres,
        int RotationDegrees,
        int SourceEntityCount,
        int SolidHatchCount,
        int PatternHatchCount,
        IReadOnlyList<string> RecognitionWarnings,
        bool Complete);
}
