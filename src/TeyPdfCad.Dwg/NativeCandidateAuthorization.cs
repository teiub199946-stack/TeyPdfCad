using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Dwg;

public static class NativeCandidateAuthorization
{
    public static IReadOnlySet<string>? GetAuthorizedCandidateIds(
        SourceReplacementPlan plan,
        int pageNumber,
        IReadOnlySet<PageSourceRef>? authorizedSuppressedSources)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        // Null is probe mode: emit every non-deferred candidate for closed-file
        // verification. An explicit set is final mode and must be fail-closed.
        if (authorizedSuppressedSources is null)
            return null;

        var authorizedSourceIds = authorizedSuppressedSources
            .Where(source => source.PageNumber == pageNumber)
            .Select(source => source.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        if (authorizedSourceIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var eligibleSourceIds = plan.EligibleSourceIds
            .ToHashSet(StringComparer.Ordinal);
        var candidateIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceId in authorizedSourceIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!eligibleSourceIds.Contains(sourceId)
                || !plan.SourceCoverageMap.TryGetValue(sourceId, out var candidates)
                || candidates.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Page {pageNumber} native authorization references source '{sourceId}' without one eligible candidate mapping.");
            }

            candidateIds.Add(candidates[0]);
        }

        foreach (var candidateId in candidateIds.OrderBy(value => value, StringComparer.Ordinal))
        {
            var candidateEligibleSources = plan.SourceCoverageMap
                .Where(pair =>
                    eligibleSourceIds.Contains(pair.Key)
                    && pair.Value.Count == 1
                    && string.Equals(pair.Value[0], candidateId, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            if (candidateEligibleSources.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Page {pageNumber} candidate '{candidateId}' has no eligible source set.");
            }

            var missing = candidateEligibleSources
                .Where(sourceId => !authorizedSourceIds.Contains(sourceId))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Page {pageNumber} candidate '{candidateId}' has partial source authorization; missing source(s): {string.Join(", ", missing)}.");
            }
        }

        return candidateIds;
    }

    public static bool ShouldEmit(
        IReadOnlySet<string>? authorizedCandidateIds,
        string candidateId)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
            throw new ArgumentException("CandidateId is required.", nameof(candidateId));

        return authorizedCandidateIds is null
            || authorizedCandidateIds.Contains(candidateId);
    }
}
