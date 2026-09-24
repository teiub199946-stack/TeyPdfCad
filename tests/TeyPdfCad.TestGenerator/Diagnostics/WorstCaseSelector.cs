using System.Globalization;
using TeyPdfCad.TestGenerator.Models;

namespace TeyPdfCad.TestGenerator.Diagnostics;

/// <summary>
/// Deterministically selects the most important real Semantic Core defects.
/// Noise-propagation and numeric-tolerance cases are never eligible.
/// </summary>
public sealed class WorstCaseSelector
{
    public List<WorstCaseRecord> Select(DiagnosticRegressionReport report, int maxCount = 20)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (maxCount < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        return report.Cases
            .Where(IsEligible)
            .OrderByDescending(x => Severity(x.Diagnostic.Category))
            .ThenByDescending(x => x.Diagnostic.MaxPaperError)
            .ThenByDescending(x => x.Diagnostic.MaxWorldError)
            .ThenBy(x => x.Expected.Id, StringComparer.Ordinal)
            .Take(maxCount)
            .Select(ToWorstCase)
            .ToList();
    }

    private static bool IsEligible(DiagnosticCaseRecord record)
    {
        if (!record.Diagnostic.IsRealCoreDefect)
            return false;

        return record.Diagnostic.Category is not DiagnosticCategory.ExpectedNoisePropagation
            and not DiagnosticCategory.NumericTolerance
            and not DiagnosticCategory.Correct
            and not DiagnosticCategory.ExpectedAbstention;
    }

    private static int Severity(DiagnosticCategory category)
        => category switch
        {
            DiagnosticCategory.UnexpectedDetection => 1000,
            DiagnosticCategory.MissedDetection => 1000,
            DiagnosticCategory.WrongAbstention => 950,
            DiagnosticCategory.ChainMismatch => 900,
            DiagnosticCategory.WrongDimensionCount => 850,
            DiagnosticCategory.WrongValue => 800,
            DiagnosticCategory.WrongScale => 750,
            DiagnosticCategory.WrongDimensionType => 700,
            DiagnosticCategory.WrongGeometry => 600,
            _ => 500
        };

    private static WorstCaseRecord ToWorstCase(DiagnosticCaseRecord row)
    {
        return new WorstCaseRecord
        {
            CaseId = row.Expected.Id,
            Seed = row.Expected.Seed,
            Category = row.Diagnostic.Category,
            DrawingScale = row.Expected.DrawingScale,
            InjectedNoise = row.Expected.Noise,
            Expected = DescribeExpected(row.Expected),
            Actual = DescribeActual(row.Actual),
            MaxPaperError = row.Diagnostic.MaxPaperError,
            MaxWorldError = row.Diagnostic.MaxWorldError,
            Explanation = row.Diagnostic.Reasons.Count == 0
                ? "Real Core defect."
                : string.Join("; ", row.Diagnostic.Reasons)
        };
    }

    private static string DescribeExpected(DimensionCase value)
        => string.Format(
            CultureInfo.InvariantCulture,
            "result={0}; type={1}; value={2:0.######}; count={3}; scale=1:{4:0.######}; p1=({5:0.######},{6:0.######}); p2=({7:0.######},{8:0.######}); dim=({9:0.######},{10:0.######})",
            value.ExpectedResult,
            value.DimensionType,
            value.ExpectedValue,
            value.ExpectedDimensions,
            value.DrawingScale,
            value.P1.X,
            value.P1.Y,
            value.P2.X,
            value.P2.Y,
            value.DimensionLinePoint.X,
            value.DimensionLinePoint.Y);

    private static string DescribeActual(ActualDimensionResult value)
        => string.Format(
            CultureInfo.InvariantCulture,
            "result={0}; type={1}; value={2}; count={3}; scale={4}; p1={5}; p2={6}; dim={7}",
            value.Result,
            value.DimensionType?.ToString() ?? "<null>",
            Format(value.Value),
            value.DetectedDimensions,
            value.DrawingScale is null ? "<null>" : "1:" + Format(value.DrawingScale),
            Format(value.P1),
            Format(value.P2),
            Format(value.DimensionLinePoint));

    private static string Format(double? value)
        => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? "<null>";

    private static string Format(Point2D? point)
        => point is null
            ? "<null>"
            : string.Format(CultureInfo.InvariantCulture, "({0:0.######},{1:0.######})", point.Value.X, point.Value.Y);
}
