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
    double? SourceVisibleHeightMm,
    double? SourceGlyphInkWidthMm,
    double? SourceGlyphInkHeightMm,
    double? NativeRenderedWidthMm,
    double? NativeRenderedHeightMm,
    double? VisibleWidthDeltaMm,
    double? FragmentWidthDeltaMm,
    double? ProjectedHeightDeltaMm,
    double? GlyphInkWidthDeltaMm,
    double? GlyphInkHeightDeltaMm,
    double? SourceHeightMm,
    double? NativeNominalTextHeightMm,
    double? NativeTextStyleWidthFactor,
    double? NativeEntityWidthFactor,
    int NativeBlockGeometryCount,
    IReadOnlyList<string> NativeBlockGeometryKinds,
    int NativeExplodedGeometryCount,
    IReadOnlyList<string> NativeExplodedGeometryKinds,
    double? BaselineDirectionDot,
    int SourceDimensionLineCount,
    int MatchedSourceDimensionLineCount,
    int SourceExtensionLineCount,
    int MatchedSourceExtensionLineCount,
    int SourceArrowLineCount,
    int MatchedSourceArrowLineCount);

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

        if (!string.Equals(nativeSchemaVersion, "7", StringComparison.Ordinal))
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

            // In the first safe subset the DIMENSION must remain semantically
            // live: AutoCAD generates the displayed numeric text from the
            // measurement. A non-empty DimensionText is an explicit override
            // and would freeze the label after geometry edits.
            if (IsFirstSafeNumericDimensionText(source.SourceText)
                && !string.IsNullOrEmpty(native.DimensionText))
            {
                blockers.Add("native-text-override-present");
            }
        }

        if (natives.Count == 1)
        {
            if (!string.Equals(
                    native.BlockGeometryCoordinateFrame,
                    "dimension-block-mcs",
                    StringComparison.Ordinal))
            {
                blockers.Add("native-block-coordinate-frame-unsupported");
            }
            if (!string.Equals(
                    native.ExplodedGeometryCoordinateFrame,
                    "drawing-wcs",
                    StringComparison.Ordinal))
            {
                blockers.Add("native-exploded-coordinate-frame-unsupported");
            }

            ValidateGeometryEvidence(
                native.BlockGeometry,
                "native-block",
                blockers);
            ValidateGeometryEvidence(
                native.ExplodedGeometry,
                "native-exploded",
                blockers);
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
                if (metric.BackgroundFill)
                    blockers.Add("native-mtext-background-fill-present");
                if (metric.UseBackgroundColor)
                    blockers.Add("native-mtext-background-color-present");
                if (metric.ShowBorders)
                    blockers.Add("native-mtext-border-present");
                if (string.IsNullOrWhiteSpace(metric.Attachment))
                    blockers.Add("native-mtext-attachment-missing");

                if (metric.Fragments.Count != 1)
                {
                    blockers.Add("native-fragment-count-not-one");
                }
                else
                {
                    var fragment = metric.Fragments[0];
                    if (!string.Equals(fragment.Text, source.SourceText, StringComparison.Ordinal))
                        blockers.Add("native-fragment-text-mismatch");
                    if (string.IsNullOrWhiteSpace(fragment.TrueTypeFont))
                        blockers.Add("native-fragment-truetype-font-missing");
                    if (!string.IsNullOrWhiteSpace(fragment.ShxFont))
                        blockers.Add("native-fragment-shx-font-present");
                    if (!IsPositiveFinite(fragment.ExtentWidth)
                        || !IsPositiveFinite(fragment.ExtentHeight)
                        || !IsPositiveFinite(fragment.CapsHeight))
                    {
                        blockers.Add("native-fragment-extents-invalid");
                    }
                    if (!AlmostEqual(fragment.TrackingFactor, 1d))
                        blockers.Add("native-fragment-tracking-not-one");
                    if (!AlmostEqual(fragment.WidthFactor, 1d))
                        blockers.Add("native-fragment-width-factor-not-one");
                    if (!AlmostEqual(fragment.ObliqueAngle, 0d))
                        blockers.Add("native-fragment-oblique-not-zero");
                    if (fragment.Bold || fragment.Italic)
                        blockers.Add("native-fragment-font-style-not-plain");
                    if (fragment.StackTop
                        || fragment.StackBottom
                        || fragment.Underlined
                        || fragment.Overlined
                        || fragment.Strikethrough)
                    {
                        blockers.Add("native-fragment-formatting-not-plain");
                    }

                    if (!TryGetPlanarUnitDirection(
                            fragment,
                            out _,
                            out _))
                    {
                        blockers.Add("native-fragment-direction-not-planar");
                    }
                }
            }
        }

        double? baselineDirectionDot = null;
        if (metric.Fragments.Count == 1
            && double.IsFinite(source.SourceRotationDegrees)
            && TryGetPlanarUnitDirection(
                metric.Fragments[0],
                out var nativeX,
                out var nativeY))
        {
            var sourceRadians = source.SourceRotationDegrees * Math.PI / 180d;
            var sourceX = Math.Cos(sourceRadians);
            var sourceY = Math.Sin(sourceRadians);
            baselineDirectionDot = sourceX * nativeX + sourceY * nativeY;

            if (Math.Abs(sourceX - nativeX) > 1e-6
                || Math.Abs(sourceY - nativeY) > 1e-6)
            {
                blockers.Add("source-native-baseline-direction-mismatch");
            }
        }

        if (!IsPositiveFinite(source.SourceAdvanceWidthMm))
            blockers.Add("source-advance-width-invalid");
        if (!IsPositiveFinite(source.SourceVisibleWidthMm))
            blockers.Add("source-visible-width-invalid");
        else
            blockers.Add("source-visible-width-is-bbox-projection-not-ink");
        if (!IsPositiveFinite(source.SourceVisibleHeightMm))
            blockers.Add("source-visible-height-invalid");
        else
            blockers.Add("source-visible-height-is-bbox-projection-not-ink");
        if (!IsPositiveFinite(source.SourceGlyphInkWidthMm)
            || !IsPositiveFinite(source.SourceGlyphInkHeightMm))
        {
            blockers.Add("source-glyph-ink-extents-invalid");
        }
        if (!IsPositiveFinite(source.SourceHeightMm))
            blockers.Add("source-height-invalid");

        if (!string.Equals(
                source.SourceCoordinateFrame,
                "drawing-wcs-model-mm",
                StringComparison.Ordinal))
        {
            blockers.Add("source-geometry-coordinate-frame-unsupported");
        }

        var explodedLines = native.ExplodedGeometry
            .Where(item => string.Equals(
                item.GeometryKind,
                "line",
                StringComparison.Ordinal))
            .ToArray();

        var sourceDimensionLines = source.SourceLineGeometry
            .Where(item => string.Equals(
                item.Role,
                "dimension-line",
                StringComparison.Ordinal))
            .ToArray();
        if (sourceDimensionLines.Length != 1)
            blockers.Add("source-dimension-line-count-not-one");
        var matchedSourceDimensionLineCount = CountUniqueLineMatches(
            sourceDimensionLines,
            explodedLines,
            NumericalWidthToleranceMm);
        if (sourceDimensionLines.Length > 0
            && matchedSourceDimensionLineCount != sourceDimensionLines.Length)
        {
            blockers.Add("source-dimension-line-unmatched");
        }

        var sourceExtensionLines = source.SourceLineGeometry
            .Where(item => string.Equals(
                item.Role,
                "extension-line",
                StringComparison.Ordinal))
            .ToArray();
        if (sourceExtensionLines.Length != 2)
            blockers.Add("source-extension-line-count-not-two");
        var matchedSourceExtensionLineCount = CountUniqueLineMatches(
            sourceExtensionLines,
            explodedLines,
            NumericalWidthToleranceMm);
        if (sourceExtensionLines.Length > 0
            && matchedSourceExtensionLineCount != sourceExtensionLines.Length)
        {
            blockers.Add("source-extension-line-unmatched");
        }

        var sourceArrowLines = source.SourceLineGeometry
            .Where(item => string.Equals(
                item.Role,
                "arrow-geometry",
                StringComparison.Ordinal))
            .ToArray();
        if (sourceArrowLines.Length != 2)
            blockers.Add("source-arrow-line-count-not-two");
        var matchedSourceArrowLineCount = CountUniqueLineMatches(
            sourceArrowLines,
            explodedLines,
            NumericalWidthToleranceMm);
        if (sourceArrowLines.Length > 0
            && matchedSourceArrowLineCount != sourceArrowLines.Length)
        {
            blockers.Add("source-arrow-line-unmatched");
        }

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

        double? fragmentWidthDelta = null;
        double? projectedHeightDelta = null;
        double? glyphInkWidthDelta = null;
        double? glyphInkHeightDelta = null;
        if (metric.Fragments.Count == 1)
        {
            var fragment = metric.Fragments[0];
            if (IsPositiveFinite(source.SourceVisibleWidthMm)
                && IsPositiveFinite(fragment.ExtentWidth))
            {
                fragmentWidthDelta =
                    fragment.ExtentWidth!.Value - source.SourceVisibleWidthMm!.Value;
                if (Math.Abs(fragmentWidthDelta.Value) > NumericalWidthToleranceMm)
                    blockers.Add("native-fragment-width-mismatch");
            }

            if (IsPositiveFinite(source.SourceVisibleHeightMm)
                && IsPositiveFinite(fragment.ExtentHeight))
            {
                projectedHeightDelta =
                    fragment.ExtentHeight!.Value - source.SourceVisibleHeightMm!.Value;
                if (Math.Abs(projectedHeightDelta.Value) > NumericalWidthToleranceMm)
                    blockers.Add("projected-height-mismatch");
            }

            if (IsPositiveFinite(source.SourceGlyphInkWidthMm)
                && IsPositiveFinite(fragment.ExtentWidth))
            {
                glyphInkWidthDelta =
                    fragment.ExtentWidth!.Value - source.SourceGlyphInkWidthMm!.Value;
                if (Math.Abs(glyphInkWidthDelta.Value) > NumericalWidthToleranceMm)
                    blockers.Add("glyph-ink-width-mismatch");
            }

            if (IsPositiveFinite(source.SourceGlyphInkHeightMm)
                && IsPositiveFinite(fragment.ExtentHeight))
            {
                glyphInkHeightDelta =
                    fragment.ExtentHeight!.Value - source.SourceGlyphInkHeightMm!.Value;
                if (Math.Abs(glyphInkHeightDelta.Value) > NumericalWidthToleranceMm)
                    blockers.Add("glyph-ink-height-mismatch");
            }
        }

        // Even when every currently captured scalar matches, source->native text
        // appearance is not authorized until the project has independently
        // proven that the compared PDF metric is a real glyph/ink metric and
        // that AutoCAD's generated anonymous-block text carries the same glyph
        // outlines/fallback behavior after REGEN.
        blockers.Add("rendered-glyph-equivalence-not-yet-authorized");
        blockers.Add("anonymous-block-structural-equivalence-not-yet-authorized");
        blockers.Add("exploded-geometry-source-equivalence-not-yet-authorized");

        var distinctBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var measurementsUsable = globalMeasurementsUsable
            && sources.Count == 1
            && natives.Count == 1
            && native.TextMetrics.Count == 1
            && native.BlockGeometry.Count > 0
            && native.ExplodedGeometry.Count > 0
            && string.Equals(
                native.BlockGeometryCoordinateFrame,
                "dimension-block-mcs",
                StringComparison.Ordinal)
            && string.Equals(
                native.ExplodedGeometryCoordinateFrame,
                "drawing-wcs",
                StringComparison.Ordinal)
            && metric.Fragments.Count == 1
            && string.Equals(drawingUnits, "Millimeters", StringComparison.OrdinalIgnoreCase)
            && IsPositiveFinite(source.SourceVisibleWidthMm)
            && IsPositiveFinite(source.SourceVisibleHeightMm)
            && IsPositiveFinite(source.SourceGlyphInkWidthMm)
            && IsPositiveFinite(source.SourceGlyphInkHeightMm)
            && IsPositiveFinite(source.SourceAdvanceWidthMm)
            && IsPositiveFinite(source.SourceHeightMm)
            && string.Equals(
                source.SourceCoordinateFrame,
                "drawing-wcs-model-mm",
                StringComparison.Ordinal)
            && sourceDimensionLines.Length == 1
            && matchedSourceDimensionLineCount == 1
            && sourceExtensionLines.Length == 2
            && matchedSourceExtensionLineCount == 2
            && sourceArrowLines.Length == 2
            && matchedSourceArrowLineCount == 2
            && baselineDirectionDot.HasValue
            && baselineDirectionDot.Value > 0.999999
            && IsPositiveFinite(metric.Width)
            && IsPositiveFinite(metric.Height)
            && IsSupportedMetricKind(metric.MetricKind)
            && !distinctBlockers.Contains("native-dimension-metrics-error", StringComparer.Ordinal)
            && !distinctBlockers.Any(blocker =>
                blocker.StartsWith("native-fragment-", StringComparison.Ordinal))
            && !distinctBlockers.Any(blocker =>
                blocker.StartsWith("native-mtext-", StringComparison.Ordinal))
            && !distinctBlockers.Any(blocker =>
                blocker.StartsWith("native-block-", StringComparison.Ordinal))
            && !distinctBlockers.Any(blocker =>
                blocker.StartsWith("native-exploded-", StringComparison.Ordinal));

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
            source.SourceVisibleHeightMm,
            source.SourceGlyphInkWidthMm,
            source.SourceGlyphInkHeightMm,
            metric.Width,
            metric.Height,
            widthDelta,
            fragmentWidthDelta,
            projectedHeightDelta,
            glyphInkWidthDelta,
            glyphInkHeightDelta,
            source.SourceHeightMm,
            metric.NominalTextHeight,
            metric.TextStyleWidthFactor,
            metric.EntityWidthFactor,
            native.BlockGeometry.Count,
            native.BlockGeometry
                .Select(item => item.GeometryKind)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            native.ExplodedGeometry.Count,
            native.ExplodedGeometry
                .Select(item => item.GeometryKind)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray(),
            baselineDirectionDot,
            sourceDimensionLines.Length,
            matchedSourceDimensionLineCount,
            sourceExtensionLines.Length,
            matchedSourceExtensionLineCount,
            sourceArrowLines.Length,
            matchedSourceArrowLineCount);
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
                var sourceLineGeometry = new List<SourceLineGeometry>();
                if (TryGetArray(item, "sourceLineGeometry", out var sourceGeometryArray))
                {
                    foreach (var line in sourceGeometryArray.EnumerateArray())
                    {
                        sourceLineGeometry.Add(new SourceLineGeometry(
                            GetString(line, "role") ?? string.Empty,
                            GetNullableDouble(line, "startX"),
                            GetNullableDouble(line, "startY"),
                            GetNullableDouble(line, "endX"),
                            GetNullableDouble(line, "endY")));
                    }
                }

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
                    GetNullableDouble(item, "sourceVisibleHeightMm"),
                    GetNullableDouble(item, "sourceGlyphInkWidthMm"),
                    GetNullableDouble(item, "sourceGlyphInkHeightMm"),
                    GetNullableDouble(item, "sourceHeightMm"),
                    GetNullableDouble(item, "sourceRotationDegrees") ?? double.NaN,
                    GetString(item, "sourceCoordinateFrame") ?? string.Empty,
                    sourceLineGeometry));
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
                    var fragments = new List<NativeFragmentMetric>();
                    if (TryGetArray(metric, "fragments", out var fragmentArray))
                    {
                        foreach (var fragment in fragmentArray.EnumerateArray())
                        {
                            fragments.Add(new NativeFragmentMetric(
                                GetString(fragment, "text") ?? string.Empty,
                                GetString(fragment, "trueTypeFont") ?? string.Empty,
                                GetString(fragment, "shxFont") ?? string.Empty,
                                GetNullableDouble(fragment, "extentWidth"),
                                GetNullableDouble(fragment, "extentHeight"),
                                GetNullableDouble(fragment, "capsHeight"),
                                GetNullableDouble(fragment, "trackingFactor"),
                                GetNullableDouble(fragment, "widthFactor"),
                                GetNullableDouble(fragment, "obliqueAngle"),
                                GetNullableDouble(fragment, "locationX"),
                                GetNullableDouble(fragment, "locationY"),
                                GetNullableDouble(fragment, "locationZ"),
                                GetNullableDouble(fragment, "directionX"),
                                GetNullableDouble(fragment, "directionY"),
                                GetNullableDouble(fragment, "directionZ"),
                                GetNullableBool(fragment, "bold") ?? false,
                                GetNullableBool(fragment, "italic") ?? false,
                                GetNullableBool(fragment, "stackTop") ?? false,
                                GetNullableBool(fragment, "stackBottom") ?? false,
                                GetNullableBool(fragment, "underlined") ?? false,
                                GetNullableBool(fragment, "overlined") ?? false,
                                GetNullableBool(fragment, "strikethrough") ?? false));
                        }
                    }

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
                        GetNullableDouble(metric, "entityWidthFactor"),
                        GetNullableBool(metric, "backgroundFill") ?? false,
                        GetNullableBool(metric, "useBackgroundColor") ?? false,
                        GetNullableDouble(metric, "backgroundScaleFactor"),
                        GetNullableBool(metric, "showBorders") ?? false,
                        GetString(metric, "attachment") ?? string.Empty,
                        fragments));
                }
            }

            var blockGeometry = new List<NativeBlockGeometry>();
            if (TryGetArray(dimension, "blockGeometry", out var geometryArray))
            {
                foreach (var geometry in geometryArray.EnumerateArray())
                {
                    blockGeometry.Add(new NativeBlockGeometry(
                        GetString(geometry, "entityType") ?? string.Empty,
                        GetString(geometry, "entityHandle") ?? string.Empty,
                        GetString(geometry, "geometryKind") ?? string.Empty,
                        GetNullableDouble(geometry, "startX"),
                        GetNullableDouble(geometry, "startY"),
                        GetNullableDouble(geometry, "startZ"),
                        GetNullableDouble(geometry, "endX"),
                        GetNullableDouble(geometry, "endY"),
                        GetNullableDouble(geometry, "endZ"),
                        GetNullableDouble(geometry, "minX"),
                        GetNullableDouble(geometry, "minY"),
                        GetNullableDouble(geometry, "minZ"),
                        GetNullableDouble(geometry, "maxX"),
                        GetNullableDouble(geometry, "maxY"),
                        GetNullableDouble(geometry, "maxZ"),
                        GetString(geometry, "nestedBlockName") ?? string.Empty,
                        GetNullableInt(geometry, "vertexCount") ?? 0));
                }
            }

            var explodedGeometry = ReadGeometryEvidence(
                dimension,
                "explodedGeometry");

            output.Add(new NativeDimensionEvidence(
                GetString(dimension, "candidateId") ?? string.Empty,
                GetString(dimension, "candidateRole") ?? string.Empty,
                GetString(dimension, "dimensionText") ?? string.Empty,
                GetString(dimension, "error"),
                textMetrics,
                blockGeometry,
                explodedGeometry,
                GetString(dimension, "blockGeometryCoordinateFrame") ?? string.Empty,
                GetString(dimension, "explodedGeometryCoordinateFrame") ?? string.Empty));
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

    private static bool TryGetPlanarUnitDirection(
        NativeFragmentMetric fragment,
        out double x,
        out double y)
    {
        x = 0d;
        y = 0d;
        if (!fragment.DirectionX.HasValue
            || !fragment.DirectionY.HasValue
            || !fragment.DirectionZ.HasValue)
        {
            return false;
        }

        var rawX = fragment.DirectionX.Value;
        var rawY = fragment.DirectionY.Value;
        var rawZ = fragment.DirectionZ.Value;
        if (!double.IsFinite(rawX)
            || !double.IsFinite(rawY)
            || !double.IsFinite(rawZ)
            || Math.Abs(rawZ) > 1e-9)
        {
            return false;
        }

        var length = Math.Sqrt(rawX * rawX + rawY * rawY);
        if (!double.IsFinite(length) || length <= 1e-12)
            return false;

        x = rawX / length;
        y = rawY / length;
        return true;
    }

    private static int CountUniqueLineMatches(
        IReadOnlyList<SourceLineGeometry> sourceLines,
        IReadOnlyList<NativeBlockGeometry> nativeLines,
        double tolerance)
    {
        var usedNative = new bool[nativeLines.Count];
        var matched = 0;

        foreach (var source in sourceLines)
        {
            var matchIndex = -1;
            for (var index = 0; index < nativeLines.Count; index++)
            {
                if (usedNative[index])
                    continue;
                if (LinesEqual(source, nativeLines[index], tolerance))
                {
                    matchIndex = index;
                    break;
                }
            }

            if (matchIndex < 0)
                continue;

            usedNative[matchIndex] = true;
            matched++;
        }

        return matched;
    }

    private static bool LinesEqual(
        SourceLineGeometry source,
        NativeBlockGeometry native,
        double tolerance)
    {
        if (!source.StartX.HasValue
            || !source.StartY.HasValue
            || !source.EndX.HasValue
            || !source.EndY.HasValue
            || !native.StartX.HasValue
            || !native.StartY.HasValue
            || !native.EndX.HasValue
            || !native.EndY.HasValue)
        {
            return false;
        }

        static bool PointEqual(
            double ax,
            double ay,
            double bx,
            double by,
            double tolerance)
            => Math.Abs(ax - bx) <= tolerance
                && Math.Abs(ay - by) <= tolerance;

        var sameDirection =
            PointEqual(
                source.StartX.Value,
                source.StartY.Value,
                native.StartX.Value,
                native.StartY.Value,
                tolerance)
            && PointEqual(
                source.EndX.Value,
                source.EndY.Value,
                native.EndX.Value,
                native.EndY.Value,
                tolerance);
        var reverseDirection =
            PointEqual(
                source.StartX.Value,
                source.StartY.Value,
                native.EndX.Value,
                native.EndY.Value,
                tolerance)
            && PointEqual(
                source.EndX.Value,
                source.EndY.Value,
                native.StartX.Value,
                native.StartY.Value,
                tolerance);
        return sameDirection || reverseDirection;
    }

    private static IReadOnlyList<NativeBlockGeometry> ReadGeometryEvidence(
        JsonElement dimension,
        string propertyName)
    {
        if (!TryGetArray(dimension, propertyName, out var geometryArray))
            return [];

        var output = new List<NativeBlockGeometry>();
        foreach (var geometry in geometryArray.EnumerateArray())
        {
            output.Add(new NativeBlockGeometry(
                GetString(geometry, "entityType") ?? string.Empty,
                GetString(geometry, "entityHandle") ?? string.Empty,
                GetString(geometry, "geometryKind") ?? string.Empty,
                GetNullableDouble(geometry, "startX"),
                GetNullableDouble(geometry, "startY"),
                GetNullableDouble(geometry, "startZ"),
                GetNullableDouble(geometry, "endX"),
                GetNullableDouble(geometry, "endY"),
                GetNullableDouble(geometry, "endZ"),
                GetNullableDouble(geometry, "minX"),
                GetNullableDouble(geometry, "minY"),
                GetNullableDouble(geometry, "minZ"),
                GetNullableDouble(geometry, "maxX"),
                GetNullableDouble(geometry, "maxY"),
                GetNullableDouble(geometry, "maxZ"),
                GetString(geometry, "nestedBlockName") ?? string.Empty,
                GetNullableInt(geometry, "vertexCount") ?? 0));
        }

        return output;
    }

    private static void ValidateGeometryEvidence(
        IReadOnlyList<NativeBlockGeometry> geometryItems,
        string blockerPrefix,
        ICollection<string> blockers)
    {
        if (geometryItems.Count == 0)
        {
            blockers.Add(blockerPrefix + "-geometry-missing");
            return;
        }

        foreach (var geometry in geometryItems)
        {
            if (string.IsNullOrWhiteSpace(geometry.EntityType)
                || string.IsNullOrWhiteSpace(geometry.EntityHandle)
                || string.IsNullOrWhiteSpace(geometry.GeometryKind))
            {
                blockers.Add(blockerPrefix + "-geometry-identity-invalid");
                continue;
            }

            if (!HasValidExtents(geometry))
                blockers.Add(blockerPrefix + "-geometry-extents-invalid");

            if (string.Equals(geometry.GeometryKind, "line", StringComparison.Ordinal)
                && !HasValidLineGeometry(geometry))
            {
                blockers.Add(blockerPrefix + "-line-geometry-invalid");
            }

            if (string.Equals(geometry.GeometryKind, "polyline", StringComparison.Ordinal)
                && geometry.VertexCount < 2)
            {
                blockers.Add(blockerPrefix + "-polyline-geometry-invalid");
            }

            if (string.Equals(geometry.GeometryKind, "block-reference", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(geometry.NestedBlockName))
            {
                blockers.Add(blockerPrefix + "-reference-name-missing");
            }
        }
    }

    private static bool HasValidExtents(NativeBlockGeometry geometry)
        => geometry.MinX.HasValue
            && geometry.MinY.HasValue
            && geometry.MinZ.HasValue
            && geometry.MaxX.HasValue
            && geometry.MaxY.HasValue
            && geometry.MaxZ.HasValue
            && double.IsFinite(geometry.MinX.Value)
            && double.IsFinite(geometry.MinY.Value)
            && double.IsFinite(geometry.MinZ.Value)
            && double.IsFinite(geometry.MaxX.Value)
            && double.IsFinite(geometry.MaxY.Value)
            && double.IsFinite(geometry.MaxZ.Value)
            && geometry.MaxX.Value >= geometry.MinX.Value
            && geometry.MaxY.Value >= geometry.MinY.Value
            && geometry.MaxZ.Value >= geometry.MinZ.Value;

    private static bool HasValidLineGeometry(NativeBlockGeometry geometry)
    {
        if (!geometry.StartX.HasValue
            || !geometry.StartY.HasValue
            || !geometry.StartZ.HasValue
            || !geometry.EndX.HasValue
            || !geometry.EndY.HasValue
            || !geometry.EndZ.HasValue)
        {
            return false;
        }

        var values = new[]
        {
            geometry.StartX.Value,
            geometry.StartY.Value,
            geometry.StartZ.Value,
            geometry.EndX.Value,
            geometry.EndY.Value,
            geometry.EndZ.Value
        };
        if (values.Any(value => !double.IsFinite(value)))
            return false;

        var dx = geometry.EndX.Value - geometry.StartX.Value;
        var dy = geometry.EndY.Value - geometry.StartY.Value;
        var dz = geometry.EndZ.Value - geometry.StartZ.Value;
        return dx * dx + dy * dy + dz * dz > 1e-18;
    }

    private static bool AlmostEqual(double? actual, double expected)
        => actual.HasValue
            && double.IsFinite(actual.Value)
            && Math.Abs(actual.Value - expected) <= 1e-9;

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

    private static int? GetNullableInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
                ? parsed
                : null;
    }

    private sealed record SourceLineGeometry(
        string Role,
        double? StartX,
        double? StartY,
        double? EndX,
        double? EndY);

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
        double? SourceVisibleHeightMm,
        double? SourceGlyphInkWidthMm,
        double? SourceGlyphInkHeightMm,
        double? SourceHeightMm,
        double SourceRotationDegrees,
        string SourceCoordinateFrame,
        IReadOnlyList<SourceLineGeometry> SourceLineGeometry)
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
                null,
                null,
                null,
                null,
                double.NaN,
                string.Empty,
                []);
    }

    private sealed record NativeBlockGeometry(
        string EntityType,
        string EntityHandle,
        string GeometryKind,
        double? StartX,
        double? StartY,
        double? StartZ,
        double? EndX,
        double? EndY,
        double? EndZ,
        double? MinX,
        double? MinY,
        double? MinZ,
        double? MaxX,
        double? MaxY,
        double? MaxZ,
        string NestedBlockName,
        int VertexCount);

    private sealed record NativeDimensionEvidence(
        string CandidateId,
        string CandidateRole,
        string DimensionText,
        string? Error,
        IReadOnlyList<NativeTextMetric> TextMetrics,
        IReadOnlyList<NativeBlockGeometry> BlockGeometry,
        IReadOnlyList<NativeBlockGeometry> ExplodedGeometry,
        string BlockGeometryCoordinateFrame,
        string ExplodedGeometryCoordinateFrame)
    {
        public static NativeDimensionEvidence Empty(string candidateId)
            => new(
                candidateId,
                string.Empty,
                string.Empty,
                null,
                [],
                [],
                [],
                string.Empty,
                string.Empty);
    }

    private sealed record NativeFragmentMetric(
        string Text,
        string TrueTypeFont,
        string ShxFont,
        double? ExtentWidth,
        double? ExtentHeight,
        double? CapsHeight,
        double? TrackingFactor,
        double? WidthFactor,
        double? ObliqueAngle,
        double? LocationX,
        double? LocationY,
        double? LocationZ,
        double? DirectionX,
        double? DirectionY,
        double? DirectionZ,
        bool Bold,
        bool Italic,
        bool StackTop,
        bool StackBottom,
        bool Underlined,
        bool Overlined,
        bool Strikethrough);

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
        double? EntityWidthFactor,
        bool BackgroundFill,
        bool UseBackgroundColor,
        double? BackgroundScaleFactor,
        bool ShowBorders,
        string Attachment,
        IReadOnlyList<NativeFragmentMetric> Fragments)
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
            null,
            false,
            false,
            null,
            false,
            string.Empty,
            []);
    }
}
