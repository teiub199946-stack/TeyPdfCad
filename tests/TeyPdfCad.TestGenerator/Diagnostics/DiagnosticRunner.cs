using TeyPdfCad.TestGenerator.Models;
using TeyPdfCad.TestGenerator.Pipelines;

namespace TeyPdfCad.TestGenerator.Diagnostics;

/// <summary>
/// Executes TEST-003 without altering Semantic Core or expected labels.
/// The diagnostic path is deliberately separate from the legacy TEST-002 RegressionRunner.
/// </summary>
public sealed class DiagnosticRunner
{
    private readonly SemanticCoreTestPipeline _pipeline;
    private readonly ErrorClassifier _classifier;
    private readonly DiagnosticReportBuilder _reportBuilder;

    public DiagnosticRunner(
        SemanticCoreTestPipeline? pipeline = null,
        ErrorClassifier? classifier = null,
        DiagnosticReportBuilder? reportBuilder = null)
    {
        _pipeline = pipeline ?? new SemanticCoreTestPipeline();
        _classifier = classifier ?? new ErrorClassifier();
        _reportBuilder = reportBuilder ?? new DiagnosticReportBuilder();
    }

    public async Task<DiagnosticRegressionReport> RunAsync(
        TestCorpus corpus,
        TestConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(config);

        var classifier = new ErrorClassifier(config);
        var records = new List<DiagnosticCaseRecord>(corpus.Cases.Count);

        foreach (var testCase in corpus.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await _pipeline.RunDetailedAsync(testCase, cancellationToken);
            var diagnostic = classifier.Classify(testCase, run.Actual, run.Trace);

            records.Add(new DiagnosticCaseRecord
            {
                Expected = testCase,
                Actual = run.Actual,
                Trace = run.Trace,
                Diagnostic = diagnostic
            });
        }

        return _reportBuilder.Build(corpus.Seed, records);
    }
}
