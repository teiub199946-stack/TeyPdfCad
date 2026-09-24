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

public sealed record PaintOrderKey(
    int PageNumber,
    string SourceOrCandidateKey,
    string Role,
    PaintPriority Priority,
    int StableOrdinal);

public sealed record PaintOrderItem<T>(
    T Item,
    PaintOrderKey Key);

public static class PaintOrderEngine
{
    // Contract: bottom-to-top ordering depends only on immutable identity fields.
    // Input enumeration order is never a tie-breaker.
    public static IReadOnlyList<PaintOrderItem<T>> OrderBottomToTop<T>(
        IEnumerable<PaintOrderItem<T>> items)
        => items
            .OrderBy(item => item.Key.Priority)
            .ThenBy(item => item.Key.PageNumber)
            .ThenBy(item => item.Key.SourceOrCandidateKey, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Key.StableOrdinal)
            .ToArray();
}
