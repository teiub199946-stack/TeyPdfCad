using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;

namespace TeyPdfCad.Core.Recognition;

/// <summary>
/// Reads explicit vector stroke templates upstream of semantic reconstruction.
/// It never changes the source scene and fails closed when any character in a
/// candidate run is unknown or ambiguous.
/// </summary>
public sealed class VectorTextRecognizer
{
    public IReadOnlyList<TextPrimitive> Recognize(
        PrimitiveScene scene,
        VectorTextRecognitionOptions? options = null)
        => Analyze(scene, options).Texts;

    public VectorTextRecognitionResult Analyze(
        PrimitiveScene scene,
        VectorTextRecognitionOptions? options = null)
    {
        if (scene is null) throw new ArgumentNullException(nameof(scene));

        options ??= new VectorTextRecognitionOptions();
        if (!options.IsValid)
            throw new ArgumentException("Vector text recognition options are invalid.", nameof(options));

        var usableTemplates = options.Templates
            .Where(template => template is not null && template.IsUsable)
            .ToArray();
        if (usableTemplates.Length == 0)
            return new VectorTextRecognitionResult([], []);

        var components = BuildConnectedComponents(scene.Lines, options.EndpointJoinTolerance);
        var candidates = new List<GlyphCandidate>();
        var rejections = new List<VectorTextRejection>();

        foreach (var component in components)
        {
            if (TryRecognizeGlyph(component, usableTemplates, options, out var candidate))
            {
                candidates.Add(candidate);
                continue;
            }

            if (LooksLikeGlyphCandidate(component))
                rejections.Add(CreateRejection("no-template-match", component));
        }

        var texts = BuildTextRuns(candidates, options);
        return new VectorTextRecognitionResult(texts, rejections);
    }

