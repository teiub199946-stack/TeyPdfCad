using System.Reflection;
using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class DimensionTextOverrideBuilderTests
{
    [Theory]
    [InlineData("2x5200", 5200d, "2x<>")]
    [InlineData("4 EQ 1250", 1250d, "4 EQ <>")]
    [InlineData("5200±10", 5200d, "<>±10")]
    [InlineData("5200", 5200d, "")]
    public void Build_replaces_the_numeric_token_matching_the_semantic_value(
        string sourceText,
        double expectedValue,
        string expectedOverride)
    {
        var assembly = typeof(TeyPdfCad.AutoCAD.ReconstructionCommands).Assembly;
        var builderType = assembly.GetType("TeyPdfCad.AutoCAD.DimensionTextOverrideBuilder");
        Assert.NotNull(builderType);

        var method = builderType!.GetMethod(
            "Build",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(double) },
            modifiers: null);

        Assert.NotNull(method);
        var actual = (string?)method!.Invoke(null, new object[] { sourceText, expectedValue });
        Assert.Equal(expectedOverride, actual);
    }
}
