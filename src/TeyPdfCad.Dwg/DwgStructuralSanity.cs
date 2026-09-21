namespace TeyPdfCad.Dwg;

public static class DwgStructuralSanity
{
    public static SourceEmissionSummary GetClosedProbeSourceEmissions(
        DwgStructuralInventory probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        return new SourceEmissionSummary(
            probe.SourceFingerprintCountsBySource);
    }

    public static void ValidateDeclaredSourceEmissionParity(
        DwgStructuralInventory probe,
        SourceEmissionSummary declared)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(declared);

        var actual = probe.SourceFingerprintCountsBySource;
        var declaredEncodable = declared.OutputFingerprintCountsBySource
            .Where(pair => SourceMetadataCodec.CanEncode(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (actual.Count != declaredEncodable.Count)
        {
            throw new InvalidDataException(
                $"Closed-probe source inventory count {actual.Count} != writer declaration count {declaredEncodable.Count}.");
        }

        foreach (var pair in declaredEncodable)
        {
            if (!actual.TryGetValue(pair.Key, out var actualFingerprints)
                || !FingerprintMultisetsEqual(pair.Value, actualFingerprints))
            {
                throw new InvalidDataException(
                    $"Closed-probe source inventory differs from writer declaration for {pair.Key}.");
            }
        }
    }

    public static IReadOnlyDictionary<string, int> BuildExpectedFinalFingerprintMultiset(
        DwgStructuralInventory probe,
        IEnumerable<PageSourceRef> authorizedSuppressedSources)
        => BuildExpectedFinalFingerprintMultiset(
            probe,
            GetClosedProbeSourceEmissions(probe),
            authorizedSuppressedSources);

    public static IReadOnlyDictionary<string, int> BuildExpectedFinalFingerprintMultiset(
        DwgStructuralInventory probe,
        SourceEmissionSummary sourceEmissions,
        IEnumerable<PageSourceRef> authorizedSuppressedSources)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(sourceEmissions);
        ArgumentNullException.ThrowIfNull(authorizedSuppressedSources);

        var expected = new Dictionary<string, int>(
            probe.OutputFingerprintCounts,
            StringComparer.Ordinal);
        var removed = sourceEmissions.GetExpectedSuppressionFingerprintMultiset(
            authorizedSuppressedSources);

        SubtractFingerprintMultiset(
            expected,
            removed,
            "Authorized source-emission fingerprint is not a sub-multiset of the on-disk probe inventory");

        return expected;
    }

    public static IReadOnlyDictionary<string, int> BuildExpectedFinalFingerprintMultiset(
        DwgStructuralInventory probe,
        SourceEmissionSummary sourceEmissions,
        IEnumerable<PageSourceRef> authorizedSuppressedSources,
        IEnumerable<string> authorizedNativeCandidateIds)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(sourceEmissions);
        ArgumentNullException.ThrowIfNull(authorizedSuppressedSources);
        ArgumentNullException.ThrowIfNull(authorizedNativeCandidateIds);

        var authorizedCandidates = authorizedNativeCandidateIds
            .ToHashSet(StringComparer.Ordinal);
        var unknownAuthorizedCandidates = authorizedCandidates
            .Where(candidateId =>
                !probe.CandidateFingerprintCountsByCandidate.ContainsKey(candidateId))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (unknownAuthorizedCandidates.Length > 0)
        {
            throw new InvalidDataException(
                $"Authorized native candidate(s) are absent from the closed probe inventory: {string.Join(", ", unknownAuthorizedCandidates)}.");
        }

