using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Dwg;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Templates;
using ACadSharp.IO;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DwgDocumentWriterTests
{
    [Fact]
    public void Writer_emits_level_as_named_editable_block()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Levels = [new LevelCandidate(new(10, 10), new(17, 10), "±0.000", 0.95d, ["level"])]
        };

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics })));

        var insert = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        Assert.Equal("TEY_LEVEL", insert.Block.Name);
        Assert.Contains(insert.Block.Entities.OfType<ACadSharp.Entities.TextEntity>(), text => text.Value == "±0.000");
    }

    [Fact]
    public void Writer_emits_arc_length_as_native_arc_dimension()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            ArcDimensions = [new ArcDimensionCandidate(new(50, 50), 20, 0, Math.PI / 2, new(65, 65), "L=31,42", 0.95d, ["arc", "label"])]
        };

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics })));

        var dimension = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.DimensionArc>());
        Assert.Equal(50, dimension.Center.X, 6);
        Assert.Equal(20, Math.Sqrt(Math.Pow(dimension.FirstPoint.X - dimension.Center.X, 2) + Math.Pow(dimension.FirstPoint.Y - dimension.Center.Y, 2)), 6);
        Assert.Equal("L=31,42", dimension.Text);
    }

    [Fact]
    public void Writer_inserts_confirmed_template_block_in_model_space()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
            [new VectorLine("stamp", new Point2(0, 0), new Point2(10, 0), new VectorStyle())]);
        var document = new VectorPdfDocument([page]);
        var library = new TemplateLibrary([], [new TemplateBlockDefinition(
            "A3-landscape",
            [
                new TemplateGeometryEntity("AcDbLine", "A1", [new TemplatePoint(0, 0), new TemplatePoint(420, 0)], null, null, "0"),
                new TemplateGeometryEntity("AcDbCircle", "A2", [new TemplatePoint(10, 10), new TemplatePoint(15, 10)], null, null, "0")
            ],
            [])]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            templateLibrary: library,
            templateSelectionsByPage: new Dictionary<int, TemplateSelection>
            {
                [1] = new(true, "A3-landscape", "test", ["stamp"])
            })));

        var insert = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        Assert.Equal("A3-landscape", insert.Block.Name);
        Assert.Empty(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Single(insert.Block.Entities.OfType<ACadSharp.Entities.Circle>());
    }

    [Fact]
    public void Writer_normalizes_template_geometry_by_exported_block_origin()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var library = new TemplateLibrary([], [new TemplateBlockDefinition(
            "A3-landscape",
            [new TemplateGeometryEntity(
                "AcDbLine",
                "A1",
                [new TemplatePoint(1000, 2000), new TemplatePoint(1420, 2000)],
                null,
                null,
                "0")],
            [],
            new TemplatePoint(1000, 2000))]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            templateLibrary: library,
            templateSelectionsByPage: new Dictionary<int, TemplateSelection>
            {
                [1] = new(true, "A3-landscape", "test")
            })));

        var insert = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        var line = Assert.Single(insert.Block.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Equal(0d, line.StartPoint.X, 6);
        Assert.Equal(0d, line.StartPoint.Y, 6);
        Assert.Equal(420d, line.EndPoint.X, 6);
        Assert.Equal(0d, line.EndPoint.Y, 6);
    }

    [Fact]
    public void Writer_preserves_template_polyline_closure_without_static_text_duplication()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var library = new TemplateLibrary([], [new TemplateBlockDefinition(
            "A3-landscape",
            [
                new TemplateGeometryEntity(
                    "AcDbPolyline",
                    "P1",
                    [new TemplatePoint(0, 0), new TemplatePoint(10, 0), new TemplatePoint(10, 10)],
                    null,
                    null,
                    "0",
                    IsClosed: true),
                new TemplateGeometryEntity(
                    "AcDbText",
                    "T1",
                    [new TemplatePoint(5, 5)],
                    "Лист",
                    3.5,
                    "0",
                    RotationRadians: Math.PI / 2d)
            ],
            [])]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            templateLibrary: library,
            templateSelectionsByPage: new Dictionary<int, TemplateSelection>
            {
                [1] = new(true, "A3-landscape", "test")
            })));

        var block = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Insert>()).Block;
        Assert.True(Assert.Single(block.Entities.OfType<ACadSharp.Entities.LwPolyline>()).IsClosed);
        Assert.Empty(block.Entities.OfType<ACadSharp.Entities.TextEntity>());
    }

    [Fact]
    public void Writer_does_not_duplicate_template_mtext_when_source_text_is_preserved()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine("frame", new Point2(0, 0), new Point2(10, 0), new VectorStyle()),
            new VectorText("value", "Исходное значение", new Point2(5, 5), 3, new VectorStyle())
        ]);
        var document = new VectorPdfDocument([page]);
        var library = new TemplateLibrary([], [new TemplateBlockDefinition(
            "A3-landscape",
            [
                new TemplateGeometryEntity("AcDbLine", "F1", [new TemplatePoint(0, 0), new TemplatePoint(10, 0)], null, null, "0"),
                new TemplateGeometryEntity("AcDbMText", "T1", [new TemplatePoint(5, 5)], "Статическая подпись", 3, "0")
            ],
            [])]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            templateLibrary: library,
            templateSelectionsByPage: new Dictionary<int, TemplateSelection>
            {
                [1] = new(true, "A3-landscape", "test", ["frame"])
            })));

        var insert = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        Assert.Empty(insert.Block.Entities.OfType<ACadSharp.Entities.TextEntity>());
        Assert.Equal("Исходное значение", Assert.Single(
            drawing.Entities.OfType<ACadSharp.Entities.TextEntity>()).Value);
    }

    [Fact]
    public void Writer_preserves_template_arc_as_native_arc()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var library = new TemplateLibrary([], [new TemplateBlockDefinition(
            "A3-landscape",
            [new TemplateGeometryEntity(
                "AcDbArc",
                "A1",
                [new TemplatePoint(15, 25)],
                null,
                null,
                "0",
                ArcRadius: 5,
                StartAngleRadians: 0,
                EndAngleRadians: Math.PI / 2d)],
            [],
            new TemplatePoint(10, 20))]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            templateLibrary: library,
            templateSelectionsByPage: new Dictionary<int, TemplateSelection>
            {
                [1] = new(true, "A3-landscape", "test")
            })));

        var arc = Assert.Single(Assert.Single(
            drawing.Entities.OfType<ACadSharp.Entities.Insert>()).Block.Entities.OfType<ACadSharp.Entities.Arc>());
        Assert.Equal(5d, arc.Center.X, 6);
        Assert.Equal(5d, arc.Center.Y, 6);
        Assert.Equal(5d, arc.Radius, 6);
        Assert.Equal(Math.PI / 2d, arc.EndAngle, 12);
    }

    [Fact]
    public void Writer_emits_high_confidence_axis_as_editable_named_block()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Axes = [new AxisCandidate(new Point2(0, 0), new Point2(25.4, 0), 1d, ["axis"])]
        };

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics })));

        var insert = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Insert>());
        Assert.Equal("TEY_AXIS", insert.Block.Name);
    }

    [Fact]
    public void Writer_emits_recognized_leader_with_service_lineweight()
    {
        var page = new VectorPdfPage(1, 72, 72, 0, []);
        var document = new VectorPdfDocument([page]);
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Leaders = [new LeaderCandidate(new Point2(0, 0), new Point2(20, 10), "Текст", 1d, ["leader"])]
        };

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics })));

        var leader = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Leader>());
        Assert.Equal(ACadSharp.LineWeightType.W9, leader.LineWeight);
        Assert.Equal("Текст", Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.TextEntity>()).Value);
    }

    [Fact]
    public void Writer_routes_low_confidence_axis_source_geometry_to_review_layer()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
            [new VectorLine("maybe-axis", new Point2(0, 0), new Point2(25.4, 0), new VectorStyle("ИСХОДНЫЙ"))]);
        var document = new VectorPdfDocument([page]);
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Warnings = [new SemanticWarning("axis-low-confidence", "Needs review.", ["maybe-axis"])]
        };

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document),
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics })));

        var line = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Equal("TEY_REVIEW_AXIS", line.Layer.Name);
    }

    [Fact]
    public void Writer_type_is_available_without_autocad()
    {
        var document = new VectorPdfDocument([new VectorPdfPage(1, 595.276, 841.89, 0, [])]);
        var plan = new DocumentLayoutPlanner().Create(document);

        Assert.NotNull(new AcadSharpDwgWriter());
        Assert.Single(plan.Sheets);
    }

    [Fact]
    public void Writer_creates_a_binary_dwg_without_autocad()
    {
        var document = new VectorPdfDocument([new VectorPdfPage(1, 595.276, 841.89, 0, [])]);
        var plan = new DocumentLayoutPlanner().Create(document);

        var bytes = new AcadSharpDwgWriter().Write(document, plan);

        Assert.True(bytes.Length > 32);
        Assert.Equal("AC", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
    }

    [Fact]
    public void Writer_emits_source_lines_into_model_space()
    {
        var page = new VectorPdfPage(1, 595.276, 841.89, 0,
            [new VectorLine("line-1", new Point2(10, 20), new Point2(30, 40), new VectorStyle())]);
        var plan = new DocumentLayoutPlanner().Create(new VectorPdfDocument([page]));

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(new VectorPdfDocument([page]), plan)));

        var line = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Equal(10, line.StartPoint.X);
        Assert.Equal(40, line.EndPoint.Y);
    }

    [Fact]
    public void Writer_emits_editable_polylines()
    {
        var page = new VectorPdfPage(1, 595.276, 841.89, 0,
            [new VectorPolyline("poly-1", [new(0, 0), new(10, 0), new(10, 5)], true, new VectorStyle())]);
        var plan = new DocumentLayoutPlanner().Create(new VectorPdfDocument([page]));

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(new VectorPdfDocument([page]), plan)));

        var polyline = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>());
        Assert.True(polyline.IsClosed);
        Assert.Equal(3, polyline.Vertices.Count);
    }

    [Fact]
    public void Writer_creates_no_user_layout_or_viewport_per_source_page()
    {
        var document = new VectorPdfDocument(Enumerable.Range(1, 3)
            .Select(number => new VectorPdfPage(number, 595.276, 841.89, 0, []))
            .ToArray());
        var plan = new DocumentLayoutPlanner().Create(document);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, plan)));

        Assert.DoesNotContain(drawing.Layouts, layout => layout.Name.StartsWith("Лист-", StringComparison.Ordinal));
        Assert.Empty(drawing.Layouts
            .Where(layout => layout.Name.StartsWith("Лист-", StringComparison.Ordinal))
            .SelectMany(layout => layout.AssociatedBlock.Entities)
            .OfType<ACadSharp.Entities.Viewport>());
    }

    [Fact]
    public void Writer_isolates_each_page_in_model_space_without_viewports()
    {
        var firstPage = new VectorPdfPage(1, 72, 72, 0,
            [new VectorLine("first", new Point2(0, 0), new Point2(72, 72), new VectorStyle())]);
        var secondPage = new VectorPdfPage(2, 72, 72, 0,
            [new VectorLine("second", new Point2(0, 0), new Point2(72, 72), new VectorStyle())]);
        var document = new VectorPdfDocument([firstPage, secondPage]);
        var plan = new DocumentLayoutPlanner().Create(document);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, plan)));

        var lines = drawing.Entities.OfType<ACadSharp.Entities.Line>().OrderBy(line => line.StartPoint.X).ToArray();
        Assert.Equal(0, lines[0].StartPoint.X, 6);
        Assert.Equal(plan.Sheets[1].ModelOriginX, lines[1].StartPoint.X, 6);

        Assert.Empty(drawing.Layouts
            .Where(layout => layout.Name.StartsWith("Лист-", StringComparison.Ordinal))
            .SelectMany(layout => layout.AssociatedBlock.Entities)
            .OfType<ACadSharp.Entities.Viewport>());
    }

    [Fact]
    public void Writer_preserves_source_layer_color_and_dashed_linetype()
    {
        var style = new VectorStyle(
            SourceLayer: "Оси",
            RgbColor: 0xFF0000,
            StrokeWidthPoints: 0.5,
            DashPatternPoints: [12, 6]);
        var page = new VectorPdfPage(1, 72, 72, 0,
            [new VectorLine("axis", new Point2(0, 0), new Point2(25.4, 0), style)]);
        var document = new VectorPdfDocument([page]);
        var plan = new DocumentLayoutPlanner().Create(document);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, plan)));

        var line = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Equal("Оси", line.Layer.Name);
        Assert.True(line.Color.IsTrueColor);
        Assert.Equal(0xFF0000, line.Color.TrueColor);
        Assert.NotEqual(ACadSharp.Tables.LineType.Continuous.Name, line.LineType.Name);
        Assert.Equal(2, line.LineType.Segments.Count());
    }

    [Fact]
    public void Writer_maps_pdf_black_to_adaptive_autocad_color_seven()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
            [new VectorLine(
                "black",
                new Point2(0, 0),
                new Point2(25.4, 0),
                new VectorStyle(RgbColor: 0x000000))]);
        var document = new VectorPdfDocument([page]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
            document,
            new DocumentLayoutPlanner().Create(document))));

        var line = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.False(line.Color.IsTrueColor);
        Assert.Equal(7, line.Color.Index);
    }

    [Fact]
    public void Writer_emits_source_text_as_editable_dwg_text()
    {
        var style = new VectorStyle(SourceLayer: "ТЕКСТ", RgbColor: 0x112233);
        var page = new VectorPdfPage(1, 72, 72, 0,
            [new VectorText("note", "Марка оси", new Point2(5, 7), 10, style, RotationRadians: Math.PI / 2d)]);
        var document = new VectorPdfDocument([page]);
        var plan = new DocumentLayoutPlanner().Create(document);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, plan)));

        var text = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.TextEntity>());
        Assert.Equal("Марка оси", text.Value);
        Assert.Equal(5, text.InsertPoint.X, 6);
        Assert.Equal(7, text.InsertPoint.Y, 6);
        Assert.Equal(10 * VectorPdfPage.MillimetresPerPoint, text.Height, 6);
        Assert.Equal(Math.PI / 2d, text.Rotation, 6);
        Assert.Equal("ТЕКСТ", text.Layer.Name);
        Assert.Equal(0x112233, text.Color.TrueColor);
    }

    [Fact]
    public void Writer_promotes_a_tessellated_circular_path_to_a_native_circle()
    {
        var vertices = Enumerable.Range(0, 64)
            .Select(index => new Point2(10d + 5d * Math.Cos(index * Math.PI / 32d), 20d + 5d * Math.Sin(index * Math.PI / 32d)))
            .ToArray();
        var page = new VectorPdfPage(1, 72, 72, 0,
            [new VectorPolyline("circle", vertices, true, new VectorStyle("КРУГИ"))]);
        var document = new VectorPdfDocument([page]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, new DocumentLayoutPlanner().Create(document))));

        var circle = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Circle>());
        Assert.Equal(10d, circle.Center.X, 6);
        Assert.Equal(20d, circle.Center.Y, 6);
        Assert.Equal(5d, circle.Radius, 6);
        Assert.Empty(drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>());
    }

    [Fact]
    public void Writer_emits_editable_boundary_and_native_solid_hatch_for_filled_path()
    {
        var filled = new VectorFilledPath(
            "fill",
            [new Point2(0, 0), new Point2(10, 0), new Point2(10, 5), new Point2(0, 5)],
            VectorFillRule.NonZero,
            new VectorStyle("ЗАЛИВКА", 0x336699));
        var page = new VectorPdfPage(1, 72, 72, 0, [filled]);
        var document = new VectorPdfDocument([page]);
        var plan = new DocumentLayoutPlanner().Create(document);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, plan)));

        var boundary = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>());
        Assert.True(boundary.IsClosed);
        Assert.Equal(4, boundary.Vertices.Count);
        var hatch = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Hatch>());
        Assert.True(hatch.IsSolid);
        Assert.Equal("ЗАЛИВКА", hatch.Layer.Name);
        Assert.Equal(0x336699, hatch.Color.TrueColor);
    }

    [Fact]
    public void Writer_preserves_hole_boundaries_and_uses_an_interior_seed_for_concave_fill()
    {
        var filled = new VectorFilledPath(
            "concave-fill",
            [new Point2(0, 0), new Point2(12, 0), new Point2(12, 4), new Point2(4, 4), new Point2(4, 12), new Point2(0, 12)],
            VectorFillRule.EvenOdd,
            new VectorStyle(),
            InteriorBoundaries: [[new Point2(1, 1), new Point2(3, 1), new Point2(3, 3), new Point2(1, 3)]]);
        var page = new VectorPdfPage(1, 72, 72, 0, [filled]);
        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(new VectorPdfDocument([page]), new DocumentLayoutPlanner().Create(new VectorPdfDocument([page])))));

        Assert.Equal(2, drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>().Count());
        var hatch = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Hatch>());
        Assert.Equal(2, hatch.Paths.Count);
        var seed = Assert.Single(hatch.SeedPoints);
        Assert.InRange(seed.X, 0d, 4d);
        Assert.InRange(seed.Y, 4d, 12d);
    }

    [Fact]
    public void Writer_replaces_confirmed_parallel_source_lines_with_a_native_pattern_hatch()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorPolyline("boundary", [new(0, 0), new(20, 0), new(20, 20), new(0, 20)], true, new VectorStyle("ШТРИХОВКА")),
            new VectorLine("hatch-1", new(1, 4), new(19, 4), new VectorStyle()),
            new VectorLine("hatch-2", new(1, 8), new(19, 8), new VectorStyle()),
            new VectorLine("hatch-3", new(1, 12), new(19, 12), new VectorStyle())
        ]);
        var document = new VectorPdfDocument([page]);

        var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(document, new DocumentLayoutPlanner().Create(document))));

        Assert.Empty(drawing.Entities.OfType<ACadSharp.Entities.Line>());
        Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>());
        var hatch = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Hatch>());
        Assert.False(hatch.IsSolid);
        Assert.Equal(ACadSharp.Entities.HatchPatternType.Custom, hatch.PatternType);
        Assert.Single(hatch.Pattern.Lines);
        Assert.Equal("ШТРИХОВКА", hatch.Layer.Name);
    }
}
