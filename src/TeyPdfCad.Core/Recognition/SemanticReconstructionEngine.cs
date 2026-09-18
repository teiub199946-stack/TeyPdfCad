using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public sealed class SemanticReconstructionEngine
{
    private readonly LinearDimensionRecognizer _dimensionRecognizer = new();
    private readonly DimensionChainDetector _chainDetector = new();
    private readonly VectorTextRecognizer _vectorTextRecognizer = new();

    public SemanticReconstructionResult Analyze(
        PrimitiveScene scene,
        DimensionRecognitionOptions? dimensionOptions = null,
        VectorTextRecognitionOptions? vectorTextOptions = null)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        var analysisScene = vectorTextOptions is null
            ? scene
            : BuildVectorTextAugmentedScene(scene, vectorTextOptions);

        var dimensions = _dimensionRecognizer.Recognize(analysisScene, dimensionOptions);
        var chains = _chainDetector.Detect(dimensions);
        var dominantScale = EstimateDominantScale(dimensions);
        var averageConfidence = dimensions.Count == 0 ? 0.0 : dimensions.Average(x => x.Confidence);

        return new SemanticReconstructionResult(
            dimensions,
            chains,
            dominantScale,
            averageConfidence);
    }

    private PrimitiveScene BuildVectorTextAugmentedScene(
        PrimitiveScene source,
        VectorTextRecognitionOptions options)
    {
        var vectorText = _vectorTextRecognizer.Analyze(source, options);
        if (vectorText.Texts.Count == 0)
            return source;

        var consumedSourceIds = new HashSet<string>(
            vectorText.Texts.SelectMany(text => text.ProvenanceIds),
            StringComparer.Ordinal);

        var augmented = new PrimitiveScene();

        foreach (var line in source.Lines)
        {
            if (!HasAnyProvenance(line.ProvenanceIds, consumedSourceIds))
                augmented.Lines.Add(line);
        }

        augmented.Texts.AddRange(source.Texts);

        foreach (var recognized in vectorText.Texts)
        {
            if (!ContainsEquivalentText(augmented.Texts, recognized))
                augmented.Texts.Add(recognized);
        }

        return augmented;
    }

    private static bool HasAnyProvenance(
        IReadOnlyList<string> provenanceIds,
        HashSet<string> consumedSourceIds)
    {
        if (provenanceIds.Count == 0 || consumedSourceIds.Count == 0)
            return false;

        return provenanceIds.Any(consumedSourceIds.Contains);
    }

    private static bool ContainsEquivalentText(
        IReadOnlyList<TextPrimitive> existing,
        TextPrimitive candidate)
    {
        foreach (var text in existing)
        {
            if (!string.Equals(text.Value, candidate.Value, StringComparison.Ordinal))
                continue;

            if (HasSharedProvenance(text, candidate))
                return true;

            var tolerance = Math.Max(Math.Max(text.Height, candidate.Height) * 0.25, 1e-6);
            var dx = text.Position.X - candidate.Position.X;
            var dy = text.Position.Y - candidate.Position.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= tolerance)
                return true;
        }

        return false;
    }

    private static bool HasSharedProvenance(TextPrimitive left, TextPrimitive right)
    {
        if (left.ProvenanceIds.Count == 0 || right.ProvenanceIds.Count == 0)
            return false;

        var ids = new HashSet<string>(left.ProvenanceIds, StringComparer.Ordinal);
        return right.ProvenanceIds.Any(ids.Contains);
    }

    private static double? EstimateDominantScale(IReadOnlyList<DimensionCandidate> dimensions)
    {
        if (dimensions.Count == 0) return null;

        return dimensions
            .GroupBy(x => Math.Round(x.DrawingScale, 6))
            .OrderByDescending(group => group.Sum(x => x.Confidence))
            .ThenByDescending(group => group.Count())
            .Select(group => (double?)group.Key)
            .FirstOrDefault();
    }
}
