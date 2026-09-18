using TeyPdfCad.Core.Geometry;

namespace TeyPdfCad.Core.Recognition;

/// <summary>
/// A normalized single-character stroke template. Coordinates are expressed in
/// the unit square; the recognizer scales them to the candidate glyph bounds.
/// </summary>
public sealed record VectorGlyphTemplate
{
    public VectorGlyphTemplate(string value, IEnumerable<VectorGlyphTemplateStroke> strokes)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Strokes = strokes?.ToArray() ?? throw new ArgumentNullException(nameof(strokes));
    }

    public string Value { get; }
    public IReadOnlyList<VectorGlyphTemplateStroke> Strokes { get; }

    public bool IsUsable
        => Value.Length == 1
            && Strokes.Count > 0
            && Strokes.All(stroke => stroke.IsFinite && stroke.Length > 0);
}

public readonly record struct VectorGlyphTemplateStroke(Point2 Start, Point2 End)
{
    public double Length => Math.Sqrt(
        (End.X - Start.X) * (End.X - Start.X)
        + (End.Y - Start.Y) * (End.Y - Start.Y));

    public bool IsFinite
        => IsFiniteCoordinate(Start.X)
            && IsFiniteCoordinate(Start.Y)
            && IsFiniteCoordinate(End.X)
            && IsFiniteCoordinate(End.Y);

    private static bool IsFiniteCoordinate(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>
/// Conservative built-in templates for test/control drawings that use a
/// seven-segment vector digit convention. Production SHX fonts must provide
/// explicit templates instead of silently using this fallback.
/// </summary>
public static class VectorGlyphTemplates
{
    public static IReadOnlyList<VectorGlyphTemplate> SevenSegmentDigits { get; } =
        CreateSevenSegmentDigits();

    private static IReadOnlyList<VectorGlyphTemplate> CreateSevenSegmentDigits()
    {
        var a = new VectorGlyphTemplateStroke(new Point2(0.1, 1), new Point2(0.9, 1));
        var b = new VectorGlyphTemplateStroke(new Point2(0.9, 1), new Point2(0.9, 0.5));
        var c = new VectorGlyphTemplateStroke(new Point2(0.9, 0.5), new Point2(0.9, 0));
        var d = new VectorGlyphTemplateStroke(new Point2(0.1, 0), new Point2(0.9, 0));
        var e = new VectorGlyphTemplateStroke(new Point2(0.1, 0), new Point2(0.1, 0.5));
        var f = new VectorGlyphTemplateStroke(new Point2(0.1, 0.5), new Point2(0.1, 1));
        var g = new VectorGlyphTemplateStroke(new Point2(0.1, 0.5), new Point2(0.9, 0.5));

        var segments = new[] { a, b, c, d, e, f, g };
        var masks = new Dictionary<char, int>
        {
            ['0'] = 0b0111111,
            ['1'] = 0b0000110,
            ['2'] = 0b1011011,
            ['3'] = 0b1001111,
            ['4'] = 0b1100110,
            ['5'] = 0b1101101,
            ['6'] = 0b1111101,
            ['7'] = 0b0000111,
            ['8'] = 0b1111111,
            ['9'] = 0b1101111,
        };

        return masks
            .OrderBy(pair => pair.Key)
            .Select(pair => new VectorGlyphTemplate(
                pair.Key.ToString(),
                segments.Where((_, index) => (pair.Value & (1 << index)) != 0)))
            .ToArray();
    }
}
