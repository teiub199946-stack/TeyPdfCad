using System.Globalization;
using System.Text.Json;

namespace TeyPdfCad.Cli;

internal sealed record DimensionMetricComparisonReport(
    string SchemaVersion,
    string NativeSchemaVersion,
    string DrawingUnits,
    IReadOnlyList<string> GlobalBlockers,
    IReadOnlyList<DimensionMetricComparisonCandidate> Candidates);

internal sealed record DimensionMetricComparisonCandidate(
    string CandidateId,
    bool MeasurementsAreUsable,
    bool SourceToNativeEquivalenceProven,
    IReadOnlyList<string> Blockers,
    string SourceText,
    string NativeDimensionText,
    string? SourceFontName,
    string? SourceFontSha256,
    string? SourceFontSubtype,
    string? SourceFontEncodingName,
    bool? SourceFontHasToUnicode,
    bool? SourceFontIsSubset,
    string NativeFontFile,
    string NativeFontSha256,
    string NativeMetricKind,
    double? SourceAdvanceWidthMm,
    double? SourceVisibleWidthMm,
    double? NativeRenderedWidthMm,
    double? NativeRenderedHeightMm,
    double? VisibleWidthDeltaMm,
    double? SourceHeightMm,
    double? NativeNominalTextHeightMm,
    double? NativeTextStyleWidthFactor,
    double? NativeEntityWidthFactor);

internal static class DimensionMetricComparator
{
    public const string SchemaVersion = "1";
    private const double NumericalWidthToleranceMm = 1e-6;

