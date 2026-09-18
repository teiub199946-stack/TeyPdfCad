using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Sheets;
using Xunit;

namespace TeyPdfCad.Tests;

public sealed class TitleBlockDetectorTests
{
    [Fact]
    public void Detects_candidate_and_keeps_unknown_fields_editable()
    {
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape);
        var scene = new PrimitiveScene
        {
            Sheet = sheet,
        };
        scene.Lines.Add(new LinePrimitive(new Point2(290, 4), new Point2(420, 4), "TB", ["L1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(290, 75), new Point2(420, 75), "TB", ["L2"]));
        scene.Texts.Add(new TextPrimitive("Наименование", new Point2(300, 30), 3, 0, "TB", ["T1"]));

        var result = TitleBlockDetector.Detect(scene, sheet);

        Assert.NotNull(result);
        Assert.True(result!.IsCandidate);
        var field = Assert.Single(result.Fields);
        Assert.Equal(TitleBlockFieldKind.Unknown, field.Kind);
        Assert.Equal("Наименование", field.Value);
        Assert.Contains("T1", field.ProvenanceIds);
        Assert.Contains("L1", result.Region.ProvenanceIds);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void Fails_closed_without_text_or_without_two_boundary_segments()
    {
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape);
        var noText = new PrimitiveScene { Sheet = sheet };
        noText.Lines.Add(new LinePrimitive(new Point2(290, 4), new Point2(420, 4)));
        Assert.Null(TitleBlockDetector.Detect(noText, sheet));

        var oneLine = new PrimitiveScene { Sheet = sheet };
        oneLine.Lines.Add(new LinePrimitive(new Point2(290, 4), new Point2(420, 4)));
        oneLine.Texts.Add(new TextPrimitive("x", new Point2(300, 30), 3, 0));
        Assert.Null(TitleBlockDetector.Detect(oneLine, sheet));
    }

    [Fact]
    public void Uses_explicit_page_origin_when_detecting_candidate_region()
    {
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape)
        {
            PageBounds = new SheetPageBounds(1000, 2000, 420, 297),
        };
        var scene = new PrimitiveScene { Sheet = sheet };
        scene.Lines.Add(new LinePrimitive(new Point2(1290, 2004), new Point2(1420, 2004), SourceIds: ["L1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(1290, 2060), new Point2(1420, 2060), SourceIds: ["L2"]));
        scene.Texts.Add(new TextPrimitive("Лист 1", new Point2(1300, 2030), 3, 0, SourceIds: ["T1"]));

        var result = TitleBlockDetector.Detect(scene, sheet);

        Assert.NotNull(result);
        Assert.Equal(1285.6, result!.Region.MinX, precision: 6);
        Assert.Equal(2000, result.Region.MinY, precision: 6);
    }

    [Fact]
    public void Keeps_lines_that_cross_the_region_with_endpoints_outside()
    {
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape);
        var scene = new PrimitiveScene { Sheet = sheet };
        scene.Lines.Add(new LinePrimitive(new Point2(280, 40), new Point2(430, 40), SourceIds: ["crossing-1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(280, 60), new Point2(430, 60), SourceIds: ["crossing-2"]));
        scene.Texts.Add(new TextPrimitive("Штамп", new Point2(300, 50), 3, 0, SourceIds: ["text-1"]));

        var result = TitleBlockDetector.Detect(scene, sheet);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Lines.Count);
        Assert.Contains("crossing-1", result.Region.ProvenanceIds);
        Assert.Contains("crossing-2", result.Region.ProvenanceIds);
    }

    [Fact]
    public void Applies_page_scale_when_calculating_title_block_region()
    {
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape)
        {
            PageBounds = new SheetPageBounds(2829, 2065, 420, 297, DrawingUnitsPerMm: 100),
        };
        var scene = new PrimitiveScene { Sheet = sheet };
        scene.Lines.Add(new LinePrimitive(new Point2(31389, 3000), new Point2(44829, 3000), SourceIds: ["L1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(31389, 8000), new Point2(44829, 8000), SourceIds: ["L2"]));
        scene.Texts.Add(new TextPrimitive("Штамп", new Point2(32000, 5000), 300, 0, SourceIds: ["T1"]));

        var result = TitleBlockDetector.Detect(scene, sheet);

        Assert.NotNull(result);
        Assert.Equal(31389, result!.Region.MinX, precision: 6);
        Assert.Equal(2065, result.Region.MinY, precision: 6);
        Assert.Equal(44829, result.Region.MaxX, precision: 6);
        Assert.Equal(9193, result.Region.MaxY, precision: 6);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void DetectGeometryOnly_accepts_linework_when_pdfimport_has_no_text_primitives()
    {
        var sheet = new SheetMetadata(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape)
        {
            PageBounds = new SheetPageBounds(2829, 2065, 420, 297, DrawingUnitsPerMm: 100),
        };
        var scene = new PrimitiveScene { Sheet = sheet };
        scene.Lines.Add(new LinePrimitive(new Point2(31389, 3000), new Point2(44829, 3000), SourceIds: ["L1"]));
        scene.Lines.Add(new LinePrimitive(new Point2(31389, 8000), new Point2(44829, 8000), SourceIds: ["L2"]));

        var result = TitleBlockDetector.DetectGeometryOnly(scene, sheet);

        Assert.NotNull(result);
        Assert.Empty(result!.Fields);
        Assert.Equal(2, result.Lines.Count);
    }
}
