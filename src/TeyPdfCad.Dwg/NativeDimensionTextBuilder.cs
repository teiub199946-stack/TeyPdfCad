using System.Globalization;
using System.Text.RegularExpressions;

namespace TeyPdfCad.Dwg;

internal static class NativeDimensionTextBuilder
{
    private static readonly Regex NumberRegex = new(
        @"[+-]?\d(?:[\d\s\u00A0]*\d)?(?:[\.,]\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Build(string sourceText, double displayedValue)
    {
        if (string.IsNullOrWhiteSpace(sourceText)
            || double.IsNaN(displayedValue)
            || double.IsInfinity(displayedValue))
        {
            return string.Empty;
        }

        var matches = NumberRegex.Matches(sourceText);
        if (matches.Count == 0)
            return string.Empty;

        Match? best = null;
        var bestError = double.PositiveInfinity;
        foreach (Match match in matches)
        {
            var error = DistanceToDisplayedValue(match.Value, displayedValue);
            if (error < bestError)
            {
                best = match;
                bestError = error;
            }
        }

        if (best is null)
            return string.Empty;

        var prefix = sourceText[..best.Index];
        var suffix = sourceText[(best.Index + best.Length)..];
        return string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix)
            ? string.Empty
            : $"{prefix}<>{suffix}";
    }

    private static double DistanceToDisplayedValue(
        string token,
        double displayedValue)
    {
        var compact = token
            .Replace(" ", string.Empty)
            .Replace("\u00A0", string.Empty);

        var candidates = new List<double>(2);
        var decimalNormalized = compact.Replace(',', '.');
        if (double.TryParse(
            decimalNormalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var decimalValue))
        {
            candidates.Add(decimalValue);
        }

        var separatorStripped = compact
            .Replace(",", string.Empty)
            .Replace(".", string.Empty);
        if (double.TryParse(
            separatorStripped,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var strippedValue))
        {
            candidates.Add(strippedValue);
        }

        return candidates.Count == 0
            ? double.PositiveInfinity
            : candidates.Min(value => Math.Abs(value - displayedValue));
    }
}