    public static DimensionMetricComparisonReport Compare(
        string conversionReportJson,
        string autoCadMetricsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversionReportJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(autoCadMetricsJson);

        using var sourceDocument = JsonDocument.Parse(conversionReportJson);
        using var nativeDocument = JsonDocument.Parse(autoCadMetricsJson);

        var sourceItems = ReadSourceEvidence(sourceDocument.RootElement);
        var nativeItems = ReadNativeDimensions(nativeDocument.RootElement);
        var sourceByCandidate = sourceItems
            .Where(item => !string.IsNullOrWhiteSpace(item.CandidateId))
            .GroupBy(item => item.CandidateId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

        var nativeByCandidate = nativeItems
            .Where(item => !string.IsNullOrWhiteSpace(item.CandidateId))
            .GroupBy(item => item.CandidateId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

        var nativeSchemaVersion = GetString(nativeDocument.RootElement, "schemaVersion") ?? string.Empty;
        var drawingUnits = GetString(nativeDocument.RootElement, "drawingUnits") ?? string.Empty;
        var globalBlockers = new List<string>();

        if (!string.Equals(nativeSchemaVersion, "2", StringComparison.Ordinal))
            globalBlockers.Add("unsupported-native-metrics-schema");
        if (!TryGetArray(sourceDocument.RootElement, "pages", out _))
            globalBlockers.Add("source-report-pages-missing");
        if (!TryGetArray(nativeDocument.RootElement, "dimensions", out _))
            globalBlockers.Add("native-metrics-dimensions-missing");
        if (sourceItems.Any(item => string.IsNullOrWhiteSpace(item.CandidateId)))
            globalBlockers.Add("source-candidate-id-missing");
        if (nativeItems.Any(item => string.IsNullOrWhiteSpace(item.CandidateId)))
            globalBlockers.Add("native-candidate-id-missing");
        if (!string.Equals(drawingUnits, "Millimeters", StringComparison.OrdinalIgnoreCase))
            globalBlockers.Add("native-drawing-units-not-millimeters");

        var normalizedGlobalBlockers = globalBlockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var globalMeasurementsUsable = normalizedGlobalBlockers.Length == 0;

        var candidateIds = sourceByCandidate.Keys
            .Concat(nativeByCandidate.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var candidates = candidateIds
            .Select(candidateId => CompareCandidate(
                candidateId,
                sourceByCandidate.TryGetValue(candidateId, out var source) ? source : [],
                nativeByCandidate.TryGetValue(candidateId, out var native) ? native : [],
                drawingUnits,
                globalMeasurementsUsable))
            .ToArray();

        return new DimensionMetricComparisonReport(
            SchemaVersion,
            nativeSchemaVersion,
            drawingUnits,
            normalizedGlobalBlockers,
            candidates);
    }

    private static DimensionMetricComparisonCandidate CompareCandidate(
        string candidateId,
        IReadOnlyList<SourceEvidence> sources,
        IReadOnlyList<NativeDimensionEvidence> natives,
        string drawingUnits,
        bool globalMeasurementsUsable)
    {
        var blockers = new List<string>();

        if (sources.Count != 1)
            blockers.Add("source-candidate-count-not-one");
        if (natives.Count != 1)
            blockers.Add("native-candidate-count-not-one");
        if (!string.Equals(drawingUnits, "Millimeters", StringComparison.OrdinalIgnoreCase))
            blockers.Add("native-drawing-units-not-millimeters");

        var source = sources.Count == 1 ? sources[0] : SourceEvidence.Empty(candidateId);
        var native = natives.Count == 1 ? natives[0] : NativeDimensionEvidence.Empty(candidateId);

        if (natives.Count == 1)
        {
            if (!string.Equals(native.CandidateRole, "primary", StringComparison.Ordinal))
                blockers.Add("native-candidate-role-not-primary");
            if (!string.IsNullOrWhiteSpace(native.Error))
                blockers.Add("native-dimension-metrics-error");
            if (!string.Equals(source.SourceText, native.DimensionText, StringComparison.Ordinal))
                blockers.Add("dimension-text-mismatch");
        }

        NativeTextMetric metric = NativeTextMetric.Empty;
        if (natives.Count == 1)
        {
            if (native.TextMetrics.Count != 1)
            {
                blockers.Add("native-text-metric-count-not-one");
            }
            else
            {
                metric = native.TextMetrics[0];
                if (!string.Equals(metric.Text, source.SourceText, StringComparison.Ordinal))
                    blockers.Add("rendered-text-mismatch");
                if (!IsSupportedMetricKind(metric.MetricKind))
                    blockers.Add("native-metric-kind-not-supported");
                if (!IsPositiveFinite(metric.Width))
                    blockers.Add("native-rendered-width-invalid");
                if (!IsPositiveFinite(metric.Height))
                    blockers.Add("native-rendered-height-invalid");
                if (!string.Equals(metric.TextStyleName, "TEYPDFCAD_TEXT", StringComparison.Ordinal))
                    blockers.Add("native-text-style-not-teypdfcad");
                if (!string.Equals(metric.FontFile, "arial.ttf", StringComparison.OrdinalIgnoreCase))
                    blockers.Add("native-font-file-not-arial");
                if (string.IsNullOrWhiteSpace(metric.FontResolvedPath))
                    blockers.Add("native-font-resolved-path-missing");
                if (!IsSha256(metric.FontSha256))
                    blockers.Add("native-font-sha256-invalid");
                if (!IsPositiveFinite(metric.NominalTextHeight))
                    blockers.Add("native-nominal-text-height-invalid");
                if (!IsPositiveFinite(metric.TextStyleWidthFactor))
                    blockers.Add("native-text-style-width-factor-invalid");
                if (!IsPositiveFinite(metric.EntityWidthFactor))
                    blockers.Add("native-entity-width-factor-invalid");
            }
        }

        if (!IsPositiveFinite(source.SourceAdvanceWidthMm))
            blockers.Add("source-advance-width-invalid");
        if (!IsPositiveFinite(source.SourceVisibleWidthMm))
            blockers.Add("source-visible-width-invalid");
        if (!IsPositiveFinite(source.SourceHeightMm))
            blockers.Add("source-height-invalid");

        if (!IsFirstSafeNumericDimensionText(source.SourceText))
            blockers.Add("source-text-outside-safe-numeric-subset");

        if (!IsSha256(source.SourceFontSha256))
        {
            blockers.Add("source-font-program-fingerprint-unavailable");
        }
        else if (IsSha256(metric.FontSha256)
            && !string.Equals(
                source.SourceFontSha256,
                metric.FontSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("font-program-sha256-mismatch");
        }

        if (!string.Equals(
                source.SourceFontSubtype,
                "TrueType",
                StringComparison.Ordinal))
            blockers.Add("source-font-subtype-not-simple-truetype");
        if (!string.Equals(
                source.SourceFontEncodingName,
                "WinAnsiEncoding",
                StringComparison.Ordinal))
            blockers.Add("source-font-encoding-not-winansi");
        if (source.SourceFontHasToUnicode != false)
            blockers.Add("source-font-tounicode-not-absent");
        if (source.SourceFontIsSubset != false)
            blockers.Add("source-font-is-subset-or-unknown");

        double? widthDelta = null;
        if (IsPositiveFinite(source.SourceVisibleWidthMm)
            && IsPositiveFinite(metric.Width))
        {
            widthDelta = metric.Width!.Value - source.SourceVisibleWidthMm!.Value;
            if (Math.Abs(widthDelta.Value) > NumericalWidthToleranceMm)
                blockers.Add("visible-width-mismatch");
        }

        // Even when every currently captured scalar matches, source->native text
        // appearance is not authorized until the project has independently
        // proven that the compared PDF metric is a real glyph/ink metric and
        // that AutoCAD's generated anonymous-block text carries the same glyph
        // outlines/fallback behavior after REGEN.
        blockers.Add("rendered-glyph-equivalence-not-yet-authorized");

        var distinctBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var measurementsUsable = globalMeasurementsUsable
            && sources.Count == 1
            && natives.Count == 1
            && native.TextMetrics.Count == 1
            && string.Equals(drawingUnits, "Millimeters", StringComparison.OrdinalIgnoreCase)
            && IsPositiveFinite(source.SourceVisibleWidthMm)
            && IsPositiveFinite(source.SourceAdvanceWidthMm)
            && IsPositiveFinite(source.SourceHeightMm)
            && IsPositiveFinite(metric.Width)
            && IsPositiveFinite(metric.Height)
            && IsSupportedMetricKind(metric.MetricKind)
            && !distinctBlockers.Contains("native-dimension-metrics-error", StringComparer.Ordinal);

        return new DimensionMetricComparisonCandidate(
            candidateId,
            measurementsUsable,
            SourceToNativeEquivalenceProven: false,
            distinctBlockers,
            source.SourceText,
            native.DimensionText,
            source.SourceFontName,
            source.SourceFontSha256,
            source.SourceFontSubtype,
            source.SourceFontEncodingName,
            source.SourceFontHasToUnicode,
            source.SourceFontIsSubset,
            metric.FontFile,
            metric.FontSha256,
            metric.MetricKind,
            source.SourceAdvanceWidthMm,
            source.SourceVisibleWidthMm,
            metric.Width,
            metric.Height,
            widthDelta,
            source.SourceHeightMm,
            metric.NominalTextHeight,
            metric.TextStyleWidthFactor,
            metric.EntityWidthFactor);
    }

    private static IReadOnlyList<SourceEvidence> ReadSourceEvidence(JsonElement root)
    {
        if (!TryGetArray(root, "pages", out var pages))
            return [];

        var output = new List<SourceEvidence>();
        foreach (var page in pages.EnumerateArray())
        {
            if (!TryGetArray(page, "dimensionMetricEvidence", out var evidence))
                continue;

            foreach (var item in evidence.EnumerateArray())
            {
                var candidateId = GetString(item, "candidateId") ?? string.Empty;
                output.Add(new SourceEvidence(
                    candidateId,
                    GetString(item, "sourceText") ?? string.Empty,
                    GetString(item, "sourceFontName"),
                    GetString(item, "sourceFontSha256"),
                    GetString(item, "sourceFontSubtype"),
                    GetString(item, "sourceFontEncodingName"),
                    GetNullableBool(item, "sourceFontHasToUnicode"),
                    GetNullableBool(item, "sourceFontIsSubset"),
                    GetNullableDouble(item, "sourceAdvanceWidthMm"),
                    GetNullableDouble(item, "sourceVisibleWidthMm"),
                    GetNullableDouble(item, "sourceHeightMm")));
            }
        }

        return output;
    }

    private static IReadOnlyList<NativeDimensionEvidence> ReadNativeDimensions(JsonElement root)
    {
        if (!TryGetArray(root, "dimensions", out var dimensions))
            return [];

        var output = new List<NativeDimensionEvidence>();
        foreach (var dimension in dimensions.EnumerateArray())
        {
            var textMetrics = new List<NativeTextMetric>();
            if (TryGetArray(dimension, "textMetrics", out var metrics))
            {
                foreach (var metric in metrics.EnumerateArray())
                {
                    textMetrics.Add(new NativeTextMetric(
                        GetString(metric, "text") ?? string.Empty,
                        GetString(metric, "metricKind") ?? string.Empty,
                        GetNullableDouble(metric, "width"),
                        GetNullableDouble(metric, "height"),
                        GetString(metric, "textStyleName") ?? string.Empty,
                        GetString(metric, "fontFile") ?? string.Empty,
                        GetString(metric, "fontResolvedPath") ?? string.Empty,
                        GetString(metric, "fontSha256") ?? string.Empty,
                        GetNullableDouble(metric, "nominalTextHeight"),
                        GetNullableDouble(metric, "textStyleWidthFactor"),
                        GetNullableDouble(metric, "entityWidthFactor")));
                }
            }

            output.Add(new NativeDimensionEvidence(
                GetString(dimension, "candidateId") ?? string.Empty,
                GetString(dimension, "candidateRole") ?? string.Empty,
                GetString(dimension, "dimensionText") ?? string.Empty,
                GetString(dimension, "error"),
                textMetrics));
        }

        return output;
    }

    private static bool IsSupportedMetricKind(string value)
        // MText.ActualWidth is a text-layout width. DBText.GeometricExtents is
        // an axis-aligned box in dimension-block coordinates and is not a safe
        // baseline-visible-width proof for rotated text.
        => string.Equals(
            value,
            "mtext-actual-bounds-dimblock-mcs",
            StringComparison.Ordinal);

    private static bool IsFirstSafeNumericDimensionText(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var separatorSeen = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character >= '0' && character <= '9')
                continue;

            if ((character == '.' || character == ',')
                && !separatorSeen
                && index > 0
                && index < value.Length - 1)
            {
                separatorSeen = true;
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 }
            && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'A' && character <= 'F')
                || (character >= 'a' && character <= 'f'));

    private static bool IsPositiveFinite(double? value)
        => value.HasValue
            && double.IsFinite(value.Value)
            && value.Value > 0d;

    private static bool TryGetArray(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetNullableBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? GetNullableDouble(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var parsed)
            && double.IsFinite(parsed)
                ? parsed
                : null;
    }

    private sealed record SourceEvidence(
        string CandidateId,
        string SourceText,
        string? SourceFontName,
        string? SourceFontSha256,
        string? SourceFontSubtype,
        string? SourceFontEncodingName,
        bool? SourceFontHasToUnicode,
        bool? SourceFontIsSubset,
        double? SourceAdvanceWidthMm,
        double? SourceVisibleWidthMm,
        double? SourceHeightMm)
    {
        public static SourceEvidence Empty(string candidateId)
            => new(
                candidateId,
                string.Empty,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
    }

    private sealed record NativeDimensionEvidence(
        string CandidateId,
        string CandidateRole,
        string DimensionText,
        string? Error,
        IReadOnlyList<NativeTextMetric> TextMetrics)
    {
        public static NativeDimensionEvidence Empty(string candidateId)
            => new(candidateId, string.Empty, string.Empty, null, []);
    }

    private sealed record NativeTextMetric(
        string Text,
        string MetricKind,
        double? Width,
        double? Height,
        string TextStyleName,
        string FontFile,
        string FontResolvedPath,
        string FontSha256,
        double? NominalTextHeight,
        double? TextStyleWidthFactor,
        double? EntityWidthFactor)
    {
        public static NativeTextMetric Empty { get; } = new(
            string.Empty,
            string.Empty,
            null,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            null,
            null);
    }
}
