using System.Globalization;
using System.Reflection;
using Xunit;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Tests.Recognition;

public sealed class LinearDimensionRecognizerTests
{
    [Fact]
    public void Recognizes_5200_Horizontal_Dimension_At_1_100()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, -1), new Point2(1, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(51, -1), new Point2(53, 1)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(DimensionKind.Rotated, dimension.Kind);
        Assert.Equal(5200, dimension.DisplayedValue, 6);
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.Equal(1, dimension.ArrowEvidence, 6);
        Assert.True(dimension.Confidence >= 0.85);
    }

    [Fact]
    public void Captures_source_appearance_before_native_emission()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(52, 0),
            "DIM",
            ["dim-line"],
            0.35,
            [4.0, 1.0],
            0x112233));
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, -12),
            new Point2(0, 1),
            "DIM",
            ["ext-1"],
            0.25,
            [],
            0x223344));
        scene.Lines.Add(new LinePrimitive(
            new Point2(52, -12),
            new Point2(52, 1),
            "DIM",
            ["ext-2"],
            0.25,
            [],
            0x223344));
        scene.Lines.Add(new LinePrimitive(
            new Point2(-1, -1),
            new Point2(1, 1),
            "DIM",
            ["arrow-1"],
            0.30,
            [],
            0x334455));
        scene.Lines.Add(new LinePrimitive(
            new Point2(51, -1),
            new Point2(53, 1),
            "DIM",
            ["arrow-2"],
            0.30,
            [],
            0x334455));
        scene.Texts.Add(new TextPrimitive(
            "5200",
            new Point2(26, 3),
            2.4,
            0,
            "DIM",
            ["text"],
            0x445566,
            FontName: "Arial",
            AdvanceWidth: 5.0,
            VisualCenter: new Point2(26, 3),
            VisibleWidth: 4.5,
            FontProgramSha256: "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC",
            FontProgramSubtype: "TrueType",
            FontEncodingName: "WinAnsiEncoding",
            FontHasToUnicode: false,
            FontIsSubset: false));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        var appearance = Assert.IsType<DimensionSourceAppearance>(dimension.SourceAppearance);

        Assert.Equal("5200", appearance.Text.Value);
        Assert.Equal(2.4, appearance.Text.HeightMm, 6);
        Assert.Equal("DIM", appearance.Text.Layer);
        Assert.Equal(0x445566, appearance.Text.RgbColor);
        Assert.Equal(["text"], appearance.Text.SourceIds);
        Assert.Equal("Arial", appearance.Text.FontName);
        Assert.Equal(5.0, appearance.Text.AdvanceWidthMm);
        Assert.Equal(4.5, appearance.Text.VisibleWidthMm);
        Assert.Equal("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", appearance.Text.FontProgramSha256);
        Assert.Equal("TrueType", appearance.Text.FontProgramSubtype);
        Assert.Equal("WinAnsiEncoding", appearance.Text.FontEncodingName);
        Assert.True(appearance.Text.FontHasToUnicode == false);
        Assert.True(appearance.Text.FontIsSubset == false);

        Assert.Equal(0.35, appearance.DimensionLine.StrokeWidthMm!.Value, 6);
        Assert.Equal([4.0, 1.0], appearance.DimensionLine.DashPatternMm);
        Assert.Equal(0x112233, appearance.DimensionLine.RgbColor);
        Assert.Equal(["dim-line"], appearance.DimensionLine.SourceIds);
        Assert.False(appearance.DimensionLine.IsCompositeObservation);

        Assert.Equal(2, appearance.ExtensionLines.Count);
        Assert.Equal(2, appearance.ArrowLines.Count);
        Assert.All(appearance.ExtensionLines, line => Assert.Equal(0.25, line.StrokeWidthMm!.Value, 6));
        Assert.All(appearance.ArrowLines, line => Assert.Equal(0.30, line.StrokeWidthMm!.Value, 6));
    }

    [Fact]
    public void Split_dimension_line_is_marked_as_composite_source_observation()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(22, 0),
            SourceIds: ["dim-left"],
            StrokeWidthMm: 0.25,
            DashPatternMm: [],
            RgbColor: 0));
        scene.Lines.Add(new LinePrimitive(
            new Point2(30, 0),
            new Point2(52, 0),
            SourceIds: ["dim-right"],
            StrokeWidthMm: 0.25,
            DashPatternMm: [],
            RgbColor: 0));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1), SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1), SourceIds: ["ext-2"]));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, -1), new Point2(1, 1), SourceIds: ["arrow-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(51, -1), new Point2(53, 1), SourceIds: ["arrow-2"]));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0, SourceIds: ["text"]));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        var appearance = Assert.IsType<DimensionSourceAppearance>(dimension.SourceAppearance);

        Assert.True(appearance.DimensionLine.IsCompositeObservation);
        Assert.Equal(
            ["dim-left", "dim-right"],
            appearance.DimensionLine.SourceIds.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(0.25, appearance.DimensionLine.StrokeWidthMm!.Value, 6);
    }

    [Fact]
    public void Recognizes_Dimension_Line_Split_Around_Text()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(22, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(30, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, -1), new Point2(1, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(51, -1), new Point2(53, 1)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
    }

    [Fact]
    public void Recognizes_long_dimension_with_text_outside_right()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(300, 0),
            SourceIds: ["dim"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, -12),
            new Point2(0, 1),
            SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(300, -12),
            new Point2(300, 1),
            SourceIds: ["ext-2"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(-1, -1),
            new Point2(1, 1),
            SourceIds: ["arrow-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(299, -1),
            new Point2(301, 1),
            SourceIds: ["arrow-2"]));
        scene.Texts.Add(new TextPrimitive(
            "30000",
            new Point2(345, 0),
            2.5,
            0,
            SourceIds: ["text"]));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Equal(30000, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.Equal(["dim"], dimension.SourceAppearance!.DimensionLine.SourceIds);
    }

    [Fact]
    public void Recognizes_long_outside_text_dimension_with_micro_break()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(254.9, 0),
            SourceIds: ["dim-left"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(255.1, 0),
            new Point2(300, 0),
            SourceIds: ["dim-right"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, -12),
            new Point2(0, 1),
            SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(300, -12),
            new Point2(300, 1),
            SourceIds: ["ext-2"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(-1, -1),
            new Point2(1, 1),
            SourceIds: ["arrow-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(299, -1),
            new Point2(301, 1),
            SourceIds: ["arrow-2"]));
        scene.Texts.Add(new TextPrimitive(
            "30000",
            new Point2(345, 0),
            2.5,
            0,
            SourceIds: ["text"]));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Equal(30000, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.True(dimension.SourceAppearance!.DimensionLine.IsCompositeObservation);
        Assert.Equal(
            ["dim-left", "dim-right"],
            dimension.SourceAppearance.DimensionLine.SourceIds
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Recognizes_Aligned_Dimension_At_45_Degrees()
    {
        const double component = 36.76955262170047;
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(component, component)));
        scene.Lines.Add(new LinePrimitive(new Point2(8.485281374, -8.485281374), new Point2(-0.707106781, 0.707106781)));
        scene.Lines.Add(new LinePrimitive(new Point2(component + 8.485281374, component - 8.485281374), new Point2(component - 0.707106781, component + 0.707106781)));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, 0), new Point2(1, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(component - 1, component), new Point2(component + 1, component)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(16.263455967, 20.506096654), 2.5, 45));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(DimensionKind.Aligned, dimension.Kind);
        Assert.Equal(100, dimension.DrawingScale, 5);
        Assert.Equal(5200, dimension.ReconstructedMeasurement, 3);
    }

    [Fact]
    public void Accepts_Small_PdfImport_Coordinate_Noise()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0.01, -0.01), new Point2(52.02, 0.02)));
        scene.Lines.Add(new LinePrimitive(new Point2(-0.02, -12), new Point2(0.04, 1.01)));
        scene.Lines.Add(new LinePrimitive(new Point2(52.03, -12.02), new Point2(51.98, 1.02)));
        scene.Lines.Add(new LinePrimitive(new Point2(-0.99, -1.01), new Point2(1.01, 0.99)));
        scene.Lines.Add(new LinePrimitive(new Point2(51.01, -0.99), new Point2(53.01, 1.01)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26.01, 3.02), 2.5, 0));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.InRange(dimension.ReconstructedMeasurement, 5190, 5210);
    }

    [Fact]
    public void Canonical_scale_consensus_snaps_back_to_exact_scale()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 100.10, displayedValue: "2500");
        AddHorizontalDimension(scene, y: 30, importedLength: 99.95, displayedValue: "2500");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, dimension => Assert.Equal(25, dimension.DrawingScale, 6));
    }

    [Fact]
    public void Recognizes_NonCanonical_Scale_From_Two_Dimension_Consensus()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 50, displayedValue: "4800");
        AddHorizontalDimension(scene, y: 30, importedLength: 75, displayedValue: "7200");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, d => Assert.Equal(96, d.DrawingScale, 6));
        Assert.Contains(dimensions, d => Math.Abs(d.DisplayedValue - 4800) < 1e-6);
        Assert.Contains(dimensions, d => Math.Abs(d.DisplayedValue - 7200) < 1e-6);
    }

    [Fact]
    public void Scale_Consensus_Rejects_Single_Outlier_With_Different_Scale()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 50, displayedValue: "4800");
        AddHorizontalDimension(scene, y: 30, importedLength: 75, displayedValue: "7200");
        AddHorizontalDimension(scene, y: 60, importedLength: 10, displayedValue: "5000");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, d => Assert.Equal(96, d.DrawingScale, 6));
        Assert.DoesNotContain(dimensions, d => Math.Abs(d.DisplayedValue - 5000) < 1e-6);
    }

    [Fact]
    public void Recognizes_Two_Independent_Scale_Groups_On_One_Drawing()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 50, displayedValue: "4800");
        AddHorizontalDimension(scene, y: 30, importedLength: 75, displayedValue: "7200");
        AddHorizontalDimension(scene, y: 100, importedLength: 50, displayedValue: "2000");
        AddHorizontalDimension(scene, y: 130, importedLength: 75, displayedValue: "3000");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(4, dimensions.Count);
        Assert.Equal(2, dimensions.Count(d => Math.Abs(d.DrawingScale - 96) < 1e-6));
        Assert.Equal(2, dimensions.Count(d => Math.Abs(d.DrawingScale - 40) < 1e-6));
    }

    [Fact]
    public void Stronger_scale_group_wins_when_different_texts_claim_the_same_geometry()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(scene, y: 0, importedLength: 100, displayedValue: "5000");
        scene.Texts.Add(new TextPrimitive("500", new Point2(50, 3), 2.5, 0, SourceIds: ["wrong-500"]));
        AddHorizontalDimension(scene, y: 30, importedLength: 100, displayedValue: "5000");
        scene.Texts.Add(new TextPrimitive("500", new Point2(50, 33), 2.5, 0, SourceIds: ["wrong-500-2"]));
        AddHorizontalDimension(scene, y: 60, importedLength: 100, displayedValue: "5000");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(3, dimensions.Count);
        Assert.All(dimensions, dimension => Assert.Equal(50, dimension.DrawingScale, 6));
        Assert.DoesNotContain(dimensions, dimension => dimension.SourceText == "500");
    }

    [Fact]
    public void Equivalent_geometry_with_distinct_source_claim_sets_is_not_collapsed_before_planning()
    {
        var first = new DimensionCandidate(
            DimensionKind.Aligned,
            new Point2(0, 0),
            new Point2(100, 0),
            new Point2(50, 3),
            5000,
            5000,
            50,
            0.95,
            "5000",
            1)
        {
            SourceClaims =
            [
                new("shared-text", SourceUsageRole.Text, SourceClaimState.Valid, false),
                new("a-dimension-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false)
            ]
        };
        var second = first with
        {
            Confidence = 0.96,
            SourceClaims =
            [
                new("shared-text", SourceUsageRole.Text, SourceClaimState.Valid, false),
                new("b-dimension-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false)
            ]
        };
        var candidates = new List<DimensionCandidate> { first };
        var method = typeof(LinearDimensionRecognizer).GetMethod(
            "AddOrReplaceEquivalent",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(null, [candidates, second]);

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void Claimant_and_primary_selection_are_stable_across_input_permutations()
    {
        var original = new PrimitiveScene();
        AddHorizontalDimensionWithSources(original, y: 0, importedLength: 100, displayedValue: "5000", prefix: "a", textSourceId: "a-text-5000");
        original.Texts.Add(new TextPrimitive("10000", new Point2(50, 3), 2.5, 0, SourceIds: ["a-text-10000"]));
        AddHorizontalDimensionWithSources(original, y: 30, importedLength: 100, displayedValue: "5000", prefix: "b", textSourceId: "b-text-5000");
        original.Texts.Add(new TextPrimitive("10000", new Point2(50, 33), 2.5, 0, SourceIds: ["b-text-10000"]));

        string? expected = null;
        for (var seed = 1; seed <= 64; seed++)
        {
            var random = new Random(seed);
            var scene = new PrimitiveScene();
            scene.Lines.AddRange(original.Lines.OrderBy(_ => random.Next()));
            scene.Texts.AddRange(original.Texts.OrderBy(_ => random.Next()));

            var result = new LinearDimensionRecognizer().RecognizeDetailed(scene);
            var signature = string.Join(
                "|",
                result.PrimaryCandidates
                    .Concat(result.Claimants)
                    .Select(candidate => SourceReplacementPlanner.GetCandidateKey(candidate, 1)));

            expected ??= signature;
            Assert.Equal(expected, signature);
        }
    }

    [Fact]
    public void Shared_text_source_across_distinct_geometry_is_not_ranked_away()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimensionWithSources(scene, y: 0, importedLength: 100, displayedValue: "5000", prefix: "a", textSourceId: "shared-text");
        AddHorizontalDimensionWithSources(scene, y: 30, importedLength: 100, displayedValue: "5000", prefix: "b", textSourceId: "shared-text");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, dimension =>
            Assert.Contains(dimension.SourceClaims, claim =>
                claim.SourceId == "shared-text"
                && claim.State == SourceClaimState.Valid));
    }

    [Fact]
    public void Distinct_text_sources_claiming_same_geometry_remain_claimants_for_fail_closed_planning()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimensionWithSources(scene, y: 0, importedLength: 100, displayedValue: "5000", prefix: "a", textSourceId: "a-text-5000");
        scene.Texts.Add(new TextPrimitive("10000", new Point2(50, 3), 2.5, 0, SourceIds: ["a-text-10000"]));
        AddHorizontalDimensionWithSources(scene, y: 30, importedLength: 100, displayedValue: "5000", prefix: "b", textSourceId: "b-text-5000");
        scene.Texts.Add(new TextPrimitive("10000", new Point2(50, 33), 2.5, 0, SourceIds: ["b-text-10000"]));

        var result = new LinearDimensionRecognizer().RecognizeDetailed(scene);

        Assert.Equal(2, result.PrimaryCandidates.Count);
        Assert.Equal(4, result.Claimants.Count);
        Assert.All(result.Claimants, candidate =>
            Assert.Contains(candidate.SourceClaims, claim =>
                claim.Role == SourceUsageRole.Text
                && claim.State == SourceClaimState.Valid
                && !claim.IsPartial));
        Assert.Contains(result.Claimants, candidate =>
            candidate.SourceClaims.Any(claim => claim.SourceId == "a-text-5000"));
        Assert.Contains(result.Claimants, candidate =>
            candidate.SourceClaims.Any(claim => claim.SourceId == "a-text-10000"));
        Assert.Contains(result.Claimants, candidate =>
            candidate.SourceClaims.Any(claim => claim.SourceId == "b-text-5000"));
        Assert.Contains(result.Claimants, candidate =>
            candidate.SourceClaims.Any(claim => claim.SourceId == "b-text-10000"));
    }

    [Fact]
    public void Shared_text_source_exposes_one_primary_and_all_safety_claimants()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimensionWithSources(scene, y: 0, importedLength: 100, displayedValue: "5000", prefix: "a", textSourceId: "shared-primary-text");
        AddHorizontalDimensionWithSources(scene, y: 30, importedLength: 100, displayedValue: "5000", prefix: "b", textSourceId: "shared-primary-text");

        var result = new LinearDimensionRecognizer().RecognizeDetailed(scene);

        Assert.Single(result.PrimaryCandidates);
        Assert.Equal(2, result.Claimants.Count);
        Assert.All(result.Claimants, candidate =>
            Assert.Contains(candidate.SourceClaims, claim =>
                claim.SourceId == "shared-primary-text"
                && claim.State == SourceClaimState.Valid));
    }

    [Fact]
    public void Rotated_distinct_dimensions_sharing_text_source_are_both_retained_for_fail_closed_planning()
    {
        var scene = new PrimitiveScene();
        AddAlignedDimensionWithSources(scene, 0, 0, "a", "shared-rotated-text");
        AddAlignedDimensionWithSources(scene, 80, 60, "b", "shared-rotated-text");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, dimension => Assert.Equal(DimensionKind.Aligned, dimension.Kind));
        Assert.All(dimensions, dimension =>
            Assert.Contains(dimension.SourceClaims, claim =>
                claim.SourceId == "shared-rotated-text"
                && claim.State == SourceClaimState.Valid));
    }

    [Fact]
    public void Connected_skewed_short_extension_is_not_dropped_when_two_real_arrows_remain()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(100, 0), SourceIds: ["dim"]));

        // This line crosses the first dimension endpoint but is ~27 degrees away
        // from ideal perpendicularity. It models the long-span PDFIMPORT skew case
        // where rotating the dimension line around its midpoint nearly collapses
        // one extension line even though source connectivity remains explicit.
        scene.Lines.Add(new LinePrimitive(
            new Point2(-1.589, -3.119),
            new Point2(0.567, 1.114),
            SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(100, -3.5),
            new Point2(100, 1.25),
            SourceIds: ["ext-2"]));

        // Independent arrow evidence remains after both extension lines are excluded.
        scene.Lines.Add(new LinePrimitive(new Point2(-0.875, -0.875), new Point2(0.875, 0.875), SourceIds: ["arrow-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(99.125, 0.875), new Point2(100.875, -0.875), SourceIds: ["arrow-2"]));
        scene.Texts.Add(new TextPrimitive("100", new Point2(50, 0), 2.5, 0, SourceIds: ["text"]));

        var dimensions = new LinearDimensionRecognizer().Recognize(
            scene,
            new DimensionRecognitionOptions { DrawingScale = 1 });

        var dimension = Assert.Single(dimensions);
        Assert.Equal(1d, dimension.ArrowEvidence);
        Assert.Contains(dimension.SourceClaims, claim => claim.SourceId == "ext-1" && claim.Role == SourceUsageRole.ExtensionLine);
        Assert.Contains(dimension.SourceClaims, claim => claim.SourceId == "arrow-1" && claim.Role == SourceUsageRole.ArrowGeometry);
        Assert.Contains(dimension.SourceClaims, claim => claim.SourceId == "arrow-2" && claim.Role == SourceUsageRole.ArrowGeometry);
    }

    [Fact]
    public void One_sided_arrow_evidence_is_abstained_as_ambiguous()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, -12), new Point2(0, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, -12), new Point2(52, 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, -1), new Point2(1, 1)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));

        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    [Fact]
    public void Skewed_extension_line_cannot_supply_missing_second_arrow()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(52, 0),
            SourceIds: ["dim"]));

        // Start extension is clearly perpendicular.
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, -12),
            new Point2(0, 1),
            SourceIds: ["ext-1"]));

        // End extension is deliberately near the shared tolerance boundary:
        // perpendicular enough to be selected as ExtensionLine, but diagonal
        // enough that the old arrow detector could reuse it as arrow evidence.
        scene.Lines.Add(new LinePrimitive(
            new Point2(54.65, -12),
            new Point2(51.80, 1),
            SourceIds: ["ext-2"]));

        // Only one genuine arrow/tick exists.
        scene.Lines.Add(new LinePrimitive(
            new Point2(-1, -1),
            new Point2(1, 1),
            SourceIds: ["arrow-1"]));

        scene.Texts.Add(new TextPrimitive(
            "5200",
            new Point2(26, 3),
            2.5,
            0,
            SourceIds: ["text"]));

        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    [Fact]
    public void Nearby_diagonal_wall_line_does_not_fake_second_arrow()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, 0),
            new Point2(52, 0),
            SourceIds: ["dim"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(0, -12),
            new Point2(0, 1),
            SourceIds: ["ext-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(52, -12),
            new Point2(52, 1),
            SourceIds: ["ext-2"]));

        // Genuine arrow only at the first endpoint.
        scene.Lines.Add(new LinePrimitive(
            new Point2(-1, -1),
            new Point2(1, 1),
            SourceIds: ["arrow-1"]));

        // Short diagonal is inside the old radius around the second endpoint,
        // but it does not touch/cross that endpoint. It represents unrelated
        // nearby wall/grid geometry and must not count as arrow evidence.
        scene.Lines.Add(new LinePrimitive(
            new Point2(49, 2),
            new Point2(51, 4),
            SourceIds: ["wall-fragment"]));

        scene.Texts.Add(new TextPrimitive(
            "5200",
            new Point2(26, 3),
            2.5,
            0,
            SourceIds: ["text"]));

        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    [Fact]
    public void Rejects_Number_Next_To_Ordinary_Line_Without_Extension_Lines()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));
        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    [Fact]
    public void Rejects_dimension_geometry_perpendicular_to_text_orientation()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new(new(0, 0), new(52, 0)));
        scene.Lines.Add(new(new(0, -12), new(0, 1)));
        scene.Lines.Add(new(new(52, -12), new(52, 1)));
        scene.Lines.Add(new(new(-1, -1), new(1, 1)));
        scene.Lines.Add(new(new(51, -1), new(53, 1)));
        scene.Texts.Add(new("5200", new(26, 3), 2.5, 90));

        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }

    [Fact]
    public void Rejects_Distant_Lines_That_Only_Intersect_Endpoints_When_Extended()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(52, 0)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 100), new Point2(0, 120)));
        scene.Lines.Add(new LinePrimitive(new Point2(52, 100), new Point2(52, 120)));
        scene.Texts.Add(new TextPrimitive("5200", new Point2(26, 3), 2.5, 0));
        Assert.Empty(new LinearDimensionRecognizer().Recognize(scene));
    }


    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(45, false)]
    [InlineData(45, true)]
    [InlineData(90, false)]
    [InlineData(90, true)]
    public void Long_outside_text_micro_gap_is_stable_across_rotation_and_endpoint_order(
        int rotationDegrees,
        bool reverseEndpoints)
    {
        var scene = new PrimitiveScene();
        AddLongOutsideTextMicroGapDimension(
            scene,
            rotationDegrees,
            reverseEndpoints,
            includeEquivalentClone: false,
            includeAdjacentNonBlockingFragment: false);

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Equal(30000, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
        Assert.True(dimension.SourceAppearance!.DimensionLine.IsCompositeObservation);
    }

    [Fact]
    public void Equivalent_cloned_fragment_does_not_change_micro_gap_recognition()
    {
        var scene = new PrimitiveScene();
        AddLongOutsideTextMicroGapDimension(
            scene,
            rotationDegrees: 45,
            reverseEndpoints: false,
            includeEquivalentClone: true,
            includeAdjacentNonBlockingFragment: false);

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Equal(30000, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
    }

    [Fact]
    public void Near_collinear_fragment_outside_micro_gap_does_not_block_legitimate_merge()
    {
        var scene = new PrimitiveScene();
        AddLongOutsideTextMicroGapDimension(
            scene,
            rotationDegrees: 0,
            reverseEndpoints: false,
            includeEquivalentClone: false,
            includeAdjacentNonBlockingFragment: true);

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));

        Assert.Equal(30000, dimension.DisplayedValue, 6);
        Assert.Equal(100, dimension.DrawingScale, 6);
    }


    private static void AddLongOutsideTextMicroGapDimension(
        PrimitiveScene scene,
        int rotationDegrees,
        bool reverseEndpoints,
        bool includeEquivalentClone,
        bool includeAdjacentNonBlockingFragment)
    {
        var radians = rotationDegrees * Math.PI / 180.0;
        var along = new Point2(Math.Cos(radians), Math.Sin(radians));
        var normal = new Point2(-Math.Sin(radians), Math.Cos(radians));

        Point2 Transform(double x, double y) => new(
            along.X * x + normal.X * y,
            along.Y * x + normal.Y * y);

        void AddLine(double x1, double y1, double x2, double y2, string sourceId)
        {
            var start = Transform(x1, y1);
            var end = Transform(x2, y2);
            scene.Lines.Add(new LinePrimitive(
                reverseEndpoints ? end : start,
                reverseEndpoints ? start : end,
                SourceIds: [sourceId]));
        }

        AddLine(0, 0, 254.9, 0, "dim-left");
        AddLine(255.1, 0, 300, 0, "dim-right");

        if (includeEquivalentClone)
            AddLine(0, 0, 254.9, 0, "dim-left-clone");

        // This fragment is close and collinear with the right source line,
        // but does not occupy the (254.9, 255.1) micro-gap.
        if (includeAdjacentNonBlockingFragment)
            AddLine(255.15, 0, 300, 0, "nearby-nonblocking");

        AddLine(0, -12, 0, 1, "ext-1");
        AddLine(300, -12, 300, 1, "ext-2");
        AddLine(-1, -1, 1, 1, "arrow-1");
        AddLine(299, -1, 301, 1, "arrow-2");
        scene.Texts.Add(new TextPrimitive(
            "30000",
            Transform(345, 0),
            2.5,
            rotationDegrees,
            SourceIds: ["text"]));
    }

    private static void AddAlignedDimensionWithSources(
        PrimitiveScene scene,
        double originX,
        double originY,
        string prefix,
        string textSourceId)
    {
        const double component = 36.76955262170047;
        var start = new Point2(originX, originY);
        var end = new Point2(originX + component, originY + component);

        scene.Lines.Add(new LinePrimitive(start, end, SourceIds: [$"{prefix}-dim"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(originX + 8.485281374, originY - 8.485281374),
            new Point2(originX - 0.707106781, originY + 0.707106781),
            SourceIds: [$"{prefix}-ext-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(end.X + 8.485281374, end.Y - 8.485281374),
            new Point2(end.X - 0.707106781, end.Y + 0.707106781),
            SourceIds: [$"{prefix}-ext-2"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(originX - 1, originY),
            new Point2(originX + 1, originY),
            SourceIds: [$"{prefix}-arrow-1"]));
        scene.Lines.Add(new LinePrimitive(
            new Point2(end.X - 1, end.Y),
            new Point2(end.X + 1, end.Y),
            SourceIds: [$"{prefix}-arrow-2"]));
        scene.Texts.Add(new TextPrimitive(
            "5200",
            new Point2(originX + 16.263455967, originY + 20.506096654),
            2.5,
            45,
            SourceIds: [textSourceId]));
    }

    private static void AddHorizontalDimensionWithSources(
        PrimitiveScene scene,
        double y,
        double importedLength,
        string displayedValue,
        string prefix,
        string textSourceId)
    {
        scene.Lines.Add(new LinePrimitive(new Point2(0, y), new Point2(importedLength, y), SourceIds: [$"{prefix}-dim"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, y - 12), new Point2(0, y + 1), SourceIds: [$"{prefix}-ext-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(importedLength, y - 12), new Point2(importedLength, y + 1), SourceIds: [$"{prefix}-ext-2"]));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, y - 1), new Point2(1, y + 1), SourceIds: [$"{prefix}-arrow-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(importedLength - 1, y - 1), new Point2(importedLength + 1, y + 1), SourceIds: [$"{prefix}-arrow-2"]));
        scene.Texts.Add(new TextPrimitive(displayedValue, new Point2(importedLength / 2.0, y + 3), 2.5, 0, SourceIds: [textSourceId]));
    }

    [Fact]
    public void Long_dimension_is_not_normalized_only_because_canonical_scale_is_nearby()
    {
        var scene = new PrimitiveScene();
        // 5200 / 53.3 = 97.56, inside the existing 3% snap window for 1:100,
        // but the resulting measurement error is >2%. The semantic
        // normalization is intentionally restricted to short paper spans.
        AddHorizontalDimension(
            scene,
            y: 0,
            importedLength: 53.3,
            displayedValue: "5200");

        var dimensions = new LinearDimensionRecognizer().Recognize(scene);

        Assert.Empty(dimensions);
    }

    [Fact]
    public void Explicit_scale_keeps_strict_measurement_tolerance_for_short_dimension()
    {
        var scene = new PrimitiveScene();
        AddHorizontalDimension(
            scene,
            y: 0,
            importedLength: 0.5144,
            displayedValue: "25");

        var dimensions = new LinearDimensionRecognizer().Recognize(
            scene,
            new DimensionRecognitionOptions
            {
                DrawingScale = 50
            });

        Assert.Empty(dimensions);
    }

    private static void AddHorizontalDimension(
        PrimitiveScene scene,
        double y,
        double importedLength,
        string displayedValue)
    {
        scene.Lines.Add(new LinePrimitive(new Point2(0, y), new Point2(importedLength, y)));
        scene.Lines.Add(new LinePrimitive(new Point2(0, y - 12), new Point2(0, y + 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(importedLength, y - 12), new Point2(importedLength, y + 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(-1, y - 1), new Point2(1, y + 1)));
        scene.Lines.Add(new LinePrimitive(new Point2(importedLength - 1, y - 1), new Point2(importedLength + 1, y + 1)));
        scene.Texts.Add(new TextPrimitive(displayedValue, new Point2(importedLength / 2.0, y + 3), 2.5, 0));
    }


    [Fact]
    public void Equivalent_source_claim_sets_are_canonical_across_claim_enumeration_order()
    {
        var first = new DimensionCandidate(
            DimensionKind.Rotated,
            new Point2(0, 0),
            new Point2(10, 0),
            new Point2(5, 2),
            1000,
            1000,
            100,
            0.95,
            "1000",
            1,
            ["dim", "ext-1", "text"])
        {
            SourceClaims =
            [
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("ext-1", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var reordered = first with
        {
            SourceClaims =
            [
                new("text", SourceUsageRole.Text, SourceClaimState.Valid, false),
                new("dim", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("ext-1", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false)
            ]
        };
        var candidates = new List<DimensionCandidate> { first };
        var method = typeof(LinearDimensionRecognizer).GetMethod(
            "AddOrReplaceEquivalent",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        method!.Invoke(null, [candidates, reordered]);

        Assert.Single(candidates);
    }


    [Fact]
    public void Source_claim_set_key_is_culture_invariant_and_ordinal()
    {
        var candidate = new DimensionCandidate(
            DimensionKind.Rotated,
            new Point2(0, 0),
            new Point2(10, 0),
            new Point2(5, 2),
            1000,
            1000,
            100,
            0.95,
            "1000",
            1,
            ["I", "i", "ı", "İ", "ß", "Ж"])
        {
            SourceClaims =
            [
                new("I", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("i", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("ı", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("İ", SourceUsageRole.Text, SourceClaimState.Valid, false),
                new("ß", SourceUsageRole.Text, SourceClaimState.Valid, false),
                new("Ж", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var method = typeof(LinearDimensionRecognizer).GetMethod(
            "SourceClaimSetKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var keys = new[] { "en-US", "tr-TR", "de-DE", "ru-RU" }
                .Select(cultureName =>
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                    return Assert.IsType<string>(method!.Invoke(null, [candidate]));
                })
                .ToArray();

            Assert.All(keys, key => Assert.Equal(keys[0], key));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Source_claim_set_key_cannot_collide_when_a_source_id_contains_old_delimiters()
    {
        DimensionCandidate CandidateFor(params RecognizerSourceClaim[] claims)
            => new(
                DimensionKind.Rotated,
                new Point2(0, 0),
                new Point2(10, 0),
                new Point2(5, 2),
                1000,
                1000,
                100,
                0.95,
                "1000",
                1,
                claims.Select(claim => claim.SourceId).ToArray())
            {
                SourceClaims = claims
            };

        var splitClaims = CandidateFor(
            new("a", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
            new("b", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false));
        var embeddedDelimiter = CandidateFor(
            new("a\u001f1\u001f0\u001f0\u001eb", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false));

        var method = typeof(LinearDimensionRecognizer).GetMethod(
            "SourceClaimSetKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var splitKey = Assert.IsType<string>(method!.Invoke(null, [splitClaims]));
        var embeddedKey = Assert.IsType<string>(method!.Invoke(null, [embeddedDelimiter]));

        Assert.NotEqual(splitKey, embeddedKey);
    }
}
