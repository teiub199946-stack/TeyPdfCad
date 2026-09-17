using System.Globalization;
using System.Text.RegularExpressions;

namespace TeyPdfCad.AutoCAD;

internal static partial class DimensionTextOverrideBuilder
{
    [GeneratedRegex(@"[+-]?\d(?:[\d\s\u00A0]*\d)?(?:[\.,]\d+)?")]
    private static partial Regex NumberRegex();

    public static string Build(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return string.Empty;

        var match = NumberRegex().Match(sourceText);
        return match.Success ? BuildOverride(sourceText, match) : string.Empty;
    }

    public static string Build(string sourceText, double displayedValue)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || !double.IsFinite(displayedValue))
            return string.Empty;

        var matches = NumberRegex().Matches(sourceText);
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
        var prefix = sourceText[..match.Index];
        var suffix = sourceText[(match.Index + match.Length)..];

        // A plain numeric label needs no user override; let the active dimension style format it.
        return string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix)
            ? string.Empty
            : $"{prefix}<>{suffix}";
    }

    private static double DistanceToDisplayedValue(string token, double displayedValue)
    {
        var compact = token.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);

        var candidates = new List<double>(2);
        var decimalNormalized = compact.Replace(',', '.');
        if (double.TryParse(decimalNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            candidates.Add(decimalValue);

        var separatorStripped = compact.Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        if (double.TryParse(separatorStripped, NumberStyles.Float, CultureInfo.InvariantCulture, out var strippedValue))
            candidates.Add(strippedValue);

        return candidates.Count == 0
            ? double.PositiveInfinity
            : candidates.Min(value => Math.Abs(value - displayedValue));
    }
}
