using System.Text.Json;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
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
    private readonly IDwgDocumentWriter _dwgWriter;
    private readonly IDwgReadBackVerifier _dwgVerifier;
    private readonly bool _destructiveSuppressionEnabled;

    public ConversionPipeline()
        : this(
            new ProductionDwgDocumentWriter(),
            new ProductionDwgReadBackVerifier(),
            destructiveSuppressionEnabled: false)
    {
    }

    internal ConversionPipeline(
        IDwgDocumentWriter dwgWriter,
        IDwgReadBackVerifier dwgVerifier)
        : this(
            dwgWriter,
            dwgVerifier,
            destructiveSuppressionEnabled: true)
    {
    }

    internal ConversionPipeline(
        IDwgDocumentWriter dwgWriter,
        IDwgReadBackVerifier dwgVerifier,
        bool destructiveSuppressionEnabled)
    {
        _dwgWriter = dwgWriter ?? throw new ArgumentNullException(nameof(dwgWriter));
        _dwgVerifier = dwgVerifier ?? throw new ArgumentNullException(nameof(dwgVerifier));
        _destructiveSuppressionEnabled = destructiveSuppressionEnabled;
    }

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
                page => new HatchRecognizer().Recognize(page.Entities, page.Number));
            var semanticRecognition = document.Pages.ToDictionary(
                page => page.Number,
                AnalyzeSemantics);
            var replacementPlans = document.Pages.ToDictionary(
                page => page.Number,
                page => new SourceReplacementPlanner().BuildPlan(
                    page.Entities,
                    semanticRecognition[page.Number].Result,
                    hatchRecognition[page.Number],
                    page.Number));
            var templateLibrary = LoadTemplateLibrary(templateManifestPath);
            var templateSelections = templateLibrary is null
                ? null
                : document.Pages.ToDictionary(
                    page => page.Number,
                    page => DeferUnverifiedTemplateReplacement(
                        SelectTemplate(page, templateLibrary)));
            var pagesWithVectors = document.Pages.Count(page => page.Entities.Count > 0);
            if (pagesWithVectors == 0)
            {
                await WriteReportAsync(reportPath, complete: false, document.PageCount, 0, 0, ["PDF has no usable vector entities; raster/scanned input is unsupported in this release."], CreatePageReports(document, hatchRecognition, semanticRecognition, replacementPlans, templateSelections), null, cancellationToken);
                return new ConversionResult(ConversionOutcome.UnsupportedVectorContent, reportPath, null);
            }

            var plan = new DocumentLayoutPlanner().Create(document);
            var semanticResults = semanticRecognition
                .Where(pair => pair.Value.Result is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Result!);
            var writer = _dwgWriter;
            var verifier = _dwgVerifier;

            var outputFullPath = Path.GetFullPath(outputDwgPath);
            var outputDirectory = Path.GetDirectoryName(outputFullPath)!;
            Directory.CreateDirectory(outputDirectory);
            var runId = Guid.NewGuid().ToString("N");
            var probePath = Path.Combine(outputDirectory, $".teypdfcad-probe-{runId}.dwg");
            var finalPath = Path.Combine(outputDirectory, $".teypdfcad-final-{runId}.dwg");

            DwgWriteResult probeWrite;
            NativeReadBackVerification probeVerification;
            IReadOnlyDictionary<int, SuppressionDecision> suppressionDecisions;
            HashSet<PageSourceRef> authorizedSuppressedSources;
            DwgStructuralInventory probeInventory;
            DwgStructuralInventory finalInventory;
            ACadSharp.CadDocument finalReadBack;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var probeStream = File.Create(probePath))
                {
                    probeWrite = writer.Write(
                        probeStream,
                        document,
                        plan,
                        hatchRecognition,
                        semanticResults,
                        templateLibrary,
                        templateSelections,
                        replacementPlans,
                        authorizedSuppressedSources: null);
                }

                cancellationToken.ThrowIfCancellationRequested();
                probeVerification = verifier.Verify(probePath, probeWrite.Manifest);
                suppressionDecisions = document.Pages.ToDictionary(
                    page => page.Number,
                    page => new SuppressionGate().Evaluate(
                        replacementPlans[page.Number],
                        probeVerification));

                if (!_destructiveSuppressionEnabled)
                {
                    suppressionDecisions = suppressionDecisions.ToDictionary(
                        pair => pair.Key,
                        pair => DisableDestructiveSuppression(
                            replacementPlans[pair.Key],
                            pair.Value));
                }

                authorizedSuppressedSources = suppressionDecisions
                    .SelectMany(pair => pair.Value.SuppressSourceIds.Select(
                        sourceId => new PageSourceRef(pair.Key, sourceId)))
                    .ToHashSet();

                probeInventory = verifier.ReadStructuralInventory(probePath);

                // Validate every requested removal against the actual closed probe
                // before the final writer is allowed to suppress a source.
                try
                {
                    _ = DwgStructuralSanity.BuildExpectedFinalFingerprintMultiset(
                        probeInventory,
                        probeWrite.SourceEmissionSummary,
                        authorizedSuppressedSources);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"SourceSuppressionViolation: {exception.Message}",
                        exception);
                }

                DwgWriteResult finalWrite;
                try
                {
                    using var finalStream = File.Create(finalPath);
                    finalWrite = writer.Write(
                        finalStream,
                        document,
                        plan,
                        hatchRecognition,
                        semanticResults,
                        templateLibrary,
                        templateSelections,
                        replacementPlans,
                        authorizedSuppressedSources);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new InvalidDataException(
                        $"SourceSuppressionViolation: final writer rejected authorized suppression: {exception.Message}",
                        exception);
                }

                ValidateManifestParity(probeWrite.Manifest, finalWrite.Manifest);

                cancellationToken.ThrowIfCancellationRequested();
                var finalVerification = verifier.Verify(finalPath, probeWrite.Manifest);
                ValidateVerifiedCandidatesRemainVerified(
                    probeVerification,
                    finalVerification);

                finalInventory = verifier.ReadStructuralInventory(finalPath);
                try
                {
                    DwgStructuralSanity.ValidateFinal(
                        probeInventory,
                        finalInventory,
                        probeWrite.SourceEmissionSummary,
                        authorizedSuppressedSources);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        $"SourceSuppressionViolation: {exception.Message}",
                        exception);
                }

                finalReadBack = DwgReader.Read(finalPath);
                var layoutsInFinal = finalReadBack.Layouts.Count(layout =>
                    layout.Name.StartsWith("Лист-", StringComparison.Ordinal));
                if (layoutsInFinal != 0)
                {
                    throw new InvalidDataException(
                        $"DWG read-back found {layoutsInFinal} generated layouts; Model Space-only output requires zero.");
                }
                ValidateReadBack(finalReadBack, plan, document);

                // Publication happens only after every probe/final gate above passed.
                File.Move(finalPath, outputFullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
                if (File.Exists(finalPath))
                    File.Delete(finalPath);
            }

            var layoutsReadBack = finalReadBack.Layouts.Count(layout =>
                layout.Name.StartsWith("Лист-", StringComparison.Ordinal));
            var readBackSummary = CreateReadBackSummary(finalReadBack);
            var executionReports = document.Pages.ToDictionary(
                page => page.Number,
                page => ReplacementExecutionAuditor.Build(
                    replacementPlans[page.Number],
                    probeVerification,
                    readBackConfirmed: true));
            var warnings = new List<string>();
            if (pagesWithVectors != document.PageCount)
                warnings.Add("One or more PDF pages contained no usable vector entities; DWG is partial.");
            warnings.AddRange(document.Pages
                .SelectMany(page => page.Diagnostics.Select(diagnostic => $"Page {page.Number}: {diagnostic.Message}")));
            warnings.AddRange(document.Pages.SelectMany(page =>
                semanticRecognition[page.Number].Warnings.Select(code =>
                    $"Page {page.Number}: semantic audit requires review ({code}).")));
            warnings.AddRange(document.Pages.SelectMany(page =>
                hatchRecognition[page.Number].Warnings.Select(warning =>
                    $"Page {page.Number}: hatch audit requires review ({warning.Code}).")));
            warnings.AddRange(document.Pages.SelectMany(page =>
                replacementPlans[page.Number].Conflicts.Select(conflict =>
                    $"Page {page.Number}: source replacement conflict {conflict.Reason} on {conflict.SourceId}.")));
            if (templateSelections is not null)
            {
                warnings.AddRange(templateSelections
                    .Where(pair => string.Equals(
                        pair.Value.Reason,
                        "template-source-replacement-deferred-p0",
                        StringComparison.Ordinal))
                    .Select(pair =>
                        $"Page {pair.Key}: template source replacement is deferred until native read-back verification exists; source geometry is preserved."));
            }
            warnings.AddRange(document.Pages.SelectMany(page =>
                executionReports[page.Number].CandidateNotVerifiedSourceIds.Select(sourceId =>
                    $"Page {page.Number}: native candidate is not read-back verified for source {sourceId}; source is preserved.")));
            if (!_destructiveSuppressionEnabled
                && suppressionDecisions.Values.Any(decision =>
                    decision.Residuals.Any(residual =>
                        residual.Kind == ReplacementResidualKind.DestructiveSuppressionDisabled)))
            {
                warnings.Add("Production destructive source suppression is disabled until independent source-equivalence and current-branch 10k semantic quality gates pass; verified native candidates are emitted while source geometry is preserved.");
            }
            warnings = warnings.Distinct(StringComparer.Ordinal).ToList();

            var complete = warnings.Count == 0
                && replacementPlans.Values.All(planResult => planResult.IsFullPassEligible)
                && executionReports.Values.All(report =>
                    report.ReadBackConfirmed
                    && report.CandidateNotVerifiedSourceIds.Count == 0
                    && !report.AnyGeometryLost);
            await WriteReportAsync(reportPath, complete, document.PageCount, pagesWithVectors, layoutsReadBack, warnings, CreatePageReports(document, hatchRecognition, semanticRecognition, replacementPlans, templateSelections, executionReports, suppressionDecisions), readBackSummary, cancellationToken);
            return new ConversionResult(complete ? ConversionOutcome.Complete : ConversionOutcome.Partial, reportPath, outputDwgPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            await TryWriteFailureReportAsync(reportPath, exception.Message, cancellationToken);
            return new ConversionResult(ConversionOutcome.InvalidArgumentsOrIo, reportPath, null);
        }
    }

    private static SuppressionDecision DisableDestructiveSuppression(
        SourceReplacementPlan plan,
        SuppressionDecision decision)
    {
        if (decision.SuppressSourceIds.Count == 0)
            return decision;

        var preserved = decision.PreserveSourceIds
            .Concat(decision.SuppressSourceIds)
            .ToHashSet(StringComparer.Ordinal);
        var residuals = decision.Residuals.ToList();

        foreach (var sourceId in decision.SuppressSourceIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            var candidateId = plan.SourceCoverageMap.TryGetValue(sourceId, out var candidates)
                && candidates.Count == 1
                    ? candidates[0]
                    : null;

            if (!residuals.Any(existing =>
                    string.Equals(existing.SourceId, sourceId, StringComparison.Ordinal)
                    && existing.Kind == ReplacementResidualKind.DestructiveSuppressionDisabled))
            {
                residuals.Add(new ReplacementResidual(
                    sourceId,
                    candidateId,
                    ReplacementResidualKind.DestructiveSuppressionDisabled,
                    ReplacementResidualSeverity.Critical,
                    "Production destructive source suppression is disabled until independent source-equivalence and current-branch 10k semantic quality gates pass."));
            }
        }

        return new SuppressionDecision(
            new HashSet<string>(StringComparer.Ordinal),
            preserved,
            residuals
                .OrderBy(residual => residual.SourceId, StringComparer.Ordinal)
                .ThenBy(residual => residual.CandidateKey, StringComparer.Ordinal)
                .ThenBy(residual => residual.Kind)
                .ToArray());
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

    private static TemplateSelection DeferUnverifiedTemplateReplacement(
        TemplateSelection selection)
    {
        if (!selection.IsConfirmed || selection.SourceIdsToReplace.Count == 0)
            return selection;

        return new TemplateSelection(
            false,
            selection.TemplateName,
            "template-source-replacement-deferred-p0",
            selection.SourceIdsToReplace);
    }

    private static async Task TryWriteFailureReportAsync(
        string reportPath,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ReplacementResidual> fatalResiduals =
                message.Contains("SourceSuppressionViolation", StringComparison.Ordinal)
                    ?
                    [
                        new ReplacementResidual(
                            "(document)",
                            null,
                            ReplacementResidualKind.SourceSuppressionViolation,
                            ReplacementResidualSeverity.Critical,
                            message)
                    ]
                    : [];

            await WriteReportAsync(
                reportPath,
                complete: false,
                0,
                0,
                0,
                [message],
                [],
                null,
                cancellationToken,
                fatalResiduals);
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
        IReadOnlyDictionary<int, SourceReplacementPlan> replacementPlans,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelections = null,
        IReadOnlyDictionary<int, ReplacementExecutionReport>? executionReports = null,
        IReadOnlyDictionary<int, SuppressionDecision>? suppressionDecisions = null)
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
            semanticRecognition[page.Number].LevelCandidateCount,
            semanticRecognition[page.Number].ArcDimensionCandidateCount,
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
            suppressionDecisions is not null
                && suppressionDecisions.TryGetValue(page.Number, out var pageSuppression)
                ? pageSuppression.SuppressSourceIds.Count
                : 0,
            suppressionDecisions is not null
                && suppressionDecisions.TryGetValue(page.Number, out var pagePreservation)
                ? pagePreservation.PreserveSourceIds.Count
                : replacementPlans[page.Number].PreservedSourceIds.Count,
            replacementPlans[page.Number].DeferredCandidateKeys.Count,
            replacementPlans[page.Number].SourceCoverageMap.Count,
            replacementPlans[page.Number].Conflicts.Count,
            GetReportedResiduals(
                replacementPlans[page.Number],
                suppressionDecisions is not null
                    && suppressionDecisions.TryGetValue(page.Number, out var pageDecision)
                        ? pageDecision
                        : null),
            GetHighestResidualSeverity(
                replacementPlans[page.Number],
                suppressionDecisions is not null
                    && suppressionDecisions.TryGetValue(page.Number, out var severityDecision)
                        ? severityDecision
                        : null),
            0,
            ResolvePageAuditStatus(
                page,
                hatchRecognition[page.Number],
                semanticRecognition[page.Number],
                replacementPlans[page.Number],
                templateSelections is not null && templateSelections.TryGetValue(page.Number, out var auditTemplateSelection)
                    ? auditTemplateSelection
                    : null,
                executionReports is not null && executionReports.TryGetValue(page.Number, out var executionReport)
                    ? executionReport
                    : null),
            page.Entities.Count > 0
                && page.Diagnostics.Count == 0
                && hatchRecognition[page.Number].Warnings.Count == 0
                && semanticRecognition[page.Number].Warnings.Count == 0
                && replacementPlans[page.Number].IsFullPassEligible
                && (templateSelections is null
                    || !templateSelections.TryGetValue(page.Number, out var completionTemplateSelection)
                    || !string.Equals(
                        completionTemplateSelection.Reason,
                        "template-source-replacement-deferred-p0",
                        StringComparison.Ordinal))
                && (executionReports is null
                    || !executionReports.TryGetValue(page.Number, out var pageExecution)
                    || (pageExecution.ReadBackConfirmed
                        && pageExecution.CandidateNotVerifiedSourceIds.Count == 0
                        && !pageExecution.AnyGeometryLost)))).ToArray();

    private static IReadOnlyList<ReplacementResidual> GetReportedResiduals(
        SourceReplacementPlan plan,
        SuppressionDecision? decision)
        => decision?.Residuals ?? plan.Residuals;

    private static string? GetHighestResidualSeverity(
        SourceReplacementPlan plan,
        SuppressionDecision? decision)
    {
        var residuals = GetReportedResiduals(plan, decision);
        return residuals.Count == 0
            ? null
            : residuals
                .OrderByDescending(residual => residual.Severity)
                .First()
                .Severity
                .ToString();
    }

    private static void ValidateManifestParity(
        NativeWriteManifest probe,
        NativeWriteManifest final)
    {
        if (!probe.Candidates.Keys.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                final.Candidates.Keys.OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "SourceSuppressionViolation: final native manifest candidate set differs from probe.");
        }

        foreach (var pair in probe.Candidates)
        {
            var expected = pair.Value;
            var actual = final.Candidates[pair.Key];
            if (!string.Equals(expected.SemanticType, actual.SemanticType, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"SourceSuppressionViolation: candidate {pair.Key} semantic type changed between probe and final.");
            }

            var expectedByRole = expected.Entities.ToDictionary(
                entity => entity.Role,
                StringComparer.Ordinal);
            var actualByRole = actual.Entities.ToDictionary(
                entity => entity.Role,
                StringComparer.Ordinal);
            if (!expectedByRole.Keys.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(
                    actualByRole.Keys.OrderBy(value => value, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"SourceSuppressionViolation: candidate {pair.Key} role set changed between probe and final.");
            }

            foreach (var role in expectedByRole.Keys)
            {
                var probeEntity = expectedByRole[role];
                var finalEntity = actualByRole[role];
                if (!string.Equals(probeEntity.EntityKind, finalEntity.EntityKind, StringComparison.Ordinal)
                    || !string.Equals(probeEntity.GeometryFingerprint, finalEntity.GeometryFingerprint, StringComparison.Ordinal)
                    || !DictionaryEqual(probeEntity.RequiredProperties, finalEntity.RequiredProperties))
                {
                    throw new InvalidDataException(
                        $"SourceSuppressionViolation: candidate {pair.Key} role {role} manifest changed between probe and final.");
                }
            }
        }
    }

    private static void ValidateVerifiedCandidatesRemainVerified(
        NativeReadBackVerification probe,
        NativeReadBackVerification final)
    {
        foreach (var pair in probe.Candidates.Where(pair => pair.Value.IsVerified))
        {
            if (!final.Candidates.TryGetValue(pair.Key, out var finalCandidate)
                || !finalCandidate.IsVerified)
            {
                throw new InvalidDataException(
                    $"SourceSuppressionViolation: probe-verified candidate {pair.Key} failed final read-back verification.");
            }
        }
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string> first,
        IReadOnlyDictionary<string, string> second)
    {
        if (first.Count != second.Count)
            return false;

        foreach (var pair in first)
        {
            if (!second.TryGetValue(pair.Key, out var value)
                || !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolvePageAuditStatus(
        VectorPdfPage page,
        HatchRecognitionResult hatchRecognition,
        PageSemanticSummary semanticRecognition,
        SourceReplacementPlan replacementPlan,
        TemplateSelection? templateSelection,
        ReplacementExecutionReport? executionReport)
    {
        if (page.Entities.Count == 0 || page.Diagnostics.Count > 0)
            return "PARTIAL";
        if (executionReport is { AnyGeometryLost: true })
            return "PARTIAL";
        if (executionReport is not null && executionReport.CandidateNotVerifiedSourceIds.Count > 0)
            return "PASS_WITH_RESIDUALS";
        if (string.Equals(
                templateSelection?.Reason,
                "template-source-replacement-deferred-p0",
                StringComparison.Ordinal))
            return "PASS_WITH_RESIDUALS";
        if (hatchRecognition.Warnings.Count > 0
            || semanticRecognition.Warnings.Count > 0
            || !replacementPlan.IsFullPassEligible)
            return "PASS_WITH_RESIDUALS";
        return "FULL_PASS";
    }

    private static PageSemanticSummary AnalyzeSemantics(VectorPdfPage page)
    {
        var lines = page.Entities.OfType<VectorLine>().ToArray();
        var polylines = page.Entities.OfType<VectorPolyline>().ToArray();
        var texts = page.Entities.OfType<VectorText>().ToArray();
        var semanticPolylines = polylines
            .Where(polyline => polyline.Vertices.Count is >= 2 and <= 64)
            .ToArray();
        var semanticLines = lines
            .Concat(semanticPolylines.SelectMany(polyline => polyline.Vertices
                .Zip(polyline.Vertices.Skip(1), (start, end) => new VectorLine(
                    polyline.SourceId,
                    start,
                    end,
                    polyline.Style))
                .Concat(polyline.IsClosed
                    ? [new VectorLine(polyline.SourceId, polyline.Vertices[^1], polyline.Vertices[0], polyline.Style)]
                    : [])
                .Where(segment => GeometryMath.Distance(segment.Start, segment.End) >= 0.75d)))
            .ToArray();
        // Candidate dimension lines are spatially filtered around each text,
        // so the dominant cost is now the page-wide text/line scan.
        const long maximumSemanticWork = 1_000_000;
        var estimatedWork = (long)semanticLines.Length * Math.Max(texts.Length, 1);
        if (lines.Length > 2_000 || semanticLines.Length > 5_000 || texts.Length > 1_000 || estimatedWork > maximumSemanticWork * 3)
            return new PageSemanticSummary(0, 0, 0, 0, 0, ["semantic-recognition-skipped-complexity"], null);

        var baseScene = new PrimitiveScene();
        baseScene.Lines.AddRange(lines.Select(line => new LinePrimitive(
            line.Start,
            line.End,
            line.Style.SourceLayer,
            [line.SourceId],
            line.Style.StrokeWidthPoints * VectorPdfPage.MillimetresPerPoint,
            line.Style.DashPatternPoints?.Select(value => value * VectorPdfPage.MillimetresPerPoint).ToArray())));
        var scene = new PrimitiveScene();
        scene.Lines.AddRange(semanticLines.Select(line => new LinePrimitive(
            line.Start,
            line.End,
            line.Style.SourceLayer,
            [line.SourceId],
            line.Style.StrokeWidthPoints * VectorPdfPage.MillimetresPerPoint,
            line.Style.DashPatternPoints?.Select(value => value * VectorPdfPage.MillimetresPerPoint).ToArray())));
        foreach (var polyline in semanticPolylines.Where(polyline => !polyline.IsClosed && polyline.Vertices.Count >= 5))
        {
            if (CircularArcDetector.TryFit(polyline.Vertices, out var arc))
            {
                scene.Arcs.Add(new ArcPrimitive(
                    arc.Center,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.EndAngleRadians,
                    polyline.Style.SourceLayer,
                    [polyline.SourceId]));
            }
        }
        var primitiveTexts = texts.Select(text => new TextPrimitive(
            text.Value,
            text.InsertionPoint,
            text.HeightPoints * VectorPdfPage.MillimetresPerPoint,
            text.RotationRadians * 180d / Math.PI,
            text.Style.SourceLayer,
            [text.SourceId])).ToArray();
        baseScene.Texts.AddRange(primitiveTexts);
        scene.Texts.AddRange(primitiveTexts);
        var baseAnalyzed = new SemanticReconstructionEngine().Analyze(baseScene);
        var analyzed = new SemanticReconstructionEngine().Analyze(scene);
        var semantic = analyzed with
        {
            Dimensions = baseAnalyzed.Dimensions
                .Where(candidate => candidate.Confidence >= 0.90d && candidate.ArrowEvidence >= 0.5d)
                .ToArray(),
            Leaders = baseAnalyzed.Leaders
                .Where(candidate => candidate.Confidence >= 0.90d)
                .ToArray(),
            Levels = analyzed.Levels
                .Where(candidate => candidate.Confidence >= 0.90d)
                .ToArray(),
            ArcDimensions = analyzed.ArcDimensions
                .Where(candidate => candidate.Confidence >= 0.90d)
                .ToArray()
        };
        return new PageSemanticSummary(
            semantic.Dimensions.Count,
            semantic.Axes.Count,
            semantic.Leaders.Count,
            semantic.Levels.Count,
            semantic.ArcDimensions.Count,
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
            drawing.Entities.OfType<ACadSharp.Entities.Dimension>().Count(),
            drawing.Entities.OfType<ACadSharp.Entities.DimensionArc>().Count(),
            drawing.Entities.OfType<ACadSharp.Entities.Leader>().Count(),
            drawing.Entities.OfType<ACadSharp.Entities.Insert>().Count(),
            drawing.Layouts.SelectMany(layout => layout.AssociatedBlock.Entities).OfType<ACadSharp.Entities.Viewport>().Count(viewport => !viewport.RepresentsPaper),
            drawing.Layers.Count(),
            drawing.LineTypes.Count());

    private static Task WriteReportAsync(
        string path,
        bool complete,
        int pagesRead,
        int pagesProcessed,
        int layoutsReadBack,
        IReadOnlyList<string> warnings,
        IReadOnlyList<PageReport> pages,
        ReadBackSummary? readBack,
        CancellationToken cancellationToken,
        IReadOnlyList<ReplacementResidual>? fatalResiduals = null)
        => WriteAtomicallyAsync(path, JsonSerializer.SerializeToUtf8Bytes(new
        {
            complete,
            pagesRead,
            pagesProcessed,
            layoutsReadBack,
            warnings,
            fatalResiduals = fatalResiduals ?? [],
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
        int LevelCandidateCount,
        int ArcDimensionCandidateCount,
        IReadOnlyList<string> SemanticWarnings,
        bool TemplateSelected,
        string? TemplateName,
        string TemplateReason,
        IReadOnlyList<TeyPdfCad.Core.Documents.VectorPageDiagnostic> Diagnostics,
        int SuppressedSourceCount,
        int PreservedSourceCount,
        int DeferredCandidateCount,
        int SourceCoverageCount,
        int ReplacementConflictCount,
        IReadOnlyList<ReplacementResidual> ReplacementResiduals,
        string? HighestResidualSeverity,
        int GeometryLostCount,
        string SemanticAuditStatus,
        bool Complete);

    private sealed record PageSemanticSummary(
        int DimensionCandidateCount,
        int AxisCandidateCount,
        int LeaderCandidateCount,
        int LevelCandidateCount,
        int ArcDimensionCandidateCount,
        IReadOnlyList<string> Warnings,
        SemanticReconstructionResult? Result);

    private sealed record ReadBackSummary(
        int ModelEntityCount,
        int LineCount,
        int PolylineCount,
        int TextCount,
        int HatchCount,
        int DimensionCount,
        int ArcDimensionCount,
        int LeaderCount,
        int InsertCount,
        int ViewportCount,
        int LayerCount,
        int LineTypeCount);
}
