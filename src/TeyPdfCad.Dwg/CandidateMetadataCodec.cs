using ACadSharp.Entities;
using ACadSharp.XData;

namespace TeyPdfCad.Dwg;

public readonly record struct CandidateEntityMetadata(
    string CandidateId,
    string Role);

public static class CandidateMetadataCodec
{
    public const string AppId = "TEYCONVERT_CANDIDATE_V1";

    public static void Write(Entity entity, CandidateEntityMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateField(metadata.CandidateId, nameof(metadata.CandidateId));
        ValidateField(metadata.Role, nameof(metadata.Role));

        if (entity.ExtendedData.TryGet(AppId, out _))
        {
            throw new InvalidOperationException(
                $"Entity already carries {AppId} candidate metadata.");
        }

        entity.ExtendedData.Add(
            AppId,
            new ExtendedData(
            [
                new ExtendedDataString(metadata.CandidateId),
                new ExtendedDataString(metadata.Role)
            ]));
    }

    public static bool TryRead(
        Entity entity,
        out CandidateEntityMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(entity);
        metadata = default;

        if (!entity.ExtendedData.TryGet(AppId, out var data))
        {
            return false;
        }

        var records = data.Records.ToArray();
        if (records.Length != 2
            || records[0] is not ExtendedDataString candidate
            || records[1] is not ExtendedDataString role
            || !IsValidField(candidate.Value)
            || !IsValidField(role.Value))
        {
            return false;
        }

        metadata = new CandidateEntityMetadata(
            candidate.Value,
            role.Value);
        return true;
    }

    internal static bool HasCandidateApp(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return entity.ExtendedData.TryGet(AppId, out _);
    }

    private static void ValidateField(string value, string parameterName)
    {
        if (!IsValidField(value))
        {
            throw new ArgumentException(
                "Candidate metadata fields must be non-empty printable ASCII without control characters.",
                parameterName);
        }
    }

    private static bool IsValidField(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.All(character =>
                character is >= (char)0x20 and <= (char)0x7E);
}
