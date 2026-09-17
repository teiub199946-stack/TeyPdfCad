using System.Globalization;
using System.Text.RegularExpressions;

namespace TeyPdfCad.AutoCAD;

internal static class DimensionTextOverrideBuilder
{
    private static readonly Regex NumberRegex = new(
        @"[+-]?\d(?:[\d\s\u00A0]*\d)?(?:[\.,]\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Build(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return string.Empty;

        var match = NumberRegex.Match(sourceText);
        return match.Success ? BuildOverride(sourceText, match) : string.Empty;
    }

    public static string Build(string sourceText, double displayedValue)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || double.IsNaN(displayedValue) || double.IsInfinity(displayedValue))
            return string.Empty;

        var matches = NumberRegex.Matches(sourceText);
        if (matches.Count == 0) return string.Empty;

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

        return best is null ? string.Empty : BuildOverride(sourceText, best);
    }

    private static string BuildOverride(string sourceText, Match match)
    {
        var prefix = sourceText.Substring(0, match.Index);
        var suffix = sourceText.Substring(match.Index + match.Length);

        // A plain numeric label needs no user override; let the active dimension style format it.
        return string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix)
            ? string.Empty
            : $"{prefix}<>{suffix}";
    }

    private static double DistanceToDisplayedValue(string token, double displayedValue)
    {
        var compact = token.Replace(" ", string.Empty)
            .Replace("\u00A0", string.Empty);

        var candidates = new List<double>(2);
        var decimalNormalized = compact.Replace(',', '.');
        if (double.TryParse(decimalNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            candidates.Add(decimalValue);

        var separatorStripped = compact.Replace(",", string.Empty)
            .Replace(".", string.Empty);
        if (double.TryParse(separatorStripped, NumberStyles.Float, CultureInfo.InvariantCulture, out var strippedValue))
            candidates.Add(strippedValue);

        return candidates.Count == 0
            ? double.PositiveInfinity
            : candidates.Min(value => Math.Abs(value - displayedValue));
    }
}
