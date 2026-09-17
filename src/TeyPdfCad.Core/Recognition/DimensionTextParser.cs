using System.Globalization;
using System.Text.RegularExpressions;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public static partial class DimensionTextParser
{
    [GeneratedRegex(@"(?<=\d)[\s\u00A0]+(?=\d)")]
    private static partial Regex DigitGroupingWhitespaceRegex();

    [GeneratedRegex(@"[+-]?\d+(?:[\.,]\d+)?")]
    private static partial Regex FirstNumberRegex();

    public static bool TryParse(string? text, out ParsedDimensionText? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = text.Trim().Replace('\u00A0', ' ');
        normalized = DigitGroupingWhitespaceRegex().Replace(normalized, string.Empty);

        var match = FirstNumberRegex().Match(normalized);
        if (!match.Success) return false;

        var numeric = match.Value.Replace(',', '.');
        if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;
        if (!double.IsFinite(value) || value <= 0) return false;

        var prefix = normalized[..match.Index].Trim();
        var kind = Classify(prefix);
        parsed = new ParsedDimensionText(kind, value, text);
        return true;
    }

    private static DimensionTextKind Classify(string prefix)
    {
        if (prefix.Contains('Ø') || prefix.Contains('⌀')) return DimensionTextKind.Diameter;

        var compact = prefix.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Equals("R", StringComparison.OrdinalIgnoreCase)) return DimensionTextKind.Radius;

        if (prefix.Length == 0 || prefix.All(c => c is '~' or '≈' or '(' or '['))
            return DimensionTextKind.Linear;

        return DimensionTextKind.Unknown;
    }
}
