using Xunit;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Tests.Recognition;

public sealed class DimensionTextParserTests
{
    [Theory]
    [InlineData("5200", 5200)]
    [InlineData("5 200", 5200)]
    [InlineData("5\u00A0200", 5200)]
    [InlineData("5200,0 мм", 5200)]
    [InlineData("5200±10", 5200)]
    [InlineData("~5200", 5200)]
    public void Parses_Common_Linear_Dimension_Text(string text, double expected)
    {
        Assert.True(DimensionTextParser.TryParse(text, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(DimensionTextKind.Linear, parsed.Kind);
        Assert.Equal(expected, parsed.NominalValue, 6);
    }

    [Fact]
    public void Classifies_Diameter_Without_Treating_It_As_Linear()
    {
        Assert.True(DimensionTextParser.TryParse("Ø520", out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(DimensionTextKind.Diameter, parsed.Kind);
        Assert.Equal(520, parsed.NominalValue, 6);
    }

    [Fact]
    public void Classifies_Radius_Without_Treating_It_As_Linear()
    {
        Assert.True(DimensionTextParser.TryParse("R250", out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(DimensionTextKind.Radius, parsed.Kind);
        Assert.Equal(250, parsed.NominalValue, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-100")]
    public void Rejects_NonDimension_Or_NonPositive_Text(string text)
    {
        Assert.False(DimensionTextParser.TryParse(text, out _));
    }
}
