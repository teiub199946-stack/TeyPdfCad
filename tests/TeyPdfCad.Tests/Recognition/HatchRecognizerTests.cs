using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class HatchRecognizerTests
{
    [Fact]
    public void Three_regular_parallel_lines_need_strong_evidence_before_native_pattern_hatch()
    {
        var result = new HatchRecognizer().Recognize(RectangleWithHorizontalLines([4, 8, 12]));

        Assert.DoesNotContain(result.NativeHatches, candidate => !candidate.IsSolid);
        var claim = Assert.Single(result.Claims);
        Assert.Equal(HatchClassification.Uncertain, claim.Classification);
        Assert.Contains(result.Warnings, warning => warning.Code == "hatch-uncertain");
    }

    [Fact]
    public void Strong_hatch_layer_evidence_allows_confident_pattern_candidate()
    {
        var result = new HatchRecognizer().Recognize(RectangleWithHorizontalLines([4, 8, 12], "ШТРИХОВКА"));

        var hatch = Assert.Single(result.NativeHatches, candidate => !candidate.IsSolid);
        Assert.Equal(0d, hatch.PatternAngleRadians!.Value, 6);
        Assert.Equal(4d, hatch.PatternSpacingMillimetres!.Value, 6);
        var claim = Assert.Single(result.Claims);
        Assert.Equal(HatchClassification.Confident, claim.Classification);
        Assert.Equal("boundary", Assert.Single(claim.BoundarySourceIds));
        Assert.Equal(3, claim.PatternSourceIds.Count);
    }

    [Fact]
    public void Irregular_or_text_interrupted_lines_do_not_form_pattern_candidate()
    {
        var irregular = new HatchRecognizer().Recognize(RectangleWithHorizontalLines([4, 7, 15]));
        var withText = new HatchRecognizer().Recognize([
            ..RectangleWithHorizontalLines([4, 8, 12]),
            new VectorText("note", "бетон", new Point2(10, 8), 2.5, new VectorStyle())
        ]);

        Assert.DoesNotContain(irregular.NativeHatches, candidate => !candidate.IsSolid);
        Assert.DoesNotContain(withText.NativeHatches, candidate => !candidate.IsSolid);
        Assert.Contains(irregular.Warnings, warning => warning.Code == "hatch-low-confidence");
        Assert.Contains(withText.Warnings, warning => warning.Code == "hatch-low-confidence");
    }

    [Fact]
    public void Closed_solid_fill_is_a_native_solid_hatch_candidate()
    {
        var filled = new VectorFilledPath(
            "fill-1",
            [new Point2(0, 0), new Point2(20, 0), new Point2(20, 10), new Point2(0, 10)],
            VectorFillRule.NonZero,
            new VectorStyle(RgbColor: 0x336699));

        var result = new HatchRecognizer().Recognize([filled]);

        var hatch = Assert.Single(result.NativeHatches);
        Assert.True(hatch.IsSolid);
        Assert.Equal(0x336699, hatch.Style.RgbColor);
    }


    [Fact]
    public void Collinear_segmented_axis_does_not_trigger_hatch_low_confidence()
    {
        var lines = new VectorEntity[]
        {
            new VectorLine("axis-1", new Point2(0, 0), new Point2(25, 0), new VectorStyle()),
            new VectorLine("axis-2", new Point2(30, 0), new Point2(55, 0), new VectorStyle()),
            new VectorLine("axis-3", new Point2(60, 0), new Point2(64, 0), new VectorStyle())
        };

        var result = new HatchRecognizer().Recognize(lines);

        Assert.Empty(result.NativeHatches);
        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Code is "hatch-low-confidence" or "hatch-uncertain");
    }

    [Fact]
    public void Parallel_lines_without_a_closed_boundary_stay_geometry()
    {
        var lines = new VectorEntity[]
        {
            new VectorLine("line-1", new Point2(0, 0), new Point2(20, 0), new VectorStyle()),
            new VectorLine("line-2", new Point2(0, 5), new Point2(20, 5), new VectorStyle()),
            new VectorLine("line-3", new Point2(0, 10), new Point2(20, 10), new VectorStyle())
        };

        var result = new HatchRecognizer().Recognize(lines);

        Assert.Empty(result.NativeHatches);
        Assert.Contains(result.Warnings, warning => warning.Code == "hatch-low-confidence");
    }

    [Fact]
    public void Lines_crossing_a_boundary_are_not_mistaken_for_a_hatch()
    {
        var result = new HatchRecognizer().Recognize([
            new VectorPolyline(
                "boundary",
                [new Point2(0, 0), new Point2(20, 0), new Point2(20, 20), new Point2(0, 20)],
                true,
                new VectorStyle()),
            new VectorLine("inside-1", new Point2(1, 8), new Point2(19, 8), new VectorStyle()),
            new VectorLine("inside-2", new Point2(1, 12), new Point2(19, 12), new VectorStyle()),
            new VectorLine("crossing", new Point2(-5, 16), new Point2(25, 16), new VectorStyle())
        ]);

        Assert.DoesNotContain(result.NativeHatches, candidate => !candidate.IsSolid);
        Assert.Contains(result.Warnings, warning => warning.Code == "hatch-low-confidence");
    }

    [Fact]
    public void Very_large_pages_skip_quadratic_pattern_recognition_without_losing_geometry()
    {
        var entities = Enumerable.Range(0, 2_001)
            .Select(index => (VectorEntity)new VectorLine($"line-{index}", new Point2(0, index), new Point2(10, index), new VectorStyle()))
            .Append(new VectorPolyline("boundary", [new(0, 0), new(20, 0), new(20, 3000), new(0, 3000)], true, new VectorStyle()))
            .ToArray();

        var result = new HatchRecognizer().Recognize(entities);

        Assert.Empty(result.NativeHatches);
        Assert.Contains(result.Warnings, warning => warning.Code == "hatch-recognition-skipped-complexity");
    }

    [Fact]
    public void Nested_boundaries_do_not_duplicate_the_same_pattern_lines()
    {
        var result = new HatchRecognizer().Recognize([
            new VectorPolyline("outer", [new(0, 0), new(30, 0), new(30, 30), new(0, 30)], true, new VectorStyle()),
            new VectorPolyline("inner", [new(5, 5), new(25, 5), new(25, 25), new(5, 25)], true, new VectorStyle("ШТРИХОВКА")),
            new VectorLine("line-1", new(6, 10), new(24, 10), new VectorStyle()),
            new VectorLine("line-2", new(6, 15), new(24, 15), new VectorStyle()),
            new VectorLine("line-3", new(6, 20), new(24, 20), new VectorStyle())
        ]);

        Assert.Single(result.NativeHatches, candidate => !candidate.IsSolid);
        Assert.Contains(result.NativeHatches, candidate => candidate.ProvenanceIds[0] == "inner");
    }

    private static VectorEntity[] RectangleWithHorizontalLines(
        IReadOnlyList<double> yCoordinates,
        string? sourceLayer = null)
        => [
            new VectorPolyline(
                "boundary",
                [new Point2(0, 0), new Point2(20, 0), new Point2(20, 20), new Point2(0, 20)],
                true,
                new VectorStyle(sourceLayer)),
            ..yCoordinates.Select((y, index) => new VectorLine(
                $"hatch-line-{index}",
                new Point2(1, y),
                new Point2(19, y),
                new VectorStyle()))
        ];
}