        var expected = BuildExpectedFinalFingerprintMultiset(
            probe,
            sourceEmissions,
            authorizedSuppressedSources)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        foreach (var pair in probe.CandidateFingerprintCountsByCandidate
                     .Where(pair => !authorizedCandidates.Contains(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            SubtractFingerprintMultiset(
                expected,
                pair.Value,
                $"Probe-only native candidate {pair.Key} fingerprint is not a sub-multiset of the on-disk probe inventory");
        }

        return expected;
    }

    public static void ValidateFinal(
        DwgStructuralInventory probe,
        DwgStructuralInventory final,
        SourceEmissionSummary sourceEmissions,
        IEnumerable<PageSourceRef> authorizedSuppressedSources)
    {
        ArgumentNullException.ThrowIfNull(final);

        var expected = BuildExpectedFinalFingerprintMultiset(
            probe,
            sourceEmissions,
            authorizedSuppressedSources);

        if (final.CandidateMetadataEntityCount != probe.CandidateMetadataEntityCount)
        {
            throw new InvalidDataException(
                $"Final candidate metadata entity count {final.CandidateMetadataEntityCount} != verified probe count {probe.CandidateMetadataEntityCount}.");
        }

        if (!MultisetsEqual(expected, final.OutputFingerprintCounts))
        {
            throw new InvalidDataException(
                "Final DWG structural fingerprint multiset does not equal probe inventory minus authorized source-emission fingerprints.");
        }
    }

    public static void ValidateFinal(
        DwgStructuralInventory probe,
        DwgStructuralInventory final,
        SourceEmissionSummary sourceEmissions,
        IEnumerable<PageSourceRef> authorizedSuppressedSources,
        IEnumerable<string> authorizedNativeCandidateIds)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(final);
        ArgumentNullException.ThrowIfNull(sourceEmissions);
        ArgumentNullException.ThrowIfNull(authorizedSuppressedSources);
        ArgumentNullException.ThrowIfNull(authorizedNativeCandidateIds);

        var authorizedCandidates = authorizedNativeCandidateIds
            .ToHashSet(StringComparer.Ordinal);
        var expected = BuildExpectedFinalFingerprintMultiset(
            probe,
            sourceEmissions,
            authorizedSuppressedSources,
            authorizedCandidates);

        var expectedCandidateMetadataEntityCount = authorizedCandidates.Sum(candidateId =>
            probe.CandidateFingerprintCountsByCandidate[candidateId].Values.Sum());
        if (final.CandidateMetadataEntityCount != expectedCandidateMetadataEntityCount)
        {
            throw new InvalidDataException(
                $"Final candidate metadata entity count {final.CandidateMetadataEntityCount} != authorized count {expectedCandidateMetadataEntityCount}.");
        }

        var finalCandidateIds = final.CandidateFingerprintCountsByCandidate.Keys
            .ToHashSet(StringComparer.Ordinal);
        if (!finalCandidateIds.SetEquals(authorizedCandidates))
        {
            var missing = authorizedCandidates.Except(finalCandidateIds, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal);
            var unexpected = finalCandidateIds.Except(authorizedCandidates, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal);
            throw new InvalidDataException(
                $"Final native candidate identity set differs from authorization. Missing: [{string.Join(", ", missing)}]; unexpected: [{string.Join(", ", unexpected)}].");
        }

        foreach (var candidateId in authorizedCandidates.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!FingerprintMultisetsEqual(
                    probe.CandidateFingerprintCountsByCandidate[candidateId],
                    final.CandidateFingerprintCountsByCandidate[candidateId]))
            {
                throw new InvalidDataException(
                    $"Final native candidate {candidateId} fingerprint multiset differs from the verified closed probe.");
            }
        }

        if (!MultisetsEqual(expected, final.OutputFingerprintCounts))
        {
            throw new InvalidDataException(
                "Final DWG structural fingerprint multiset does not equal probe inventory minus authorized source emissions and probe-only native candidates.");
        }
    }

    private static void SubtractFingerprintMultiset(
        IDictionary<string, int> target,
        IReadOnlyDictionary<string, int> removed,
        string errorPrefix)
    {
        foreach (var pair in removed)
        {
            if (!target.TryGetValue(pair.Key, out var available)
                || available < pair.Value)
            {
                throw new InvalidDataException(
                    $"{errorPrefix}: requested {pair.Value}, available {available}.");
            }

            var remaining = available - pair.Value;
            if (remaining == 0)
                target.Remove(pair.Key);
            else
                target[pair.Key] = remaining;
        }
    }

    private static bool FingerprintMultisetsEqual(
        IReadOnlyDictionary<string, int> first,
        IReadOnlyDictionary<string, int> second)
    {
        if (first.Count != second.Count)
            return false;

        foreach (var pair in first)
        {
            if (!second.TryGetValue(pair.Key, out var count) || count != pair.Value)
                return false;
        }

        return true;
    }

    private static bool MultisetsEqual(
        IReadOnlyDictionary<string, int> first,
        IReadOnlyDictionary<string, int> second)
    {
        if (first.Count != second.Count)
            return false;

        foreach (var pair in first)
        {
            if (!second.TryGetValue(pair.Key, out var count)
                || count != pair.Value)
            {
                return false;
            }
        }

        return true;
    }
}
