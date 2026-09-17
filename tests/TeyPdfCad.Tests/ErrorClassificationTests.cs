using TeyPdfCad.TestGenerator.Diagnostics;
using TeyPdfCad.TestGenerator.Models;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class ErrorClassificationTests
{
    [Fact]
    public void ZeroNoise_DoesNotReceiveScaleSizedTolerance()
    {
        var result = GeometryTolerance.Calculate(new PointDiagnosticInput
        {
            ExpectedWorld = new Point2D(0, 0),
            ActualWorld = new Point2D(0.1, 0),
            CleanPaper = new Point2D(0, 0),
            CoreInputPaper = new Point2D(0, 0),
            ActualCorePaper = new Point2D(0.0002, 0),
            DrawingScale = 500
        });

        Assert.True(result.AllowedPaperError < 0.00001);
        Assert.True(result.AllowedWorldError < 0.01);
        Assert.Equal(DiagnosticCategory.WrongGeometry, result.Category);
    }

    [Theory]
    [InlineData(1, 0.02)]
    [InlineData(100, 2.0)]
    [InlineData(500, 10.0)]
    public void InjectedPaperDisplacement_ConvertsLinearlyToWorldAllowance(double scale, double expectedNoiseWorld)
    {
        var result = GeometryTolerance.Calculate(new PointDiagnosticInput
        {
            ExpectedWorld = new Point2D(0, 0),
            ActualWorld = new Point2D(expectedNoiseWorld, 0),
            CleanPaper = new Point2D(0, 0),
            CoreInputPaper = new Point2D(0.02, 0),
            ActualCorePaper = new Point2D(0.02, 0),
            DrawingScale = scale
        });

        Assert.InRange(result.AllowedWorldError, expectedNoiseWorld, expectedNoiseWorld + 0.01);
        Assert.Equal(DiagnosticCategory.ExpectedNoisePropagation, result.Category);
    }

    [Fact]
    public void LargePaperError_IsNotExcusedByLargeDrawingScale()
    {
        var result = GeometryTolerance.Calculate(new PointDiagnosticInput
        {
            ExpectedWorld = new Point2D(0, 0),
            ActualWorld = new Point2D(50, 0),
            CleanPaper = new Point2D(0, 0),
            CoreInputPaper = new Point2D(0.01, 0),
            ActualCorePaper = new Point2D(0.10, 0),
            DrawingScale = 500
        });

        Assert.Equal(DiagnosticCategory.WrongGeometry, result.Category);
        Assert.True(result.ActualPaperError > result.AllowedPaperError);
    }

    [Fact]
    public void NumericEpsilon_IsSeparateFromInjectedNoise()
    {
        var result = GeometryTolerance.Calculate(new PointDiagnosticInput
        {
            ExpectedWorld = new Point2D(1000, 2000),
            ActualWorld = new Point2D(1000.0002, 2000),
            CleanPaper = new Point2D(10, 20),
            CoreInputPaper = new Point2D(10, 20),
            ActualCorePaper = new Point2D(10.000002, 20),
            DrawingScale = 100
        });

        Assert.Equal(0, result.InjectedPaperError, 12);
        Assert.Equal(DiagnosticCategory.NumericTolerance, result.Category);
    }
}
