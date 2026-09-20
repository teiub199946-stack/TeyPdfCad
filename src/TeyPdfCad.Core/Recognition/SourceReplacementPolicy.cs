namespace TeyPdfCad.Core.Recognition;

public static class SourceReplacementPolicy
{
    public static IReadOnlyList<string> SelectWholeObjects(
        IReadOnlyCollection<string> sourceObjectIds,
        IEnumerable<string> validatedProvenanceIds)
    {
        if (sourceObjectIds is null) throw new ArgumentNullException(nameof(sourceObjectIds));
        if (validatedProvenanceIds is null) throw new ArgumentNullException(nameof(validatedProvenanceIds));

        var provenance = validatedProvenanceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => id.IndexOf('#') < 0);
        var validated = new HashSet<string>(provenance, StringComparer.OrdinalIgnoreCase);

        return sourceObjectIds
            .Where(id => validated.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

}
