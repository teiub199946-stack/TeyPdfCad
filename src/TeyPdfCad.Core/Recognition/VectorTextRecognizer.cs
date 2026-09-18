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

        var usableLines = scene.Lines
            .Select((line, index) => new IndexedLine(index, line))
            .Where(item => IsFinite(item.Line.Start) && IsFinite(item.Line.End))
            .Where(item => PointDistance(item.Line.Start, item.Line.End) > 1e-12)
            .ToArray();

        var recognized = new List<GlyphCandidate>();
        var consumed = new HashSet<int>();

        if (options.UsePdfImportProvenanceGrouping)
        {
            foreach (var group in BuildPdfImportSourceGroups(usableLines))
            {
                if (!TryRecognizeGlyph(
                        group.Select(item => item.Line).ToArray(),
                        usableTemplates,
                        options,
                        out var candidate))
                    continue;

                recognized.Add(candidate);
                foreach (var item in group)
                    consumed.Add(item.Index);
            }
        }

        var rejections = new List<VectorTextRejection>();
        var remaining = usableLines.Where(item => !consumed.Contains(item.Index)).ToArray();
        foreach (var component in BuildConnectedComponents(remaining, options.EndpointJoinTolerance))
        {
            var lines = component.Select(item => item.Line).ToArray();
            if (TryRecognizeGlyph(lines, usableTemplates, options, out var candidate))
            {
                recognized.Add(candidate);
                continue;
            }

            if (LooksLikeGlyphCandidate(lines))
                rejections.Add(CreateRejection("no-template-match", lines));
        }

        var texts = BuildTextRuns(recognized, options);
        return new VectorTextRecognitionResult(texts, rejections);
    }

    private static IReadOnlyList<IReadOnlyList<IndexedLine>> BuildPdfImportSourceGroups(
        IReadOnlyList<IndexedLine> lines)
    {
        var groups = new Dictionary<string, List<IndexedLine>>(StringComparer.Ordinal);

        foreach (var item in lines)
        {
            var key = GetPdfImportSourceObjectKey(item.Line.ProvenanceIds);
            if (key is null) continue;

            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(item);
        }

        return groups
            .Where(pair => pair.Value.Count >= 2)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (IReadOnlyList<IndexedLine>)pair.Value
                .OrderBy(item => item.Index)
                .ToArray())
            .ToArray();
    }

    private static string? GetPdfImportSourceObjectKey(IReadOnlyList<string> sourceIds)
    {
        string? key = null;
        foreach (var sourceId in sourceIds)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) continue;

            var marker = sourceId.IndexOf("#segment:", StringComparison.Ordinal);
            if (marker <= 0) continue;

            var current = sourceId[..marker];
            if (key is null)
            {
                key = current;
                continue;
            }

            if (!string.Equals(key, current, StringComparison.Ordinal))
                return null;
        }

        return key;
    }

    private static IReadOnlyList<TextPrimitive> BuildTextRuns(
        IReadOnlyList<GlyphCandidate> candidates,
        VectorTextRecognitionOptions options)
    {
        if (candidates.Count == 0) return [];

        var pending = candidates
            .OrderBy(candidate => candidate.Center.Y)
            .ThenBy(candidate => candidate.Center.X)
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

            var runRotation = ChooseDirectedRunRotation(run);
            var axisX = Math.Cos(runRotation);
            var axisY = Math.Sin(runRotation);
            var normalX = -axisY;
            var normalY = axisX;

            run.Sort((left, right) =>
            {
                var leftProjection = Dot(left.Center, axisX, axisY);
                var rightProjection = Dot(right.Center, axisX, axisY);
                return leftProjection.CompareTo(rightProjection);
            });

            var minAlong = double.PositiveInfinity;
            var maxAlong = double.NegativeInfinity;
            var minNormal = double.PositiveInfinity;
            var maxNormal = double.NegativeInfinity;

            foreach (var glyph in run)
            {
                var centerAlong = Dot(glyph.Center, axisX, axisY);
                var centerNormal = Dot(glyph.Center, normalX, normalY);
                minAlong = Math.Min(minAlong, centerAlong - glyph.Width / 2.0);
                maxAlong = Math.Max(maxAlong, centerAlong + glyph.Width / 2.0);
                minNormal = Math.Min(minNormal, centerNormal - glyph.Height / 2.0);
                maxNormal = Math.Max(maxNormal, centerNormal + glyph.Height / 2.0);
            }

            var alongCenter = (minAlong + maxAlong) / 2.0;
            var normalCenter = (minNormal + maxNormal) / 2.0;
            var position = new Point2(
                alongCenter * axisX + normalCenter * normalX,
                alongCenter * axisY + normalCenter * normalY);
            var value = string.Concat(run.Select(candidate => candidate.Value));
            var sourceIds = run
                .SelectMany(candidate => candidate.SourcePrimitiveIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            texts.Add(new TextPrimitive(
                value,
                position,
                maxNormal - minNormal,
                NormalizeAngle(runRotation),
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
        foreach (var existing in run)
        {
            if (AngleDistanceModuloPi(candidate.Rotation, existing.Rotation)
                > options.MaxRunRotationDifferenceRadians)
                continue;

            var axisRotation = MeanLineRotation(candidate.Rotation, existing.Rotation);
            var axisX = Math.Cos(axisRotation);
            var axisY = Math.Sin(axisRotation);
            var normalX = -axisY;
            var normalY = axisX;

            var deltaX = candidate.Center.X - existing.Center.X;
            var deltaY = candidate.Center.Y - existing.Center.Y;
            var along = Math.Abs(deltaX * axisX + deltaY * axisY);
            var perpendicular = Math.Abs(deltaX * normalX + deltaY * normalY);
            var height = Math.Min(candidate.Height, existing.Height);
            if (height <= 1e-12) continue;

            if (perpendicular > height * options.BaselineToleranceHeightMultiplier)
                continue;

            var gap = Math.Max(
                0,
                along - (candidate.Width + existing.Width) / 2.0);
            if (gap <= height * options.CharacterGapHeightMultiplier)
                return true;
        }

        return false;
    }

    private static double ChooseDirectedRunRotation(IReadOnlyList<GlyphCandidate> run)
    {
        var lineRotation = MeanLineRotation(run.Select(candidate => candidate.Rotation));
        var opposite = NormalizeAngle(lineRotation + Math.PI);
        var forwardCost = 0.0;
        var oppositeCost = 0.0;

        foreach (var candidate in run)
        {
            var weight = Math.Max(candidate.Confidence, 1e-6);
            forwardCost += weight * AngleDistance(candidate.Rotation, lineRotation);
            oppositeCost += weight * AngleDistance(candidate.Rotation, opposite);
        }

        if (oppositeCost + 1e-10 < forwardCost)
            return opposite;

        if (Math.Abs(oppositeCost - forwardCost) <= 1e-10)
            return PreferRotation(lineRotation, opposite);

        return lineRotation;
    }

    private static double MeanLineRotation(double left, double right)
        => MeanLineRotation(new[] { left, right });

    private static double MeanLineRotation(IEnumerable<double> rotations)
    {
        var x = 0.0;
        var y = 0.0;
        foreach (var rotation in rotations)
        {
            x += Math.Cos(2.0 * rotation);
            y += Math.Sin(2.0 * rotation);
        }

        if (Math.Abs(x) <= 1e-12 && Math.Abs(y) <= 1e-12)
            return 0;

        var angle = 0.5 * Math.Atan2(y, x);
        return NormalizeAngle(angle);
    }

    private static bool TryRecognizeGlyph(
        IReadOnlyList<LinePrimitive> component,
        IReadOnlyList<VectorGlyphTemplate> templates,
        VectorTextRecognitionOptions options,
        out GlyphCandidate candidate)
    {
        candidate = default;
        if (component.Count == 0 || component.Count > options.MaxGlyphStrokeCount)
            return false;

        var bestByValue = new Dictionary<string, OrientedTemplateMatch>(StringComparer.Ordinal);

        foreach (var template in templates)
        {
            if (template.Strokes.Count != component.Count)
                continue;

            var match = FindBestOrientedTemplateMatch(
                component,
                template,
                options.StrokeMatchTolerance);
            if (!match.HasValue)
                continue;

            if (!bestByValue.TryGetValue(template.Value, out var existing)
                || IsBetterMatch(match.Value, existing))
            {
                bestByValue[template.Value] = match.Value;
            }
        }

        if (bestByValue.Count == 0)
            return false;

        var ordered = bestByValue.Values
            .OrderByDescending(match => match.Score)
            .ThenBy(match => RotationPriority(match.Rotation))
            .ThenBy(match => match.Template.Value, StringComparer.Ordinal)
            .ToArray();

        var best = ordered[0];
        if (best.Score < options.MinGlyphConfidence)
            return false;

        var ambiguityWindow = ordered
            .Where(match => match.Score >= options.MinGlyphConfidence)
            .Where(match => best.Score - match.Score
                <= options.HalfTurnAmbiguityScoreTolerance + 1e-12)
            .ToArray();

        if (ambiguityWindow.Length > 1)
        {
            var upright = ambiguityWindow
                .OrderBy(match => RotationPriority(match.Rotation))
                .ThenByDescending(match => match.Score)
                .ThenBy(match => match.Template.Value, StringComparer.Ordinal)
                .First();

            var distinctCompetitors = ambiguityWindow
                .Where(match => !string.Equals(
                    match.Template.Value,
                    upright.Template.Value,
                    StringComparison.Ordinal))
                .ToArray();

            if (distinctCompetitors.Length > 0)
            {
                var canResolveAsHalfTurn = options.ResolveHalfTurnAmbiguityAsUpright
                    && distinctCompetitors.All(match =>
                        IsHalfTurnSeparated(upright.Rotation, match.Rotation));

                if (!canResolveAsHalfTurn)
                    return false;

                best = upright;
            }
        }

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
            best.Template.Value,
            best.Score,
            best.Center,
            best.Width,
            best.Height,
            best.Rotation,
            layer,
            sourceIds);
        return true;
    }

    private static OrientedTemplateMatch? FindBestOrientedTemplateMatch(
        IReadOnlyList<LinePrimitive> component,
        VectorGlyphTemplate template,
        double tolerance)
    {
        var hypotheses = BuildRotationHypotheses(component, template.Strokes);
        OrientedTemplateMatch? best = null;

        foreach (var rotation in hypotheses)
        {
            var evaluated = EvaluateAtRotation(component, template, rotation, tolerance);
            if (!evaluated.HasValue)
                continue;

            if (!best.HasValue || IsBetterMatch(evaluated.Value, best.Value))
                best = evaluated;
        }

        return best;
    }

    private static IReadOnlyList<double> BuildRotationHypotheses(
        IReadOnlyList<LinePrimitive> component,
        IReadOnlyList<VectorGlyphTemplateStroke> template)
    {
        var values = new List<double>();

        foreach (var candidateStroke in component)
        {
            var candidateAngle = StrokeAngle(candidateStroke.Start, candidateStroke.End);
            foreach (var templateStroke in template)
            {
                var templateAngle = StrokeAngle(templateStroke.Start, templateStroke.End);
                var baseRotation = NormalizeAngle(candidateAngle - templateAngle);
                AddAngleIfNew(values, baseRotation);
                AddAngleIfNew(values, NormalizeAngle(baseRotation + Math.PI));
            }
        }

        return values
            .OrderBy(RotationPriority)
            .ToArray();
    }

    private static void AddAngleIfNew(List<double> values, double angle)
    {
        foreach (var existing in values)
        {
            if (AngleDistance(existing, angle) <= 1e-8)
                return;
        }

        values.Add(angle);
    }

    private static OrientedTemplateMatch? EvaluateAtRotation(
        IReadOnlyList<LinePrimitive> component,
        VectorGlyphTemplate template,
        double rotation,
        double tolerance)
    {
        var origin = GetCentroid(component);
        var rotated = component
            .Select(line => new VectorGlyphTemplateStroke(
                RotateAround(line.Start, origin, -rotation),
                RotateAround(line.End, origin, -rotation)))
            .ToArray();
        var candidateBounds = GetBounds(rotated);
        if (candidateBounds.Height <= 1e-12 && candidateBounds.Width <= 1e-12)
            return null;

        var templateBounds = GetBounds(template.Strokes);
        var normalizedCandidate = rotated
            .Select(stroke => NormalizeCandidateForTemplate(
                stroke,
                candidateBounds,
                templateBounds))
            .ToArray();
        var match = MatchTemplate(
            normalizedCandidate,
            template.Strokes,
            tolerance);
        if (!match.HasValue)
            return null;

        var localCenter = new Point2(
            (candidateBounds.MinX + candidateBounds.MaxX) / 2.0,
            (candidateBounds.MinY + candidateBounds.MaxY) / 2.0);
        var center = RotateAround(localCenter, new Point2(0, 0), rotation);
        center = new Point2(center.X + origin.X, center.Y + origin.Y);

        return new OrientedTemplateMatch(
            template,
            match.Value.Score,
            NormalizeAngle(rotation),
            center,
            candidateBounds.Width,
            candidateBounds.Height);
    }

    private static bool IsBetterMatch(
        OrientedTemplateMatch candidate,
        OrientedTemplateMatch existing)
    {
        if (candidate.Score > existing.Score + 1e-12)
            return true;
        if (existing.Score > candidate.Score + 1e-12)
            return false;

        return RotationPriority(candidate.Rotation) < RotationPriority(existing.Rotation);
    }

    private static double RotationPriority(double angle)
    {
        var normalized = NormalizeAngle(angle);
        var absolute = Math.Abs(normalized);
        if (Math.Abs(absolute - Math.PI / 2.0) <= 1e-10)
            return absolute - (normalized > 0 ? 1e-9 : 0);
        return absolute;
    }

    private static double PreferRotation(double left, double right)
    {
        var leftPriority = RotationPriority(left);
        var rightPriority = RotationPriority(right);
        if (leftPriority < rightPriority) return NormalizeAngle(left);
        if (rightPriority < leftPriority) return NormalizeAngle(right);

        var normalizedLeft = NormalizeAngle(left);
        var normalizedRight = NormalizeAngle(right);
        return normalizedLeft >= normalizedRight ? normalizedLeft : normalizedRight;
    }

    private static TemplateStrokeMatch? MatchTemplate(
        IReadOnlyList<VectorGlyphTemplateStroke> candidate,
        IReadOnlyList<VectorGlyphTemplateStroke> template,
        double tolerance)
    {
        var used = new bool[template.Count];
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
                Search(index + 1, cost + error);
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

    private static VectorGlyphTemplateStroke Normalize(
        VectorGlyphTemplateStroke stroke,
        GlyphBounds bounds)
        => new(Normalize(stroke.Start, bounds), Normalize(stroke.End, bounds));

    private static VectorGlyphTemplateStroke NormalizeCandidateForTemplate(
        VectorGlyphTemplateStroke stroke,
        GlyphBounds candidateBounds,
        GlyphBounds templateBounds)
        => new(
            NormalizeCandidatePointForTemplate(
                stroke.Start,
                candidateBounds,
                templateBounds),
            NormalizeCandidatePointForTemplate(
                stroke.End,
                candidateBounds,
                templateBounds));

    private static Point2 NormalizeCandidatePointForTemplate(
        Point2 point,
        GlyphBounds candidateBounds,
        GlyphBounds templateBounds)
        => new(
            templateBounds.Width <= 1e-12
                ? 0.5
                : candidateBounds.Width <= 1e-12
                    ? 0.5
                    : (point.X - candidateBounds.MinX) / candidateBounds.Width,
            templateBounds.Height <= 1e-12
                ? 0.5
                : candidateBounds.Height <= 1e-12
                    ? 0.5
                    : (point.Y - candidateBounds.MinY) / candidateBounds.Height);

    private static Point2 Normalize(Point2 point, GlyphBounds bounds)
        => new(
            bounds.Width <= 1e-12 ? 0.5 : (point.X - bounds.MinX) / bounds.Width,
            bounds.Height <= 1e-12 ? 0.5 : (point.Y - bounds.MinY) / bounds.Height);

    private static bool LooksLikeGlyphCandidate(IReadOnlyList<LinePrimitive> component)
    {
        var bounds = GetBounds(component);
        return component.Count is >= 2 and <= 64
            && bounds.Height > 1e-9
            && (bounds.Width <= 1e-9 || bounds.Width / bounds.Height is >= 0.05 and <= 20.0);
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

    private static IReadOnlyList<IReadOnlyList<IndexedLine>> BuildConnectedComponents(
        IReadOnlyList<IndexedLine> lines,
        double endpointTolerance)
    {
        var parent = Enumerable.Range(0, lines.Count).ToArray();

        for (var left = 0; left < lines.Count; left++)
        for (var right = left + 1; right < lines.Count; right++)
        {
            if (EndpointDistance(lines[left].Line, lines[right].Line) <= endpointTolerance)
                Union(parent, left, right);
        }

        var groups = new Dictionary<int, List<IndexedLine>>();
        for (var index = 0; index < lines.Count; index++)
        {
            var root = Find(parent, index);
            if (!groups.TryGetValue(root, out var group))
            {
                group = [];
                groups.Add(root, group);
            }
            group.Add(lines[index]);
        }

        return groups.Values
            .OrderBy(group => GetBounds(group.Select(item => item.Line)).MinY)
            .ThenBy(group => GetBounds(group.Select(item => item.Line)).MinX)
            .Cast<IReadOnlyList<IndexedLine>>()
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

    private static Point2 GetCentroid(IEnumerable<LinePrimitive> lines)
    {
        var points = lines.SelectMany(line => new[] { line.Start, line.End }).ToArray();
        return new Point2(
            points.Average(point => point.X),
            points.Average(point => point.Y));
    }

    private static Point2 RotateAround(Point2 point, Point2 origin, double radians)
    {
        var x = point.X - origin.X;
        var y = point.Y - origin.Y;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new Point2(
            x * cos - y * sin,
            x * sin + y * cos);
    }

    private static double StrokeAngle(Point2 start, Point2 end)
        => Math.Atan2(end.Y - start.Y, end.X - start.X);

    private static double Dot(Point2 point, double axisX, double axisY)
        => point.X * axisX + point.Y * axisY;

    private static double PointDistance(Point2 left, Point2 right)
        => Math.Sqrt(
            (left.X - right.X) * (left.X - right.X)
            + (left.Y - right.Y) * (left.Y - right.Y));

    private static double NormalizeAngle(double angle)
    {
        var twoPi = 2.0 * Math.PI;
        while (angle <= -Math.PI) angle += twoPi;
        while (angle > Math.PI) angle -= twoPi;
        return angle;
    }

    private static double AngleDistance(double left, double right)
        => Math.Abs(NormalizeAngle(left - right));

    private static double AngleDistanceModuloPi(double left, double right)
    {
        var delta = AngleDistance(left, right);
        return Math.Min(delta, Math.Abs(Math.PI - delta));
    }

    private static bool IsHalfTurnSeparated(double left, double right)
        => Math.Abs(AngleDistance(left, right) - Math.PI) <= 1e-8;

    private static bool IsFinite(Point2 point)
        => !double.IsNaN(point.X)
            && !double.IsInfinity(point.X)
            && !double.IsNaN(point.Y)
            && !double.IsInfinity(point.Y);

    private readonly record struct IndexedLine(int Index, LinePrimitive Line);

    private readonly record struct GlyphCandidate(
        string Value,
        double Confidence,
        Point2 Center,
        double Width,
        double Height,
        double Rotation,
        string? Layer,
        IReadOnlyList<string> SourcePrimitiveIds);

    private readonly record struct OrientedTemplateMatch(
        VectorGlyphTemplate Template,
        double Score,
        double Rotation,
        Point2 Center,
        double Width,
        double Height);

    private readonly record struct TemplateStrokeMatch(double Score);

    private readonly record struct GlyphBounds(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }
}
