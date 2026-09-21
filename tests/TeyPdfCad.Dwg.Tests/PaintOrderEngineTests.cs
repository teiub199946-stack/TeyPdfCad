using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class PaintOrderEngineTests
{
    [Fact]
    public void Order_is_deterministic_by_priority_then_insertion_then_source_key()
    {
        var items = new[]
        {
            new PaintOrderItem<string>("text", PaintPriority.Text, 3, "T"),
            new PaintOrderItem<string>("hatch", PaintPriority.PatternHatch, 1, "H"),
            new PaintOrderItem<string>("geometry-b", PaintPriority.BaseGeometry, 2, "B"),
            new PaintOrderItem<string>("geometry-a", PaintPriority.BaseGeometry, 2, "A")
        };

        var ordered = PaintOrderEngine.OrderBottomToTop(items);

        Assert.Equal(
            new[] { "hatch", "geometry-a", "geometry-b", "text" },
            ordered.Select(item => item.Item));
    }

    [Fact]
    public void Input_order_does_not_change_result_when_sort_keys_match()
    {
        var items = new[]
        {
            new PaintOrderItem<string>("a", PaintPriority.Dimension, 7, "A"),
            new PaintOrderItem<string>("b", PaintPriority.Dimension, 7, "B"),
            new PaintOrderItem<string>("c", PaintPriority.Text, 8, "C")
        };

        var first = PaintOrderEngine.OrderBottomToTop(items).Select(item => item.Item).ToArray();
        var reversedInput = items.AsEnumerable().Reverse().ToArray();
        var second = PaintOrderEngine.OrderBottomToTop<string>(reversedInput).Select(item => item.Item).ToArray();

        Assert.Equal<string>(first, second);
    }
}
