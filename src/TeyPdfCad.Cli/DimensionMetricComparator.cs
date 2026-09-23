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
    bool LineAppearanceEvidenceUsable,
    bool CrossSnapshotLineAppearanceEvidenceUsable,
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

        if (!string.Equals(nativeSchemaVersion, "9", StringComparison.Ordinal))
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
                else if (!string.Equals(
                    metric.Attachment,
                    "MiddleCenter",
                    StringComparison.Ordinal))
                {
                    blockers.Add("native-mtext-attachment-not-middle-center");
                }

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

        var sourceNativeMeasurementUsable =
            IsPositiveFinite(source.SourceNativeMeasurementMm);
        var nativeMeasurementUsable =
            IsPositiveFinite(native.Measurement);
        var nativeMeasurementMatches = false;
        if (!sourceNativeMeasurementUsable)
        {
            blockers.Add("source-native-measurement-evidence-invalid");
        }
        else if (!nativeMeasurementUsable)
        {
            blockers.Add("native-dimension-measurement-invalid");
        }
        else
        {
            nativeMeasurementMatches =
                Math.Abs(
                    native.Measurement!.Value
                    - source.SourceNativeMeasurementMm!.Value)
                <= NumericalWidthToleranceMm;
            if (!nativeMeasurementMatches)
                blockers.Add("source-native-measurement-mismatch");
        }

        NativeTextMetric explodedTextMetric = NativeTextMetric.Empty;
        if (natives.Count == 1)
        {
            if (native.ExplodedTextMetrics.Count != 1)
            {
                blockers.Add("cross-snapshot-exploded-text-count-not-one");
            }
            else
            {
                explodedTextMetric = native.ExplodedTextMetrics[0];
                if (!string.Equals(
                        metric.EntityType,
                        "MText",
                        StringComparison.Ordinal)
                    || !string.Equals(
                        explodedTextMetric.EntityType,
                        "MText",
                        StringComparison.Ordinal))
                {
                    blockers.Add("cross-snapshot-text-type-not-mtext");
                }
                if (!string.Equals(
                        explodedTextMetric.MetricKind,
                        "mtext-actual-bounds-exploded-wcs",
                        StringComparison.Ordinal))
                {
                    blockers.Add("cross-snapshot-exploded-metric-kind-unsupported");
                }
                if (!string.Equals(
                        metric.Text,
                        explodedTextMetric.Text,
                        StringComparison.Ordinal))
                {
                    blockers.Add("cross-snapshot-text-mismatch");
                }
                if (!string.Equals(
                        metric.FontSha256,
                        explodedTextMetric.FontSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add("cross-snapshot-font-sha256-mismatch");
                }
                if (!string.Equals(
                        metric.FontFile,
                        explodedTextMetric.FontFile,
                        StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add("cross-snapshot-font-file-mismatch");
                }
                if (!ScalarEqual(metric.Width, explodedTextMetric.Width, 1e-6)
                    || !ScalarEqual(metric.Height, explodedTextMetric.Height, 1e-6))
                {
                    blockers.Add("cross-snapshot-text-extents-mismatch");
                }
                if (!ScalarEqual(
                        metric.NominalTextHeight,
                        explodedTextMetric.NominalTextHeight,
                        1e-6)
                    || !ScalarEqual(
                        metric.TextStyleWidthFactor,
                        explodedTextMetric.TextStyleWidthFactor,
                        1e-9)
                    || !ScalarEqual(
                        metric.EntityWidthFactor,
                        explodedTextMetric.EntityWidthFactor,
                        1e-9))
                {
                    blockers.Add("cross-snapshot-text-style-mismatch");
                }
                if (metric.BackgroundFill != explodedTextMetric.BackgroundFill
                    || metric.UseBackgroundColor != explodedTextMetric.UseBackgroundColor
                    || metric.ShowBorders != explodedTextMetric.ShowBorders
                    || !string.Equals(
                        metric.Attachment,
                        explodedTextMetric.Attachment,
                        StringComparison.Ordinal))
                {
                    blockers.Add("cross-snapshot-text-decoration-mismatch");
                }

                if (metric.Fragments.Count != 1
                    || explodedTextMetric.Fragments.Count != 1)
                {
                    blockers.Add("cross-snapshot-fragment-count-not-one");
                }
                else
                {
                    var blockFragment = metric.Fragments[0];
                    var explodedFragment = explodedTextMetric.Fragments[0];
                    if (!string.Equals(
                            blockFragment.Text,
                            explodedFragment.Text,
                            StringComparison.Ordinal)
                        || !string.Equals(
                            blockFragment.TrueTypeFont,
                            explodedFragment.TrueTypeFont,
                            StringComparison.Ordinal)
                        || !string.Equals(
                            blockFragment.ShxFont,
                            explodedFragment.ShxFont,
                            StringComparison.Ordinal))
                    {
                        blockers.Add("cross-snapshot-fragment-identity-mismatch");
                    }
                    if (!ScalarEqual(
                            blockFragment.ExtentWidth,
                            explodedFragment.ExtentWidth,
                            1e-6)
                        || !ScalarEqual(
                            blockFragment.ExtentHeight,
                            explodedFragment.ExtentHeight,
                            1e-6)
                        || !ScalarEqual(
                            blockFragment.CapsHeight,
                            explodedFragment.CapsHeight,
                            1e-6)
                        || !ScalarEqual(
                            blockFragment.TrackingFactor,
                            explodedFragment.TrackingFactor,
                            1e-9)
                        || !ScalarEqual(
                            blockFragment.WidthFactor,
                            explodedFragment.WidthFactor,
                            1e-9)
                        || !ScalarEqual(
                            blockFragment.ObliqueAngle,
                            explodedFragment.ObliqueAngle,
                            1e-9))
                    {
                        blockers.Add("cross-snapshot-fragment-metrics-mismatch");
                    }
                    if (blockFragment.Bold != explodedFragment.Bold
                        || blockFragment.Italic != explodedFragment.Italic
                        || blockFragment.StackTop != explodedFragment.StackTop
                        || blockFragment.StackBottom != explodedFragment.StackBottom
                        || blockFragment.Underlined != explodedFragment.Underlined
                        || blockFragment.Overlined != explodedFragment.Overlined
                        || blockFragment.Strikethrough != explodedFragment.Strikethrough)
                    {
                        blockers.Add("cross-snapshot-fragment-formatting-mismatch");
                    }
                }
            }
        }

        // Source text rotation is expressed in drawing WCS. The regenerated
        // anonymous *D block fragment is expressed in dimension-block MCS and
        // cannot be compared directly without the dimension block transform.
        // Dimension.Explode() gives us a separate drawing-WCS text snapshot, so
        // bind source baseline evidence to that channel only.
        if (native.ExplodedTextMetrics.Count == 1
            && explodedTextMetric.Fragments.Count == 1
            && double.IsFinite(source.SourceRotationDegrees)
            && string.Equals(
                native.ExplodedGeometryCoordinateFrame,
                "drawing-wcs",
                StringComparison.Ordinal))
        {
            if (TryGetPlanarUnitDirection(
                    explodedTextMetric.Fragments[0],
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
            else
            {
                blockers.Add("native-exploded-fragment-direction-not-planar");
            }
        }

        var sourceVisualCenterUsable =
            source.SourceVisualCenterX.HasValue
            && source.SourceVisualCenterY.HasValue
            && double.IsFinite(source.SourceVisualCenterX.Value)
            && double.IsFinite(source.SourceVisualCenterY.Value);
        if (!sourceVisualCenterUsable)
            blockers.Add("source-text-visual-center-invalid");

        var explodedTextCenterMatches = false;
        if (native.ExplodedTextMetrics.Count == 1)
        {
            var nativeCenterUsable =
                explodedTextMetric.PositionX.HasValue
                && explodedTextMetric.PositionY.HasValue
                && explodedTextMetric.PositionZ.HasValue
                && double.IsFinite(explodedTextMetric.PositionX.Value)
                && double.IsFinite(explodedTextMetric.PositionY.Value)
                && double.IsFinite(explodedTextMetric.PositionZ.Value)
                && Math.Abs(explodedTextMetric.PositionZ.Value)
                    <= NumericalWidthToleranceMm;

            if (!nativeCenterUsable)
            {
                blockers.Add("native-exploded-text-position-invalid");
            }
            else if (!string.Equals(
                explodedTextMetric.Attachment,
                "MiddleCenter",
                StringComparison.Ordinal))
            {
                blockers.Add("native-mtext-attachment-not-middle-center");
            }
            else if (sourceVisualCenterUsable)
            {
                explodedTextCenterMatches =
                    Math.Abs(
                        explodedTextMetric.PositionX!.Value
                        - source.SourceVisualCenterX!.Value)
                        <= NumericalWidthToleranceMm
                    && Math.Abs(
                        explodedTextMetric.PositionY!.Value
                        - source.SourceVisualCenterY!.Value)
                        <= NumericalWidthToleranceMm;

                if (!explodedTextCenterMatches)
                    blockers.Add("source-native-text-center-mismatch");
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

        var sourceExtensionLines = source.SourceLineGeometry
            .Where(item => string.Equals(
                item.Role,
                "extension-line",
                StringComparison.Ordinal))
            .ToArray();
        if (sourceExtensionLines.Length != 2)
            blockers.Add("source-extension-line-count-not-two");

        var sourceArrowLines = source.SourceLineGeometry
            .Where(item => string.Equals(
                item.Role,
                "arrow-geometry",
                StringComparison.Ordinal))
            .ToArray();
        if (sourceArrowLines.Length != 2)
            blockers.Add("source-arrow-line-count-not-two");

        var structuralMatch = MatchSourceLineworkGlobally(
            sourceDimensionLines,
            sourceExtensionLines,
            sourceArrowLines,
            explodedLines,
            NumericalWidthToleranceMm);
        var matchedSourceDimensionLineCount = structuralMatch.DimensionLineMatches;
        var matchedSourceExtensionLineCount = structuralMatch.ExtensionLineMatches;
        var matchedSourceArrowLineCount = structuralMatch.ArrowLineMatches;
        var sourceStructuralLineCount =
            sourceDimensionLines.Length
            + sourceExtensionLines.Length
            + sourceArrowLines.Length;

        if (sourceDimensionLines.Length > 0
            && matchedSourceDimensionLineCount != sourceDimensionLines.Length)
        {
            blockers.Add("source-dimension-line-unmatched");
        }
        if (sourceExtensionLines.Length > 0
            && matchedSourceExtensionLineCount != sourceExtensionLines.Length)
        {
            blockers.Add("source-extension-line-unmatched");
        }
        if (sourceArrowLines.Length > 0
            && matchedSourceArrowLineCount != sourceArrowLines.Length)
        {
            blockers.Add("source-arrow-line-unmatched");
        }

        if (structuralMatch.TotalMatches != sourceStructuralLineCount)
            blockers.Add("source-structural-line-global-one-to-one-mismatch");

        // The first safe subset is line-only. Matching all expected source
        // strokes is insufficient if the native DIMENSION explodes to extra
        // non-text geometry: that would still alter the visible drawing after
        // source suppression. Require exact structural coverage fail-closed.
        if (native.ExplodedGeometry.Count != sourceStructuralLineCount)
        {
            blockers.Add(
                "native-exploded-geometry-count-does-not-match-source-structure");
        }
        if (explodedLines.Length != native.ExplodedGeometry.Count)
            blockers.Add("native-exploded-non-line-geometry-present");
        if (explodedLines.Length != sourceStructuralLineCount)
            blockers.Add("native-exploded-line-count-does-not-match-source-structure");

        if (explodedLines.Any(line =>
                !IsLineInSourcePlane(line, NumericalWidthToleranceMm)))
        {
            blockers.Add("native-exploded-line-not-in-source-plane");
        }

        if (native.BlockGeometry
            .Where(item => string.Equals(
                item.GeometryKind,
                "line",
                StringComparison.Ordinal))
            .Any(line => !HasUnambiguousRawLineAppearance(line)))
        {
            blockers.Add(
                "native-block-line-appearance-inherited-or-unresolved");
        }

        if (explodedLines.Any(line =>
                !HasUnambiguousRawLineAppearance(line)))
        {
            blockers.Add(
                "native-exploded-line-appearance-inherited-or-unresolved");
        }

        var sourceLineAppearanceComplete =
            source.SourceLineGeometry.Count == sourceStructuralLineCount
            && source.SourceLineGeometry.All(HasUsableSourceLineAppearance);
        if (!sourceLineAppearanceComplete)
            blockers.Add("source-line-appearance-evidence-incomplete");

        var lineAppearanceEvidenceUsable = false;
        if (sourceLineAppearanceComplete
            && explodedLines.Length == sourceStructuralLineCount
            && explodedLines.All(HasUnambiguousRawLineAppearance))
        {
            var appearanceMatches = MatchSourceLinesGlobally(
                source.SourceLineGeometry,
                explodedLines,
                NumericalWidthToleranceMm,
                LineAppearanceEqual);
            lineAppearanceEvidenceUsable =
                appearanceMatches.Count(value => value)
                    == source.SourceLineGeometry.Count;

            if (!lineAppearanceEvidenceUsable)
                blockers.Add("source-native-line-appearance-mismatch");
        }

        var blockLines = native.BlockGeometry
            .Where(item => string.Equals(
                item.GeometryKind,
                "line",
                StringComparison.Ordinal))
            .ToArray();
        var crossSnapshotStructuralTransformConsistent = false;
        var crossSnapshotTextFragmentTransformConsistent = false;
        var crossSnapshotLineAppearanceEvidenceUsable = false;
        if (blockLines.Length != native.BlockGeometry.Count
            || native.BlockGeometry.Count != explodedLines.Length)
        {
            blockers.Add("cross-snapshot-structural-transform-mismatch");
        }
        else if (!TryBuildCrossSnapshotTransform(
                     metric,
                     explodedTextMetric,
                     NumericalWidthToleranceMm,
                     out var crossSnapshotTransform))
        {
            blockers.Add("cross-snapshot-transform-evidence-invalid");
        }
        else
        {
            crossSnapshotTextFragmentTransformConsistent =
                FragmentLocationMatchesTransform(
                    metric,
                    explodedTextMetric,
                    crossSnapshotTransform,
                    NumericalWidthToleranceMm);
            if (!crossSnapshotTextFragmentTransformConsistent)
            {
                blockers.Add(
                    "cross-snapshot-text-fragment-transform-mismatch");
            }

            var transformedBlockLines = new List<SourceLineGeometry>(
                blockLines.Length);
            var transformUsable = true;
            foreach (var blockLine in blockLines)
            {
                if (!TryTransformBlockLine(
                        blockLine,
                        crossSnapshotTransform,
                        NumericalWidthToleranceMm,
                        out var transformed))
                {
                    transformUsable = false;
                    break;
                }

                transformedBlockLines.Add(transformed);
            }

            if (!transformUsable)
            {
                blockers.Add("cross-snapshot-transform-evidence-invalid");
            }
            else
            {
                var matchedBlockLines = MatchSourceLinesGlobally(
                    transformedBlockLines,
                    explodedLines,
                    NumericalWidthToleranceMm);
                crossSnapshotStructuralTransformConsistent =
                    matchedBlockLines.Count(value => value)
                        == transformedBlockLines.Count
                    && transformedBlockLines.Count == explodedLines.Length;

                if (!crossSnapshotStructuralTransformConsistent)
                {
                    blockers.Add(
                        "cross-snapshot-structural-transform-mismatch");
                }

                if (crossSnapshotStructuralTransformConsistent
                    && blockLines.All(HasUnambiguousRawLineAppearance)
                    && explodedLines.All(HasUnambiguousRawLineAppearance))
                {
                    var appearanceMatches =
                        MatchCrossSnapshotLineAppearanceGlobally(
                            blockLines,
                            transformedBlockLines,
                            explodedLines,
                            NumericalWidthToleranceMm);
                    crossSnapshotLineAppearanceEvidenceUsable =
                        appearanceMatches.Count(value => value)
                            == blockLines.Length;
                    if (!crossSnapshotLineAppearanceEvidenceUsable)
                    {
                        blockers.Add(
                            "cross-snapshot-line-appearance-mismatch");
                    }
                }
            }
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
        blockers.Add("cross-snapshot-transform-equivalence-not-yet-authorized");

        var distinctBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var measurementsUsable = globalMeasurementsUsable
            && sources.Count == 1
            && natives.Count == 1
            && native.TextMetrics.Count == 1
            && native.ExplodedTextMetrics.Count == 1
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
            && sourceNativeMeasurementUsable
            && nativeMeasurementUsable
            && nativeMeasurementMatches
            && crossSnapshotStructuralTransformConsistent
            && crossSnapshotTextFragmentTransformConsistent
            && baselineDirectionDot.HasValue
            && baselineDirectionDot.Value > 0.999999
            && sourceVisualCenterUsable
            && explodedTextCenterMatches
            && string.Equals(
                metric.Attachment,
                "MiddleCenter",
                StringComparison.Ordinal)
            && string.Equals(
                explodedTextMetric.Attachment,
                "MiddleCenter",
                StringComparison.Ordinal)
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
                blocker.StartsWith("native-exploded-", StringComparison.Ordinal))
            && !distinctBlockers.Any(blocker =>
                blocker.StartsWith("cross-snapshot-", StringComparison.Ordinal)
                && !string.Equals(
                    blocker,
                    "cross-snapshot-transform-equivalence-not-yet-authorized",
                    StringComparison.Ordinal));

        return new DimensionMetricComparisonCandidate(
            candidateId,
            measurementsUsable,
            lineAppearanceEvidenceUsable,
            crossSnapshotLineAppearanceEvidenceUsable,
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
                            GetNullableDouble(line, "endY"),
                            GetNullableInt(line, "rgbColor"),
                            GetNullableDouble(line, "strokeWidthMm"),
                            GetNullableDoubleArray(line, "dashPatternMm")));
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
                    GetNullableDouble(item, "sourceNativeMeasurementMm"),
                    GetNullableDouble(item, "sourceRotationDegrees") ?? double.NaN,
                    GetNullableDouble(item, "sourceVisualCenterX"),
                    GetNullableDouble(item, "sourceVisualCenterY"),
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
            var textMetrics = ReadTextMetrics(
                dimension,
                "textMetrics");
            var explodedTextMetrics = ReadTextMetrics(
                dimension,
                "explodedTextMetrics");

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
                        GetNullableInt(geometry, "vertexCount") ?? 0,
                        GetString(geometry, "colorMethod") ?? string.Empty,
                        GetNullableInt(geometry, "rgbColor"),
                        GetString(geometry, "lineWeightMode") ?? string.Empty,
                        GetNullableInt(geometry, "lineWeightHundredthsMm"),
                        GetString(geometry, "linetype") ?? string.Empty,
                        GetString(geometry, "layer") ?? string.Empty));
                }
            }

            var explodedGeometry = ReadGeometryEvidence(
                dimension,
                "explodedGeometry");

            output.Add(new NativeDimensionEvidence(
                GetString(dimension, "candidateId") ?? string.Empty,
                GetString(dimension, "candidateRole") ?? string.Empty,
                GetString(dimension, "dimensionText") ?? string.Empty,
                GetNullableDouble(dimension, "measurement"),
                GetString(dimension, "error"),
                textMetrics,
                blockGeometry,
                explodedGeometry,
                explodedTextMetrics,
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

    private static StructuralLineMatchResult MatchSourceLineworkGlobally(
        IReadOnlyList<SourceLineGeometry> dimensionLines,
        IReadOnlyList<SourceLineGeometry> extensionLines,
        IReadOnlyList<SourceLineGeometry> arrowLines,
        IReadOnlyList<NativeBlockGeometry> nativeLines,
        double tolerance)
    {
        var sources = dimensionLines
            .Select(line => new StructuralSourceLine(StructuralLineRole.Dimension, line))
            .Concat(extensionLines.Select(line =>
                new StructuralSourceLine(StructuralLineRole.Extension, line)))
            .Concat(arrowLines.Select(line =>
                new StructuralSourceLine(StructuralLineRole.Arrow, line)))
            .ToArray();
        var matchedSources = MatchSourceLinesGlobally(
            sources.Select(source => source.Geometry).ToArray(),
            nativeLines,
            tolerance);

        var dimensionMatches = 0;
        var extensionMatches = 0;
        var arrowMatches = 0;
        for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            if (!matchedSources[sourceIndex])
                continue;

            switch (sources[sourceIndex].Role)
            {
                case StructuralLineRole.Dimension:
                    dimensionMatches++;
                    break;
                case StructuralLineRole.Extension:
                    extensionMatches++;
                    break;
                case StructuralLineRole.Arrow:
                    arrowMatches++;
                    break;
            }
        }

        return new StructuralLineMatchResult(
            dimensionMatches,
            extensionMatches,
            arrowMatches,
            matchedSources.Count(value => value));
    }

    private static bool[] MatchSourceLinesGlobally(
        IReadOnlyList<SourceLineGeometry> sourceLines,
        IReadOnlyList<NativeBlockGeometry> nativeLines,
        double tolerance,
        Func<SourceLineGeometry, NativeBlockGeometry, bool>? additionalMatch = null)
    {
        // Maximum-cardinality bipartite matching prevents one native entity
        // from satisfying more than one source/transformed line.
        var nativeToSource = Enumerable.Repeat(-1, nativeLines.Count).ToArray();

        bool TryAssign(int sourceIndex, bool[] visitedNative)
        {
            for (var nativeIndex = 0; nativeIndex < nativeLines.Count; nativeIndex++)
            {
                if (visitedNative[nativeIndex]
                    || !LinesEqual(
                        sourceLines[sourceIndex],
                        nativeLines[nativeIndex],
                        tolerance)
                    || (additionalMatch is not null
                        && !additionalMatch(
                            sourceLines[sourceIndex],
                            nativeLines[nativeIndex])))
                {
                    continue;
                }

                visitedNative[nativeIndex] = true;
                if (nativeToSource[nativeIndex] < 0
                    || TryAssign(nativeToSource[nativeIndex], visitedNative))
                {
                    nativeToSource[nativeIndex] = sourceIndex;
                    return true;
                }
            }

            return false;
        }

        for (var sourceIndex = 0; sourceIndex < sourceLines.Count; sourceIndex++)
            TryAssign(sourceIndex, new bool[nativeLines.Count]);

        var matchedSources = new bool[sourceLines.Count];
        foreach (var sourceIndex in nativeToSource)
        {
            if (sourceIndex >= 0)
                matchedSources[sourceIndex] = true;
        }

        return matchedSources;
    }

    private enum StructuralLineRole
    {
        Dimension,
        Extension,
        Arrow
    }

    private readonly record struct StructuralSourceLine(
        StructuralLineRole Role,
        SourceLineGeometry Geometry);

    private readonly record struct StructuralLineMatchResult(
        int DimensionLineMatches,
        int ExtensionLineMatches,
        int ArrowLineMatches,
        int TotalMatches);

    private static bool TryBuildCrossSnapshotTransform(
        NativeTextMetric blockText,
        NativeTextMetric explodedText,
        double tolerance,
        out RigidTransform2D transform)
    {
        transform = default;
        if (!blockText.PositionX.HasValue
            || !blockText.PositionY.HasValue
            || !blockText.PositionZ.HasValue
            || !explodedText.PositionX.HasValue
            || !explodedText.PositionY.HasValue
            || !explodedText.PositionZ.HasValue
            || !double.IsFinite(blockText.PositionX.Value)
            || !double.IsFinite(blockText.PositionY.Value)
            || !double.IsFinite(blockText.PositionZ.Value)
            || !double.IsFinite(explodedText.PositionX.Value)
            || !double.IsFinite(explodedText.PositionY.Value)
            || !double.IsFinite(explodedText.PositionZ.Value)
            || Math.Abs(blockText.PositionZ.Value) > tolerance
            || Math.Abs(explodedText.PositionZ.Value) > tolerance
            || blockText.Fragments.Count != 1
            || explodedText.Fragments.Count != 1
            || !TryGetPlanarUnitDirection(
                blockText.Fragments[0],
                out var blockX,
                out var blockY)
            || !TryGetPlanarUnitDirection(
                explodedText.Fragments[0],
                out var explodedX,
                out var explodedY))
        {
            return false;
        }

        var cosine = blockX * explodedX + blockY * explodedY;
        var sine = blockX * explodedY - blockY * explodedX;
        if (!double.IsFinite(cosine)
            || !double.IsFinite(sine)
            || Math.Abs(cosine * cosine + sine * sine - 1d) > 1e-9)
        {
            return false;
        }

        var rotatedBlockX =
            cosine * blockText.PositionX.Value
            - sine * blockText.PositionY.Value;
        var rotatedBlockY =
            sine * blockText.PositionX.Value
            + cosine * blockText.PositionY.Value;
        var translateX = explodedText.PositionX.Value - rotatedBlockX;
        var translateY = explodedText.PositionY.Value - rotatedBlockY;
        if (!double.IsFinite(translateX) || !double.IsFinite(translateY))
            return false;

        transform = new RigidTransform2D(
            cosine,
            sine,
            translateX,
            translateY);
        return true;
    }

    private static bool FragmentLocationMatchesTransform(
        NativeTextMetric blockText,
        NativeTextMetric explodedText,
        RigidTransform2D transform,
        double tolerance)
    {
        if (blockText.Fragments.Count != 1
            || explodedText.Fragments.Count != 1)
        {
            return false;
        }

        var block = blockText.Fragments[0];
        var exploded = explodedText.Fragments[0];
        if (!block.LocationX.HasValue
            || !block.LocationY.HasValue
            || !block.LocationZ.HasValue
            || !exploded.LocationX.HasValue
            || !exploded.LocationY.HasValue
            || !exploded.LocationZ.HasValue
            || !double.IsFinite(block.LocationX.Value)
            || !double.IsFinite(block.LocationY.Value)
            || !double.IsFinite(block.LocationZ.Value)
            || !double.IsFinite(exploded.LocationX.Value)
            || !double.IsFinite(exploded.LocationY.Value)
            || !double.IsFinite(exploded.LocationZ.Value)
            || Math.Abs(block.LocationZ.Value) > tolerance
            || Math.Abs(exploded.LocationZ.Value) > tolerance)
        {
            return false;
        }

        var transformed = transform.Apply(
            block.LocationX.Value,
            block.LocationY.Value);
        return Math.Abs(transformed.X - exploded.LocationX.Value) <= tolerance
            && Math.Abs(transformed.Y - exploded.LocationY.Value) <= tolerance;
    }

    private static bool TryTransformBlockLine(
        NativeBlockGeometry blockLine,
        RigidTransform2D transform,
        double tolerance,
        out SourceLineGeometry transformed)
    {
        transformed = new SourceLineGeometry(
            "cross-snapshot",
            null,
            null,
            null,
            null);
        if (!string.Equals(
                blockLine.GeometryKind,
                "line",
                StringComparison.Ordinal)
            || !IsLineInSourcePlane(blockLine, tolerance)
            || !blockLine.StartX.HasValue
            || !blockLine.StartY.HasValue
            || !blockLine.EndX.HasValue
            || !blockLine.EndY.HasValue)
        {
            return false;
        }

        var start = transform.Apply(
            blockLine.StartX.Value,
            blockLine.StartY.Value);
        var end = transform.Apply(
            blockLine.EndX.Value,
            blockLine.EndY.Value);
        transformed = new SourceLineGeometry(
            "cross-snapshot",
            start.X,
            start.Y,
            end.X,
            end.Y);
        return true;
    }

    private readonly record struct RigidTransform2D(
        double Cosine,
        double Sine,
        double TranslateX,
        double TranslateY)
    {
        public (double X, double Y) Apply(double x, double y)
            => (
                Cosine * x - Sine * y + TranslateX,
                Sine * x + Cosine * y + TranslateY);
    }

    private static bool[] MatchCrossSnapshotLineAppearanceGlobally(
        IReadOnlyList<NativeBlockGeometry> blockLines,
        IReadOnlyList<SourceLineGeometry> transformedBlockLines,
        IReadOnlyList<NativeBlockGeometry> explodedLines,
        double tolerance)
    {
        if (blockLines.Count != transformedBlockLines.Count)
            return new bool[blockLines.Count];

        var explodedToBlock = Enumerable.Repeat(-1, explodedLines.Count).ToArray();

        bool TryAssign(int blockIndex, bool[] visitedExploded)
        {
            for (var explodedIndex = 0;
                 explodedIndex < explodedLines.Count;
                 explodedIndex++)
            {
                if (visitedExploded[explodedIndex]
                    || !LinesEqual(
                        transformedBlockLines[blockIndex],
                        explodedLines[explodedIndex],
                        tolerance)
                    || !RawLineAppearanceEqual(
                        blockLines[blockIndex],
                        explodedLines[explodedIndex]))
                {
                    continue;
                }

                visitedExploded[explodedIndex] = true;
                if (explodedToBlock[explodedIndex] < 0
                    || TryAssign(
                        explodedToBlock[explodedIndex],
                        visitedExploded))
                {
                    explodedToBlock[explodedIndex] = blockIndex;
                    return true;
                }
            }

            return false;
        }

        for (var blockIndex = 0; blockIndex < blockLines.Count; blockIndex++)
            TryAssign(blockIndex, new bool[explodedLines.Count]);

        var matchedBlocks = new bool[blockLines.Count];
        foreach (var blockIndex in explodedToBlock)
        {
            if (blockIndex >= 0)
                matchedBlocks[blockIndex] = true;
        }

        return matchedBlocks;
    }

    private static bool RawLineAppearanceEqual(
        NativeBlockGeometry first,
        NativeBlockGeometry second)
        => HasUnambiguousRawLineAppearance(first)
            && HasUnambiguousRawLineAppearance(second)
            && first.RgbColor == second.RgbColor
            && first.LineWeightHundredthsMm
                == second.LineWeightHundredthsMm
            && string.Equals(
                first.Linetype,
                second.Linetype,
                StringComparison.OrdinalIgnoreCase);

    private static bool HasUsableSourceLineAppearance(
        SourceLineGeometry line)
    {
        if (!line.RgbColor.HasValue
            || line.RgbColor.Value < 0
            || line.RgbColor.Value > 0xFFFFFF
            || !line.StrokeWidthMm.HasValue
            || !double.IsFinite(line.StrokeWidthMm.Value)
            || line.StrokeWidthMm.Value <= 0d
            || line.DashPatternMm is null
            || line.DashPatternMm.Any(value =>
                !double.IsFinite(value) || value < 0d))
        {
            return false;
        }

        var hundredths = line.StrokeWidthMm.Value * 100d;
        return Math.Abs(hundredths - Math.Round(hundredths)) <= 1e-9;
    }

    private static bool LineAppearanceEqual(
        SourceLineGeometry source,
        NativeBlockGeometry native)
    {
        if (!HasUsableSourceLineAppearance(source)
            || !HasUnambiguousRawLineAppearance(native))
        {
            return false;
        }

        var expectedLineWeight =
            (int)Math.Round(source.StrokeWidthMm!.Value * 100d);
        if (native.RgbColor != source.RgbColor
            || native.LineWeightHundredthsMm != expectedLineWeight)
        {
            return false;
        }

        var expectedLinetype = ExpectedLinetypeName(
            source.DashPatternMm!);
        return string.Equals(
            native.Linetype,
            expectedLinetype,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ExpectedLinetypeName(
        IReadOnlyList<double> dashPatternMm)
    {
        if (dashPatternMm.Count == 0)
            return "CONTINUOUS";

        var normalized = dashPatternMm
            .Select(Math.Abs)
            .ToArray();
        if (normalized.Length % 2 != 0)
            normalized = [.. normalized, .. normalized];

        var token = string.Join(
            "_",
            normalized.Select(value =>
                value.ToString(
                    "0.######",
                    CultureInfo.InvariantCulture)));
        return "PDF_DASH_MM_" + token.Replace('.', '_');
    }

    private static bool HasUnambiguousRawLineAppearance(
        NativeBlockGeometry line)
    {
        var colorExplicit =
            (string.Equals(
                line.ColorMethod,
                "ByColor",
                StringComparison.Ordinal)
             || string.Equals(
                 line.ColorMethod,
                 "ByAci",
                 StringComparison.Ordinal))
            && line.RgbColor.HasValue;

        var lineWeightExplicit =
            line.LineWeightHundredthsMm.HasValue
            && line.LineWeightHundredthsMm.Value >= 0
            && !string.IsNullOrWhiteSpace(line.LineWeightMode)
            && !line.LineWeightMode.Contains(
                "ByLayer",
                StringComparison.OrdinalIgnoreCase)
            && !line.LineWeightMode.Contains(
                "ByBlock",
                StringComparison.OrdinalIgnoreCase)
            && !line.LineWeightMode.Contains(
                "Default",
                StringComparison.OrdinalIgnoreCase);

        var linetypeExplicit =
            !string.IsNullOrWhiteSpace(line.Linetype)
            && !string.Equals(
                line.Linetype,
                "ByLayer",
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                line.Linetype,
                "ByBlock",
                StringComparison.OrdinalIgnoreCase);

        return colorExplicit
            && lineWeightExplicit
            && linetypeExplicit
            && !string.IsNullOrWhiteSpace(line.Layer);
    }

    private static bool IsLineInSourcePlane(
        NativeBlockGeometry line,
        double tolerance)
        => line.StartZ.HasValue
            && line.EndZ.HasValue
            && line.MinZ.HasValue
            && line.MaxZ.HasValue
            && double.IsFinite(line.StartZ.Value)
            && double.IsFinite(line.EndZ.Value)
            && double.IsFinite(line.MinZ.Value)
            && double.IsFinite(line.MaxZ.Value)
            && Math.Abs(line.StartZ.Value) <= tolerance
            && Math.Abs(line.EndZ.Value) <= tolerance
            && Math.Abs(line.MinZ.Value) <= tolerance
            && Math.Abs(line.MaxZ.Value) <= tolerance;

    private static bool LinesEqual(
        SourceLineGeometry source,
        NativeBlockGeometry native,
        double tolerance)
    {
        if (!IsLineInSourcePlane(native, tolerance))
            return false;
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

    private static IReadOnlyList<NativeTextMetric> ReadTextMetrics(
        JsonElement dimension,
        string propertyName)
    {
        if (!TryGetArray(dimension, propertyName, out var metrics))
            return [];

        var output = new List<NativeTextMetric>();
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

            output.Add(new NativeTextMetric(
                GetString(metric, "entityType") ?? string.Empty,
                GetString(metric, "entityHandle") ?? string.Empty,
                GetString(metric, "text") ?? string.Empty,
                GetString(metric, "metricKind") ?? string.Empty,
                GetNullableDouble(metric, "width"),
                GetNullableDouble(metric, "height"),
                GetNullableDouble(metric, "positionX"),
                GetNullableDouble(metric, "positionY"),
                GetNullableDouble(metric, "positionZ"),
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

        return output;
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
                GetNullableInt(geometry, "vertexCount") ?? 0,
                GetString(geometry, "colorMethod") ?? string.Empty,
                GetNullableInt(geometry, "rgbColor"),
                GetString(geometry, "lineWeightMode") ?? string.Empty,
                GetNullableInt(geometry, "lineWeightHundredthsMm"),
                GetString(geometry, "linetype") ?? string.Empty,
                GetString(geometry, "layer") ?? string.Empty));
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

    private static bool ScalarEqual(
        double? first,
        double? second,
        double tolerance)
        => first.HasValue
            && second.HasValue
            && double.IsFinite(first.Value)
            && double.IsFinite(second.Value)
            && Math.Abs(first.Value - second.Value) <= tolerance;

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

    private static IReadOnlyList<double>? GetNullableDoubleArray(
        JsonElement element,
        string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var result = new List<double>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number
                || !item.TryGetDouble(out var parsed)
                || !double.IsFinite(parsed))
            {
                return null;
            }

            result.Add(parsed);
        }

        return result;
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
        double? EndY,
        int? RgbColor = null,
        double? StrokeWidthMm = null,
        IReadOnlyList<double>? DashPatternMm = null);

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
        double? SourceNativeMeasurementMm,
        double SourceRotationDegrees,
        double? SourceVisualCenterX,
        double? SourceVisualCenterY,
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
                null,
                double.NaN,
                null,
                null,
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
        int VertexCount,
        string ColorMethod,
        int? RgbColor,
        string LineWeightMode,
        int? LineWeightHundredthsMm,
        string Linetype,
        string Layer);

    private sealed record NativeDimensionEvidence(
        string CandidateId,
        string CandidateRole,
        string DimensionText,
        double? Measurement,
        string? Error,
        IReadOnlyList<NativeTextMetric> TextMetrics,
        IReadOnlyList<NativeBlockGeometry> BlockGeometry,
        IReadOnlyList<NativeBlockGeometry> ExplodedGeometry,
        IReadOnlyList<NativeTextMetric> ExplodedTextMetrics,
        string BlockGeometryCoordinateFrame,
        string ExplodedGeometryCoordinateFrame)
    {
        public static NativeDimensionEvidence Empty(string candidateId)
            => new(
                candidateId,
                string.Empty,
                string.Empty,
                null,
                null,
                [],
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
        string EntityType,
        string EntityHandle,
        string Text,
        string MetricKind,
        double? Width,
        double? Height,
        double? PositionX,
        double? PositionY,
        double? PositionZ,
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
            string.Empty,
            string.Empty,
            null,
            null,
            null,
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
