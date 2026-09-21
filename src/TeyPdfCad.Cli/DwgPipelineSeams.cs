using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Templates;
using TeyPdfCad.Dwg;

namespace TeyPdfCad.Cli;

internal interface IDwgDocumentWriter
{
    DwgWriteResult Write(
        Stream destination,
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
        TemplateLibrary? templateLibrary = null,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
        IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
        IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null);
}

internal sealed class ProductionDwgDocumentWriter : IDwgDocumentWriter
{
    private readonly AcadSharpDwgWriter _inner = new();

    public DwgWriteResult Write(
        Stream destination,
        VectorPdfDocument source,
        DwgDocumentPlan plan,
        IReadOnlyDictionary<int, HatchRecognitionResult>? hatchRecognitionByPage = null,
        IReadOnlyDictionary<int, SemanticReconstructionResult>? semanticRecognitionByPage = null,
        TemplateLibrary? templateLibrary = null,
        IReadOnlyDictionary<int, TemplateSelection>? templateSelectionsByPage = null,
        IReadOnlyDictionary<int, SourceReplacementPlan>? sourceReplacementPlansByPage = null,
        IReadOnlySet<PageSourceRef>? authorizedSuppressedSources = null)
        => _inner.Write(
            destination,
            source,
            plan,
            hatchRecognitionByPage,
            semanticRecognitionByPage,
            templateLibrary,
            templateSelectionsByPage,
            sourceReplacementPlansByPage,
            authorizedSuppressedSources);
}

internal interface IDwgReadBackVerifier
{
    NativeReadBackVerification Verify(
        string dwgPath,
        NativeWriteManifest manifest);

    DwgStructuralInventory ReadStructuralInventory(string dwgPath);
}

internal sealed class ProductionDwgReadBackVerifier : IDwgReadBackVerifier
{
    private readonly DwgReadBackVerifier _inner = new();

    public NativeReadBackVerification Verify(
        string dwgPath,
        NativeWriteManifest manifest)
        => _inner.Verify(dwgPath, manifest);

    public DwgStructuralInventory ReadStructuralInventory(string dwgPath)
        => _inner.ReadStructuralInventory(dwgPath);
}
