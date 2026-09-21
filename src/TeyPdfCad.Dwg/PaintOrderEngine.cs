namespace TeyPdfCad.Dwg;

public enum PaintPriority
{
    BackgroundMask = 10,
    SolidFill = 20,
    PatternHatch = 30,
    BaseGeometry = 40,
    PreservedBoundary = 50,
    Axis = 60,
    Dimension = 70,
    Annotation = 80,
    Text = 90,
    ReviewOverlay = 100
}

public sealed record PaintOrderItem<T>(
    T Item,
    PaintPriority Priority,
    int InsertionIndex,
    string SourceIdOrdinal);

public static class PaintOrderEngine
{
    // Contract: bottom-to-top order is determined only by
    // (paintPriority, insertionIndex, sourceIdOrdinal).
    public static IReadOnlyList<PaintOrderItem<T>> OrderBottomToTop<T>(
        IEnumerable<PaintOrderItem<T>> items)
        => items
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.InsertionIndex)
            .ThenBy(item => item.SourceIdOrdinal, StringComparer.Ordinal)
            .ToArray();
}
