using System.Text.Json;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;
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
            var semanticRecognition = document.Pages.ToDictionary(
                page => page.Number,
                AnalyzeSemantics);
            var pagesWithVectors = document.Pages.Count(page => page.Entities.Count > 0);
            if (pagesWithVectors == 0)
            {
                await WriteReportAsync(reportPath, complete: false, document.PageCount, 0, 0, ["PDF has no usable vector entities; raster/scanned input is unsupported in this release."], CreatePageReports(document, hatchRecognition, semanticRecognition), null, cancellationToken);
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
            var readBackSummary = CreateReadBackSummary(readBack);
            await WriteAtomicallyAsync(outputDwgPath, bytes, cancellationToken);
            var warnings = new List<string>();
            if (pagesWithVectors != document.PageCount)
                warnings.Add("One or more PDF pages contained no usable vector entities; DWG is partial.");
            warnings.AddRange(document.Pages
                .SelectMany(page => page.Diagnostics.Select(diagnostic => $"Page {page.Number}: {diagnostic.Message}")));
            var complete = warnings.Count == 0;
            await WriteReportAsync(reportPath, complete, document.PageCount, pagesWithVectors, layoutsReadBack, warnings, CreatePageReports(document, hatchRecognition, semanticRecognition), readBackSummary, cancellationToken);
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
            await WriteReportAsync(reportPath, complete: false, 0, 0, 0, [message], [], null, cancellationToken);
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
        IReadOnlyDictionary<int, HatchRecognitionResult> hatchRecognition,
        IReadOnlyDictionary<int, PageSemanticSummary> semanticRecognition)
        => document.Pages.Select(page => new PageReport(
            page.Number,
            page.WidthMillimetres,
            page.HeightMillimetres,
            page.RotationDegrees,
            page.Entities.Count,
            hatchRecognition[page.Number].NativeHatches.Count(candidate => candidate.IsSolid),
            hatchRecognition[page.Number].NativeHatches.Count(candidate => !candidate.IsSolid),
            hatchRecognition[page.Number].Warnings.Select(warning => warning.Code).Distinct().ToArray(),
            semanticRecognition[page.Number].DimensionCandidateCount,
            semanticRecognition[page.Number].AxisCandidateCount,
            semanticRecognition[page.Number].LeaderCandidateCount,
            semanticRecognition[page.Number].Warnings,
            page.Diagnostics,
            page.Entities.Count > 0 && page.Diagnostics.Count == 0)).ToArray();

    private static PageSemanticSummary AnalyzeSemantics(VectorPdfPage page)
    {
        var lines = page.Entities.OfType<VectorLine>().ToArray();
        var texts = page.Entities.OfType<VectorText>().ToArray();
        // Dimension reconstruction probes candidate lines and then searches the
        // full line set for extension/arrow geometry.  Bound that quadratic
        // work for real construction sheets; semantics are advisory and must
        // never delay the vector conversion itself.
        const long maximumSemanticWork = 2_000_000;
        var estimatedWork = (long)lines.Length * lines.Length * Math.Max(texts.Length, 1);
        if (lines.Length > 500 || texts.Length > 500 || estimatedWork > maximumSemanticWork)
            return new PageSemanticSummary(0, 0, 0, ["semantic-recognition-skipped-complexity"]);

        var scene = new PrimitiveScene();
        scene.Lines.AddRange(lines.Select(line => new LinePrimitive(
            line.Start,
            line.End,
            line.Style.SourceLayer,
            [line.SourceId],
            line.Style.StrokeWidthPoints * VectorPdfPage.MillimetresPerPoint,
            line.Style.DashPatternPoints?.Select(value => value * VectorPdfPage.MillimetresPerPoint).ToArray())));
        scene.Texts.AddRange(texts.Select(text => new TextPrimitive(
            text.Value,
            text.InsertionPoint,
            text.HeightPoints * VectorPdfPage.MillimetresPerPoint,
            0d,
            text.Style.SourceLayer,
            [text.SourceId])));
        var semantic = new SemanticReconstructionEngine().Analyze(scene);
        return new PageSemanticSummary(
            semantic.Dimensions.Count,
            semantic.Axes.Count,
            semantic.Leaders.Count,
            semantic.Warnings.Select(warning => warning.Code).Distinct().ToArray());
    }

    private static ReadBackSummary CreateReadBackSummary(ACadSharp.CadDocument drawing)
        => new(
            drawing.Entities.Count(),
            drawing.Entities.OfType<ACadSharp.Entities.Line>().Count(),
            drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>().Count(),
            drawing.Entities.OfType<ACadSharp.Entities.TextEntity>().Count(),
            drawing.Entities.OfType<ACadSharp.Entities.Hatch>().Count(),
            drawing.Layouts.SelectMany(layout => layout.AssociatedBlock.Entities).OfType<ACadSharp.Entities.Viewport>().Count(viewport => !viewport.RepresentsPaper),
            drawing.Layers.Count(),
            drawing.LineTypes.Count());

    private static Task WriteReportAsync(string path, bool complete, int pagesRead, int pagesProcessed, int layoutsReadBack, IReadOnlyList<string> warnings, IReadOnlyList<PageReport> pages, ReadBackSummary? readBack, CancellationToken cancellationToken)
        => WriteAtomicallyAsync(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            complete,
            pagesRead,
            pagesProcessed,
            layoutsReadBack,
            warnings,
            pages,
            readBack
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
        int DimensionCandidateCount,
        int AxisCandidateCount,
        int LeaderCandidateCount,
        IReadOnlyList<string> SemanticWarnings,
        IReadOnlyList<TeyPdfCad.Core.Documents.VectorPageDiagnostic> Diagnostics,
        bool Complete);

    private sealed record PageSemanticSummary(
        int DimensionCandidateCount,
        int AxisCandidateCount,
        int LeaderCandidateCount,
        IReadOnlyList<string> Warnings);

    private sealed record ReadBackSummary(
        int ModelEntityCount,
        int LineCount,
        int PolylineCount,
        int TextCount,
        int HatchCount,
        int ViewportCount,
        int LayerCount,
        int LineTypeCount);
}
