namespace TeyPdfCad.TestGenerator.Diagnostics;

public static class GeometryTolerance
{
    public const double NumericPaperEpsilon = 0.000005;
    public const double NumericWorldEpsilon = 0.000001;

    public static GeometryToleranceResult Calculate(PointDiagnosticInput input)
    {
        if (!double.IsFinite(input.DrawingScale) || input.DrawingScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "DrawingScale must be finite and positive.");
        }

        var injectedPaperError = input.CleanPaper.DistanceTo(input.CoreInputPaper);
        var actualPaperError = input.CleanPaper.DistanceTo(input.ActualCorePaper);
        var actualWorldError = input.ExpectedWorld.DistanceTo(input.ActualWorld);
        var numericWorldAsPaper = NumericWorldEpsilon / input.DrawingScale;
        var allowedPaperError = injectedPaperError + NumericPaperEpsilon + numericWorldAsPaper;
        var allowedWorldError = allowedPaperError * input.DrawingScale;

        var category = Classify(
            injectedPaperError,
            actualPaperError,
            numericWorldAsPaper);

        return new GeometryToleranceResult
        {
            Category = category,
            InjectedPaperError = injectedPaperError,
            AllowedPaperError = allowedPaperError,
            AllowedWorldError = allowedWorldError,
            ActualPaperError = actualPaperError,
            ActualWorldError = actualWorldError
        };
    }

    private static DiagnosticCategory Classify(
        double injectedPaperError,
        double actualPaperError,
        double numericWorldAsPaper)
    {
        const double comparisonEpsilon = 1e-12;

        if (actualPaperError <= injectedPaperError + comparisonEpsilon)
        {
            return injectedPaperError > comparisonEpsilon
                ? DiagnosticCategory.ExpectedNoisePropagation
                : DiagnosticCategory.Correct;
        }

        var numericAllowance = NumericPaperEpsilon + numericWorldAsPaper;
        if (actualPaperError <= injectedPaperError + numericAllowance + comparisonEpsilon)
        {
            return DiagnosticCategory.NumericTolerance;
        }

        return DiagnosticCategory.WrongGeometry;
    }
}
