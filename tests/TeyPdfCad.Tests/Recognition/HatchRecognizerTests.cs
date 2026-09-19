using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class HatchRecognizerTests
{
    [Fact]
    public void Three_regular_parallel_lines_inside_closed_boundary_form_pattern_candidate()
    {
        var result = new HatchRecognizer().Recognize(RectangleWithHorizontalLines([4, 8, 12]));

        var hatch = Assert.Single(result.NativeHatches, candidate => !candidate.IsSolid);
        Assert.Equal(0d, hatch.PatternAngleRadians!.Value, 6);
        Assert.Equal(4d, hatch.PatternSpacingMillimetres!.Value, 6);
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

    private static VectorEntity[] RectangleWithHorizontalLines(IReadOnlyList<double> yCoordinates)
        => [
            new VectorPolyline(
                "boundary",
                [new Point2(0, 0), new Point2(20, 0), new Point2(20, 20), new Point2(0, 20)],
                true,
                new VectorStyle()),
            ..yCoordinates.Select((y, index) => new VectorLine(
                $"hatch-line-{index}",
                new Point2(1, y),
                new Point2(19, y),
                new VectorStyle()))
        ];
}
