using System.Text.Json;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Sheets;
using TeyPdfCad.Core.Templates;
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
    public async Task<ConversionResult> ConvertAsync(
        string inputPdfPath,
        string outputDwgPath,
        string reportPath,
        CancellationToken cancellationToken,
        string? templateManifestPath = null)
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
            var templateLibrary = LoadTemplateLibrary(templateManifestPath);
            var templateSelections = templateLibrary is null
                ? null
                : document.Pages.ToDictionary(page => page.Number, page => SelectTemplate(page, templateLibrary));
            var pagesWithVectors = document.Pages.Count(page => page.Entities.Count > 0);
            if (pagesWithVectors == 0)
            {
                await WriteReportAsync(reportPath, complete: false, document.PageCount, 0, 0, ["PDF has no usable vector entities; raster/scanned input is unsupported in this release."], CreatePageReports(document, hatchRecognition, semanticRecognition, templateSelections), null, cancellationToken);
                return new ConversionResult(ConversionOutcome.UnsupportedVectorContent, reportPath, null);
            }

            var plan = new DocumentLayoutPlanner().Create(document);
            var semanticResults = semanticRecognition
                .Where(pair => pair.Value.Result is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Result!);
            var bytes = new AcadSharpDwgWriter().Write(
                document,
                plan,
                hatchRecognition,
                semanticResults,
                templateLibrary,
                templateSelections);
            var readBack = DwgReader.Read(new MemoryStream(bytes));
            var layoutsReadBack = readBack.Layouts.Count(layout => layout.Name.StartsWith("Лист-", StringComparison.Ordinal));
            if (layoutsReadBack != 0)
            {
                throw new InvalidDataException($"DWG read-back found {layoutsReadBack} generated layouts; Model Space-only output requires zero.");
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
            await WriteReportAsync(reportPath, complete, document.PageCount, pagesWithVectors, layoutsReadBack, warnings, CreatePageReports(document, hatchRecognition, semanticRecognition, templateSelections), readBackSummary, cancellationToken);
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

    private static TemplateLibrary? LoadTemplateLibrary(string? templateManifestPath)
    {
        if (string.IsNullOrWhiteSpace(templateManifestPath)) return null;
        var fullPath = Path.GetFullPath(templateManifestPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Template manifest was not found.", fullPath);
        return new TemplateLibraryManifestReader().Read(File.ReadAllText(fullPath));
    }

    private static TemplateSelection SelectTemplate(VectorPdfPage page, TemplateLibrary library)
    {
        var detection = StandardSheetDetector.Detect(page.WidthMillimetres, page.HeightMillimetres);
        var sheet = new SheetMetadata(page.WidthMillimetres, page.HeightMillimetres, detection.Format, detection.Orientation);
        var scene = new PrimitiveScene { Sheet = sheet };
        foreach (var line in page.Entities.OfType<VectorLine>())
        {
            scene.Lines.Add(new LinePrimitive(
                line.Start,
                line.End,
                line.Style.SourceLayer,
                [line.SourceId]));
        }
        foreach (var polyline in page.Entities.OfType<VectorPolyline>())
        {
            for (var index = 1; index < polyline.Vertices.Count; index++)
            {
                scene.Lines.Add(new LinePrimitive(
                    polyline.Vertices[index - 1],
                    polyline.Vertices[index],
                    polyline.Style.SourceLayer,
                    [polyline.SourceId]));
            }

            if (polyline.IsClosed && polyline.Vertices.Count > 2)
            {
                scene.Lines.Add(new LinePrimitive(
                    polyline.Vertices[^1],
                    polyline.Vertices[0],
                    polyline.Style.SourceLayer,
                    [polyline.SourceId]));
            }
        }
        scene.Texts.AddRange(page.Entities.OfType<VectorText>().Select(text => new TextPrimitive(
            text.Value,
            text.InsertionPoint,
            text.HeightPoints * VectorPdfPage.MillimetresPerPoint,
            text.RotationRadians * 180d / Math.PI,
            text.Style.SourceLayer,
            [text.SourceId])));
        var titleBlock = TitleBlockDetector.Detect(scene, sheet);
        return new TemplateSheetSelector(library).Select(sheet, titleBlock);
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
        if (drawing.Layouts.Any(candidate => candidate.Name.StartsWith("Лист-", StringComparison.Ordinal)))
            throw new InvalidDataException("DWG read-back found generated layouts in Model Space-only mode.");

        if (source.Pages.Any(page => page.Entities.Count > 0) && !drawing.Entities.Any())
            throw new InvalidDataException("DWG read-back found no model-space entities for a non-empty vector PDF.");
    }

    private static IReadOnlyList<PageReport> CreatePageReports(
        TeyPdfCad.Core.Documents.VectorPdfDocument document,
        IReadOnlyDictionary<int, HatchRecognitionResult> hatchRecognition,
        IReadOnlyDictionary<int, PageSemanticSummary> semanticRecognition,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelections = null)
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
            templateSelections is not null
                && templateSelections.TryGetValue(page.Number, out var selection)
                && selection.IsConfirmed,
            templateSelections is not null
                && templateSelections.TryGetValue(page.Number, out var namedSelection)
                ? namedSelection.TemplateName
                : null,
            templateSelections is not null
                && templateSelections.TryGetValue(page.Number, out var reasonSelection)
                ? reasonSelection.Reason
                : "template-manifest-not-supplied",
            page.Diagnostics,
            page.Entities.Count > 0 && page.Diagnostics.Count == 0)).ToArray();

    private static PageSemanticSummary AnalyzeSemantics(VectorPdfPage page)
    {
        var lines = page.Entities.OfType<VectorLine>().ToArray();
        var texts = page.Entities.OfType<VectorText>().ToArray();
        // Candidate dimension lines are spatially filtered around each text,
        // so the dominant cost is now the page-wide text/line scan.
        const long maximumSemanticWork = 1_000_000;
        var estimatedWork = (long)lines.Length * Math.Max(texts.Length, 1);
        if (lines.Length > 2_000 || texts.Length > 1_000 || estimatedWork > maximumSemanticWork)
            return new PageSemanticSummary(0, 0, 0, ["semantic-recognition-skipped-complexity"], null);

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
            text.RotationRadians * 180d / Math.PI,
            text.Style.SourceLayer,
            [text.SourceId])));
        var analyzed = new SemanticReconstructionEngine().Analyze(scene);
        var semantic = analyzed with
        {
            Dimensions = analyzed.Dimensions
                .Where(candidate => candidate.Confidence >= 0.90d && candidate.ArrowEvidence >= 0.5d)
                .ToArray(),
            Leaders = analyzed.Leaders
                .Where(candidate => candidate.Confidence >= 0.90d)
                .ToArray()
        };
        return new PageSemanticSummary(
            semantic.Dimensions.Count,
            semantic.Axes.Count,
            semantic.Leaders.Count,
            semantic.Warnings.Select(warning => warning.Code).Distinct().ToArray(),
            semantic);
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
        bool TemplateSelected,
        string? TemplateName,
        string TemplateReason,
        IReadOnlyList<TeyPdfCad.Core.Documents.VectorPageDiagnostic> Diagnostics,
        bool Complete);

    private sealed record PageSemanticSummary(
        int DimensionCandidateCount,
        int AxisCandidateCount,
        int LeaderCandidateCount,
        IReadOnlyList<string> Warnings,
        SemanticReconstructionResult? Result);

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
