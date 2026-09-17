using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Pipelines;

public interface ISemanticTestPipeline
{
    ValueTask<ActualDimensionResult> RunAsync(DimensionCase testCase, CancellationToken cancellationToken = default);
}

/// <summary>
/// Future AutoCAD integration point. Implementations may create a native dimension,
/// plot to PDF, PDFIMPORT it and return the primitive/semantic result. The generator
/// itself intentionally has no AutoCAD assembly dependency.
/// </summary>
public interface IAutoCadTestPipeline
{
    ValueTask<AutoCadPipelineResult> RoundTripAsync(
        DimensionCase testCase,
        CancellationToken cancellationToken = default);
}

public sealed record AutoCadPipelineResult
{
    public string CaseId { get; init; } = string.Empty;
    public string? DwgPath { get; init; }
    public string? PdfPath { get; init; }
    public string? ImportedArtifactPath { get; init; }
    public ActualDimensionResult SemanticResult { get; init; } = new();
}

/// <summary>
/// Harness-only pipeline used to prove the regression infrastructure itself.
/// It is NOT a semantic recognizer and must not be treated as accuracy evidence.
/// </summary>
public sealed class ExpectedEchoPipeline : ISemanticTestPipeline
{
    public ValueTask<ActualDimensionResult> RunAsync(
        DimensionCase testCase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new ActualDimensionResult
        {
            CaseId = testCase.Id,
            Result = testCase.ExpectedResult,
            DetectedDimensions = testCase.ExpectedDimensions,
            Value = testCase.ExpectedResult == ExpectedResult.Rejected ? null : testCase.ExpectedValue,
            P1 = testCase.ExpectedResult == ExpectedResult.Rejected ? null : testCase.P1,
            P2 = testCase.ExpectedResult == ExpectedResult.Rejected ? null : testCase.P2,
            DimensionLinePoint = testCase.ExpectedResult == ExpectedResult.Rejected
                ? null
                : testCase.DimensionLinePoint,
            DimensionType = testCase.ExpectedResult == ExpectedResult.Rejected
                ? null
                : testCase.DimensionType,
            DrawingScale = testCase.ExpectedResult == ExpectedResult.Rejected
                ? null
                : testCase.DrawingScale,
            ConfidenceClass = testCase.ExpectedConfidenceClass
        };

        return ValueTask.FromResult(result);
    }
}

public sealed class JsonActualResultPipeline : ISemanticTestPipeline
{
    private readonly IReadOnlyDictionary<string, ActualDimensionResult> _results;

    public JsonActualResultPipeline(IEnumerable<ActualDimensionResult> results)
    {
        _results = results.ToDictionary(x => x.CaseId, StringComparer.Ordinal);
    }

    public ValueTask<ActualDimensionResult> RunAsync(
        DimensionCase testCase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_results.TryGetValue(testCase.Id, out var result))
        {
            return ValueTask.FromResult(result);
        }

        return ValueTask.FromResult(new ActualDimensionResult
        {
            CaseId = testCase.Id,
            Result = ExpectedResult.Ambiguous,
            IsMissing = true,
            Diagnostics = new List<string> { "No actual result was supplied for this case." }
        });
    }
}
