using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class PaintOrderEngineTests
{
    [Fact]
    public void Order_is_deterministic_by_immutable_paint_key()
    {
        var items = new[]
        {
            Item("text", 2, "T", "text", PaintPriority.Text, 0),
            Item("hatch", 1, "H", "primary", PaintPriority.PatternHatch, 0),
            Item("geometry-b", 1, "B", "source", PaintPriority.BaseGeometry, 0),
            Item("geometry-a", 1, "A", "source", PaintPriority.BaseGeometry, 0)
        };

        var ordered = PaintOrderEngine.OrderBottomToTop(items);

        Assert.Equal(
            new[] { "hatch", "geometry-a", "geometry-b", "text" },
            ordered.Select(item => item.Item));
    }

    [Fact]
    public void Reversing_input_enumeration_does_not_change_output()
    {
        var items = new[]
        {
            Item("a", 1, "candidate-a", "primary", PaintPriority.Dimension, 0),
            Item("b", 1, "candidate-b", "primary", PaintPriority.Dimension, 0),
            Item("c", 2, "candidate-c", "annotation", PaintPriority.Text, 0)
        };

        var first = PaintOrderEngine.OrderBottomToTop(items).Select(item => item.Item).ToArray();
        var second = PaintOrderEngine.OrderBottomToTop(items.Reverse()).Select(item => item.Item).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Stable_ordinal_orders_multiple_emissions_from_same_source_without_input_order()
    {
        var firstBoundary = Item(
            "boundary-0", 1, "fill-1", "source-fill-boundary", PaintPriority.BaseGeometry, 0);
        var secondBoundary = Item(
            "boundary-1", 1, "fill-1", "source-fill-boundary", PaintPriority.BaseGeometry, 1);

        var ordered = PaintOrderEngine.OrderBottomToTop(
            new[] { secondBoundary, firstBoundary });

        Assert.Equal(new[] { "boundary-0", "boundary-1" }, ordered.Select(item => item.Item));
    }

    [Fact]
    public void Page_number_is_a_deterministic_tie_breaker_before_source_key()
    {
        var items = new[]
        {
            Item("page-2", 2, "A", "source", PaintPriority.BaseGeometry, 0),
            Item("page-1", 1, "Z", "source", PaintPriority.BaseGeometry, 0)
        };

        var ordered = PaintOrderEngine.OrderBottomToTop(items);

        Assert.Equal(new[] { "page-1", "page-2" }, ordered.Select(item => item.Item));
    }

    private static PaintOrderItem<string> Item(
        string item,
        int page,
        string key,
        string role,
        PaintPriority priority,
        int ordinal)
        => new(
            item,
            new PaintOrderKey(page, key, role, priority, ordinal));
}
