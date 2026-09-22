using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Generation;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class PreRecognitionGeometryAnalyzerTests
{
    [Fact]
    public void Chain_case_000010_is_degenerate_before_recognition()
    {
        var testCase = new DimensionCaseGenerator()
            .Generate(10_000, 12345)
            .Cases
            .Single(item => item.Id == "case_000010");

        var assessment = PreRecognitionGeometryAnalyzer.Analyze(testCase);

        Assert.Equal(PreRecognitionGeometryStatus.Degenerate, assessment.Status);
        Assert.Equal(2, assessment.DegenerateSegments.Count);
        Assert.All(
            assessment.DegenerateSegments,
            segment => Assert.Contains("post-mismatch", segment.Reason, StringComparison.Ordinal));
    }
}
