using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class NativeDimensionTextBuilderTests
{
    [Theory]
    [InlineData("2x5200", 5200d, "2x<>")]
    [InlineData("4 EQ 1250", 1250d, "4 EQ <>")]
    [InlineData("5200±10", 5200d, "<>±10")]
    [InlineData("5200", 5200d, "")]
    [InlineData("L=31,42", 31.42d, "L=<>")]
    public void Build_preserves_annotation_around_native_measurement(
        string sourceText,
        double displayedValue,
        string expected)
    {
        Assert.Equal(
            expected,
            NativeDimensionTextBuilder.Build(sourceText, displayedValue));
    }
}
