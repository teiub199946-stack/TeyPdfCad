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
            0x445566));

        var dimension = Assert.Single(new LinearDimensionRecognizer().Recognize(scene));
        var appearance = Assert.IsType<DimensionSourceAppearance>(dimension.SourceAppearance);

        Assert.Equal("5200", appearance.Text.Value);
        Assert.Equal(2.4, appearance.Text.HeightMm, 6);
        Assert.Equal("DIM", appearance.Text.Layer);
        Assert.Equal(0x445566, appearance.Text.RgbColor);
        Assert.Equal(["text"], appearance.Text.SourceIds);

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
}
