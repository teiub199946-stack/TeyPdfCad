using System.Text;

namespace TeyPdfCad.AutoCAD;

internal static class AnalysisReportFormatter
{
    public static string FormatSummary(
        int selectedCount,
        int lineCount,
        int textCount,
        int dimensionCount,
        int chainCount,
        string scales,
        double averageConfidence)
    {
        var builder = new StringBuilder();
        builder.Append(
            $"TeyPdfCad analysis: selected={selectedCount}, lines={lineCount}, texts={textCount}, " +
            $"dimensions={dimensionCount}, chains={chainCount}, scales=[{scales}], " +
            $"avg confidence={averageConfidence:P2}.");

        if (selectedCount > 0 && lineCount > 0 && textCount == 0)
        {
            builder.Append(
                "\nTeyPdfCad diagnostic: no DBText/MText primitives were read. " +
                "PDFIMPORT text may be vector glyph geometry; semantic dimension reconstruction " +
                "cannot use numeric labels until text is available.");
        }

        return builder.ToString();
    }
}
