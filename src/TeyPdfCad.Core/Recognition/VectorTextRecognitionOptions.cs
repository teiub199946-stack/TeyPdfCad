namespace TeyPdfCad.Core.Recognition;

public sealed record VectorTextRecognitionOptions
{
    /// <summary>Templates are intentionally explicit so an SHX font is never guessed.</summary>
    public IReadOnlyList<VectorGlyphTemplate> Templates { get; init; }
        = VectorGlyphTemplates.SevenSegmentDigits;

    /// <summary>Endpoint join tolerance in drawing units.</summary>
    public double EndpointJoinTolerance { get; init; } = 1e-3;

    /// <summary>Maximum normalized endpoint error for an individual stroke.</summary>
    public double StrokeMatchTolerance { get; init; } = 0.08;

    /// <summary>Minimum template match score accepted for a glyph.</summary>
    public double MinGlyphConfidence { get; init; } = 0.90;

    /// <summary>Maximum baseline deviation relative to glyph height.</summary>
    public double BaselineToleranceHeightMultiplier { get; init; } = 0.35;

    /// <summary>Maximum gap between adjacent glyphs relative to glyph height.</summary>
    public double CharacterGapHeightMultiplier { get; init; } = 1.5;

    /// <summary>Maximum difference between glyph baseline directions inside one text run.</summary>
    public double MaxRunRotationDifferenceRadians { get; init; } = 7.0 * Math.PI / 180.0;

    /// <summary>
    /// Prefer grouping segments emitted from the same PDFIMPORT polyline handle before
    /// falling back to endpoint-connected components.
    /// </summary>
    public bool UsePdfImportProvenanceGrouping { get; init; } = true;

    /// <summary>Hard safety cap for one glyph candidate.</summary>
    public int MaxGlyphStrokeCount { get; init; } = 64;

    public bool IsValid
        => EndpointJoinTolerance >= 0
            && StrokeMatchTolerance > 0
            && StrokeMatchTolerance < 1
            && MinGlyphConfidence is >= 0 and <= 1
            && BaselineToleranceHeightMultiplier >= 0
            && CharacterGapHeightMultiplier >= 0
            && MaxRunRotationDifferenceRadians >= 0
            && MaxRunRotationDifferenceRadians <= Math.PI / 2.0
            && MaxGlyphStrokeCount >= 1
            && Templates is not null;
}
