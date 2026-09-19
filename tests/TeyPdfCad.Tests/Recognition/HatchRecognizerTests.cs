using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class HatchRecognizerTests
{
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
}
