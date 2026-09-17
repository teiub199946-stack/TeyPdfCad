using System.Text.RegularExpressions;

namespace TeyPdfCad.AutoCAD;

internal static partial class DimensionTextOverrideBuilder
{
    [GeneratedRegex(@"[+-]?\d(?:[\d\s\u00A0]*\d)?(?:[\.,]\d+)?")]
    private static partial Regex FirstNumberRegex();

    public static string Build(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return string.Empty;

        var match = FirstNumberRegex().Match(sourceText);
        if (!match.Success) return string.Empty;

        var prefix = sourceText[..match.Index];
        var suffix = sourceText[(match.Index + match.Length)..];
        var overrideText = $"{prefix}<>{suffix}";

        // A plain numeric label needs no user override; let the active dimension style format it.
        return string.IsNullOrWhiteSpace(prefix) && string.IsNullOrWhiteSpace(suffix)
            ? string.Empty
            : overrideText;
    }
}
