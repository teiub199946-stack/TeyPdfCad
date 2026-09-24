using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;

namespace TeyPdfCad.Core.Recognition;

public sealed class SemanticReconstructionEngine
{
    private readonly LinearDimensionRecognizer _dimensionRecognizer = new();
    private readonly DimensionChainDetector _chainDetector = new();
    private readonly AxisRecognizer _axisRecognizer = new();
    private readonly LeaderRecognizer _leaderRecognizer = new();
    private readonly LevelRecognizer _levelRecognizer = new();
    private readonly ArcDimensionRecognizer _arcDimensionRecognizer = new();

    public SemanticReconstructionResult Analyze(
        PrimitiveScene scene,
        DimensionRecognitionOptions? dimensionOptions = null)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        var dimensionRecognition = _dimensionRecognizer.RecognizeDetailed(scene, dimensionOptions);
        var dimensions = dimensionRecognition.PrimaryCandidates;
        var dimensionWarnings = _dimensionRecognizer.DetectAmbiguities(scene, dimensionOptions);
        var chains = _chainDetector.Detect(dimensions);
        var dominantScale = EstimateDominantScale(dimensions);
        var averageConfidence = dimensions.Count == 0 ? 0.0 : dimensions.Average(x => x.Confidence);
        var axes = _axisRecognizer.Recognize(scene);
        var leaders = _leaderRecognizer.Recognize(scene);
        var levels = _levelRecognizer.Recognize(scene);
        var arcDimensions = _arcDimensionRecognizer.Recognize(scene);

        return new SemanticReconstructionResult(
            dimensions,
            chains,
            dominantScale,
            averageConfidence)
        {
            DimensionClaimants = dimensionRecognition.Claimants,
            Axes = axes.NativeAxes,
            Leaders = leaders.NativeLeaders,
            Levels = levels.NativeLevels,
            ArcDimensions = arcDimensions.NativeArcDimensions,
            NativeFillPaths = scene.ClosedPaths,
            Warnings = dimensionWarnings
                .Concat(axes.Warnings)
                .Concat(leaders.Warnings)
                .Concat(levels.Warnings)
                .Concat(arcDimensions.Warnings)
                .ToArray()
        };
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