    private static IReadOnlyList<TextPrimitive> BuildTextRuns(
        IReadOnlyList<GlyphCandidate> candidates,
        VectorTextRecognitionOptions options)
    {
        if (candidates.Count == 0) return [];

        var pending = candidates
            .OrderBy(candidate => candidate.Bounds.MinY)
            .ThenBy(candidate => candidate.Bounds.MinX)
            .ToList();
        var texts = new List<TextPrimitive>();

        while (pending.Count > 0)
        {
            var seed = pending[0];
            pending.RemoveAt(0);
            var run = new List<GlyphCandidate> { seed };

            var changed = true;
            while (changed)
            {
                changed = false;
                for (var index = pending.Count - 1; index >= 0; index--)
                {
                    var candidate = pending[index];
                    if (!BelongsToRun(candidate, run, options)) continue;

                    run.Add(candidate);
                    pending.RemoveAt(index);
                    changed = true;
                }
            }

            run.Sort((left, right) => left.Bounds.MinX.CompareTo(right.Bounds.MinX));
            var value = string.Concat(run.Select(candidate => candidate.Value));
            var minX = run.Min(candidate => candidate.Bounds.MinX);
            var minY = run.Min(candidate => candidate.Bounds.MinY);
            var maxX = run.Max(candidate => candidate.Bounds.MaxX);
            var maxY = run.Max(candidate => candidate.Bounds.MaxY);
            var sourceIds = run
                .SelectMany(candidate => candidate.SourcePrimitiveIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            texts.Add(new TextPrimitive(
                value,
                new Point2((minX + maxX) / 2.0, (minY + maxY) / 2.0),
                maxY - minY,
                0,
                run.Select(candidate => candidate.Layer).Distinct(StringComparer.Ordinal).Count() == 1
                    ? run[0].Layer
                    : null,
                sourceIds));
        }

        return texts
            .OrderBy(text => text.Position.Y)
            .ThenBy(text => text.Position.X)
            .ToArray();
    }

    private static bool BelongsToRun(
        GlyphCandidate candidate,
        IReadOnlyList<GlyphCandidate> run,
        VectorTextRecognitionOptions options)
    {
        var candidateHeight = candidate.Bounds.Height;
        foreach (var existing in run)
        {
            var height = Math.Min(candidateHeight, existing.Bounds.Height);
            if (height <= 1e-12) continue;

            var centerDelta = Math.Abs(candidate.Bounds.CenterY - existing.Bounds.CenterY);
            if (centerDelta > height * options.BaselineToleranceHeightMultiplier) continue;

            var gap = HorizontalGap(candidate.Bounds, existing.Bounds);
            if (gap <= height * options.CharacterGapHeightMultiplier)
                return true;
        }
        return false;
    }

    private static double HorizontalGap(GlyphBounds left, GlyphBounds right)
    {
        if (left.MaxX < right.MinX) return right.MinX - left.MaxX;
        if (right.MaxX < left.MinX) return left.MinX - right.MaxX;
        return 0;
    }

    private static bool TryRecognizeGlyph(
        IReadOnlyList<LinePrimitive> component,
        IReadOnlyList<VectorGlyphTemplate> templates,
        VectorTextRecognitionOptions options,
        out GlyphCandidate candidate)
    {
        candidate = default;
        var bounds = GetBounds(component);
        if (bounds.Height <= 1e-12) return false;
        if (component.Count > 32) return false;

        var normalized = component
            .Select(line => Normalize(line, bounds))
            .ToArray();
        var best = default(TemplateMatch?);
        foreach (var template in templates)
        {
            if (template.Strokes.Count != normalized.Length) continue;
            var match = MatchTemplate(normalized, template.Strokes, options.StrokeMatchTolerance);
            if (!match.HasValue) continue;
            if (best is null || match.Value.Score > best.Value.Score)
                best = new TemplateMatch(template, match.Value.Score);
        }

        if (best is null || best.Value.Score < options.MinGlyphConfidence)
            return false;

        var sourceIds = component
            .SelectMany(line => line.ProvenanceIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var layer = component
            .Select(line => line.Layer)
            .Distinct(StringComparer.Ordinal)
            .Count() == 1
            ? component[0].Layer
            : null;

        candidate = new GlyphCandidate(
            best.Value.Template.Value,
            best.Value.Score,
            bounds,
            layer,
            sourceIds);
        return true;
    }

    private static TemplateStrokeMatch? MatchTemplate(
        IReadOnlyList<VectorGlyphTemplateStroke> candidate,
        IReadOnlyList<VectorGlyphTemplateStroke> template,
        double tolerance)
    {
        var used = new bool[template.Count];
        var assignment = new int[candidate.Count];
        for (var index = 0; index < assignment.Length; index++) assignment[index] = -1;
        var templateBounds = GetBounds(template);
        var normalizedTemplate = template
            .Select(stroke => Normalize(stroke, templateBounds))
            .ToArray();
        var bestCost = double.PositiveInfinity;
        Search(0, 0.0);

        if (double.IsInfinity(bestCost)) return null;
        var averageError = bestCost / candidate.Count;
        var score = 1.0 - Math.Min(1.0, averageError / tolerance);
        return new TemplateStrokeMatch(score);

        void Search(int index, double cost)
        {
            if (cost > bestCost) return;
            if (index == candidate.Count)
            {
                bestCost = cost;
                return;
            }

            for (var templateIndex = 0; templateIndex < template.Count; templateIndex++)
            {
                if (used[templateIndex]) continue;
                var error = StrokeError(candidate[index], normalizedTemplate[templateIndex]);
                if (error > tolerance) continue;
                used[templateIndex] = true;
                assignment[index] = templateIndex;
                Search(index + 1, cost + error);
                assignment[index] = -1;
                used[templateIndex] = false;
            }
        }
    }

    private static double StrokeError(
        VectorGlyphTemplateStroke candidate,
        VectorGlyphTemplateStroke template)
    {
        var direct = PointDistance(candidate.Start, template.Start)
            + PointDistance(candidate.End, template.End);
        var reversed = PointDistance(candidate.Start, template.End)
            + PointDistance(candidate.End, template.Start);
        return Math.Min(direct, reversed) / 2.0;
    }

    private static VectorGlyphTemplateStroke Normalize(LinePrimitive line, GlyphBounds bounds)
    {
        return new VectorGlyphTemplateStroke(
            Normalize(line.Start, bounds),
            Normalize(line.End, bounds));
    }

    private static VectorGlyphTemplateStroke Normalize(
        VectorGlyphTemplateStroke stroke,
        GlyphBounds bounds)
        => new(Normalize(stroke.Start, bounds), Normalize(stroke.End, bounds));

    private static Point2 Normalize(Point2 point, GlyphBounds bounds)
        => new(
            bounds.Width <= 1e-12 ? 0.5 : (point.X - bounds.MinX) / bounds.Width,
            bounds.Height <= 1e-12 ? 0.5 : (point.Y - bounds.MinY) / bounds.Height);

    private static double PointDistance(Point2 left, Point2 right)
        => Math.Sqrt(
            (left.X - right.X) * (left.X - right.X)
            + (left.Y - right.Y) * (left.Y - right.Y));

    private static bool LooksLikeGlyphCandidate(IReadOnlyList<LinePrimitive> component)
    {
        var bounds = GetBounds(component);
        return component.Count is >= 2 and <= 16
            && bounds.Height > 1e-9
            && (bounds.Width <= 1e-9 || bounds.Width / bounds.Height is >= 0.15 and <= 4.0);
    }

    private static VectorTextRejection CreateRejection(
        string reason,
        IReadOnlyList<LinePrimitive> component)
    {
        var bounds = GetBounds(component);
        return new VectorTextRejection(
            reason,
            component
                .SelectMany(line => line.ProvenanceIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            new Point2(bounds.MinX, bounds.MinY),
            new Point2(bounds.MaxX, bounds.MaxY),
            component.Count);
    }

    private static IReadOnlyList<IReadOnlyList<LinePrimitive>> BuildConnectedComponents(
        IReadOnlyList<LinePrimitive> lines,
        double endpointTolerance)
    {
        var usable = lines
            .Where(line => IsFinite(line.Start) && IsFinite(line.End))
            .Where(line => PointDistance(line.Start, line.End) > 1e-12)
            .ToArray();
        var parent = Enumerable.Range(0, usable.Length).ToArray();

        for (var left = 0; left < usable.Length; left++)
        for (var right = left + 1; right < usable.Length; right++)
        {
            if (EndpointDistance(usable[left], usable[right]) <= endpointTolerance)
                Union(parent, left, right);
        }

        var groups = new Dictionary<int, List<LinePrimitive>>();
        for (var index = 0; index < usable.Length; index++)
        {
            var root = Find(parent, index);
            if (!groups.TryGetValue(root, out var group))
            {
                group = [];
                groups.Add(root, group);
            }
            group.Add(usable[index]);
        }

        return groups.Values
            .OrderBy(group => GetBounds(group).MinY)
            .ThenBy(group => GetBounds(group).MinX)
            .Cast<IReadOnlyList<LinePrimitive>>()
            .ToArray();
    }

    private static double EndpointDistance(LinePrimitive left, LinePrimitive right)
        => Math.Min(
            Math.Min(PointDistance(left.Start, right.Start), PointDistance(left.Start, right.End)),
            Math.Min(PointDistance(left.End, right.Start), PointDistance(left.End, right.End)));

    private static int Find(int[] parent, int value)
    {
        while (parent[value] != value)
        {
            parent[value] = parent[parent[value]];
            value = parent[value];
        }
        return value;
    }

    private static void Union(int[] parent, int left, int right)
    {
        var leftRoot = Find(parent, left);
        var rightRoot = Find(parent, right);
        if (leftRoot != rightRoot) parent[rightRoot] = leftRoot;
    }

    private static GlyphBounds GetBounds(IEnumerable<LinePrimitive> lines)
    {
        var points = lines.SelectMany(line => new[] { line.Start, line.End }).ToArray();
        return new GlyphBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static GlyphBounds GetBounds(IEnumerable<VectorGlyphTemplateStroke> strokes)
    {
        var points = strokes
            .SelectMany(stroke => new[] { stroke.Start, stroke.End })
            .ToArray();
        return new GlyphBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }

    private static bool IsFinite(Point2 point)
        => !double.IsNaN(point.X)
            && !double.IsInfinity(point.X)
            && !double.IsNaN(point.Y)
            && !double.IsInfinity(point.Y);

    private readonly record struct GlyphCandidate(
        string Value,
        double Confidence,
        GlyphBounds Bounds,
        string? Layer,
        IReadOnlyList<string> SourcePrimitiveIds);

    private readonly record struct TemplateMatch(VectorGlyphTemplate Template, double Score);

    private readonly record struct TemplateStrokeMatch(double Score);

    private readonly record struct GlyphBounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double CenterY => (MinY + MaxY) / 2.0;
    }
}
