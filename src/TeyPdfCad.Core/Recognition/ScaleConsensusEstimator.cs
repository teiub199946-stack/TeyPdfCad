namespace TeyPdfCad.Core.Recognition;

internal readonly record struct ScaleObservation(double Scale, double Weight);

internal readonly record struct ScaleConsensus(double Scale, int Votes, double Weight);

internal static class ScaleConsensusEstimator
{
    public static ScaleConsensus? Estimate(
        IReadOnlyList<ScaleObservation> observations,
        double relativeTolerance,
        int minimumVotes)
    {
        if (observations.Count < minimumVotes) return null;

        ScaleConsensus? best = null;

        for (var i = 0; i < observations.Count; i++)
        {
            var seed = observations[i].Scale;
            if (!double.IsFinite(seed) || seed <= 0) continue;

            var members = observations
                .Where(x => RelativeDifference(x.Scale, seed) <= relativeTolerance)
                .ToArray();

            if (members.Length < minimumVotes) continue;

            var totalWeight = members.Sum(x => Math.Max(x.Weight, 1e-9));
            var weightedScale = members.Sum(x => x.Scale * Math.Max(x.Weight, 1e-9)) / totalWeight;

            // Re-evaluate around the weighted center to avoid a seed sitting at a cluster edge.
            members = observations
                .Where(x => RelativeDifference(x.Scale, weightedScale) <= relativeTolerance)
                .ToArray();

            if (members.Length < minimumVotes) continue;

            totalWeight = members.Sum(x => Math.Max(x.Weight, 1e-9));
            weightedScale = members.Sum(x => x.Scale * Math.Max(x.Weight, 1e-9)) / totalWeight;
            var candidate = new ScaleConsensus(weightedScale, members.Length, totalWeight);

            if (best is null
                || candidate.Votes > best.Value.Votes
                || (candidate.Votes == best.Value.Votes && candidate.Weight > best.Value.Weight))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static double RelativeDifference(double a, double b)
        => Math.Abs(a - b) / Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1e-9);
}
