using System.Globalization;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using TeyPdfCad.Core.Recognition;

namespace TeyPdfCad.Dwg;

public sealed record DwgStructuralInventory(
    int CountedEntityCount,
    int CandidateMetadataEntityCount,
    IReadOnlyDictionary<string, int> OutputFingerprintCounts);

public sealed class DwgReadBackVerifier
{
    public NativeReadBackVerification Verify(
        string dwgPath,
        NativeWriteManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(dwgPath))
            throw new ArgumentException("DWG path is required.", nameof(dwgPath));
        ArgumentNullException.ThrowIfNull(manifest);

        var drawing = DwgReader.Read(dwgPath);
        var observations = ReadObservations(drawing, out var globalInvalid, out var invalidByCandidate);
        var globalIssues = globalInvalid.ToList();

        foreach (var unexpectedCandidateId in observations
                     .Select(observation => observation.Metadata.CandidateId)
                     .Concat(invalidByCandidate.Keys)
                     .Distinct(StringComparer.Ordinal)
                     .Where(candidateId => !manifest.Candidates.ContainsKey(candidateId))
                     .OrderBy(candidateId => candidateId, StringComparer.Ordinal))
        {
            globalIssues.Add($"Unexpected CandidateId '{unexpectedCandidateId}' exists in DWG read-back.");
        }

        var result = new Dictionary<string, CandidateVerification>(StringComparer.Ordinal);
        foreach (var pair in manifest.Candidates.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var candidateId = pair.Key;
            var expected = pair.Value;
            var missing = new List<string>();
            var duplicates = new List<string>();
            var invalid = new List<string>();

            if (!string.Equals(expected.CandidateId, candidateId, StringComparison.Ordinal))
                invalid.Add("Manifest candidate key does not equal ExpectedCandidate.CandidateId.");

            invalid.AddRange(globalIssues);
            if (invalidByCandidate.TryGetValue(candidateId, out var candidateInvalid))
                invalid.AddRange(candidateInvalid);

            var candidateObservations = observations
                .Where(observation =>
                    string.Equals(observation.Metadata.CandidateId, candidateId, StringComparison.Ordinal))
                .ToArray();

            var expectedRoleGroups = expected.Entities
                .GroupBy(entity => entity.Role, StringComparer.Ordinal)
                .ToArray();
            foreach (var duplicateExpectedRole in expectedRoleGroups.Where(group => group.Count() > 1))
            {
                duplicates.Add(duplicateExpectedRole.Key);
                invalid.Add($"Manifest declares role '{duplicateExpectedRole.Key}' more than once.");
            }

            var expectedRoles = expectedRoleGroups
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var extra in candidateObservations
                         .Where(observation => !expectedRoles.Contains(observation.Metadata.Role)))
            {
                invalid.Add($"Unexpected metadata role '{extra.Metadata.Role}' on {extra.Entity.GetType().Name}.");
            }

            foreach (var expectedEntity in expected.Entities)
            {
                if (!string.Equals(expectedEntity.CandidateId, candidateId, StringComparison.Ordinal))
                {
                    invalid.Add($"Expected entity role '{expectedEntity.Role}' has mismatched CandidateId.");
                    continue;
                }

                var matching = candidateObservations
                    .Where(observation =>
                        string.Equals(
                            observation.Metadata.Role,
                            expectedEntity.Role,
                            StringComparison.Ordinal))
                    .ToArray();

                if (matching.Length == 0)
                {
                    missing.Add(expectedEntity.Role);
                    continue;
                }

                if (matching.Length > 1)
                {
                    duplicates.Add(expectedEntity.Role);
                    continue;
                }

                ValidateEntity(
                    matching[0],
                    expectedEntity,
                    invalid);
            }

            var isVerified = missing.Count == 0
                && duplicates.Count == 0
                && invalid.Count == 0;

            result[candidateId] = new CandidateVerification(
                candidateId,
                isVerified,
                missing.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                duplicates.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                invalid.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }

        return new NativeReadBackVerification(result);
    }

    public DwgStructuralInventory ReadStructuralInventory(string dwgPath)
    {
        if (string.IsNullOrWhiteSpace(dwgPath))
            throw new ArgumentException("DWG path is required.", nameof(dwgPath));

        var drawing = DwgReader.Read(dwgPath);
        var counted = new List<Entity>();
        foreach (var entity in drawing.Entities)
        {
            counted.Add(entity);
            if (entity is Insert insert)
                counted.AddRange(insert.Attributes);
        }

        var malformedSource = counted.FirstOrDefault(entity =>
            SourceMetadataCodec.HasSourceApp(entity)
            && !SourceMetadataCodec.TryRead(entity, out _));
        if (malformedSource is not null)
        {
            throw new InvalidDataException(
                $"Malformed {SourceMetadataCodec.AppId} metadata exists on {malformedSource.GetType().Name}.");
        }

        var fingerprints = counted
            .Select(DwgEntityFingerprint.ComputeOutput)
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new DwgStructuralInventory(
            counted.Count,
            counted.Count(CandidateMetadataCodec.HasCandidateApp),
            fingerprints);
    }

    private static IReadOnlyList<Observation> ReadObservations(
        CadDocument drawing,
        out IReadOnlyList<string> globalInvalid,
        out IReadOnlyDictionary<string, IReadOnlyList<string>> invalidByCandidate)
    {
        var observations = new List<Observation>();
        var global = new List<string>();
        var perCandidate = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var entity in drawing.Entities)
        {
            AddObservation(entity, null, observations, global);

            if (entity is Insert insert)
            {
                foreach (var attribute in insert.Attributes)
                    AddObservation(attribute, insert, observations, global);
            }
        }

        foreach (var block in drawing.BlockRecords)
        {
            if (!block.ExtendedData.TryGet(CandidateMetadataCodec.AppId, out _))
                continue;

            if (block.ExtendedData.TryGet(CandidateMetadataCodec.AppId, out var data))
            {
                var strings = data.Records
                    .OfType<ACadSharp.XData.ExtendedDataString>()
                    .Select(record => record.Value)
                    .ToArray();
                if (strings.Length >= 1 && !string.IsNullOrWhiteSpace(strings[0]))
                {
                    if (!perCandidate.TryGetValue(strings[0], out var list))
                    {
                        list = [];
                        perCandidate[strings[0]] = list;
                    }
                    list.Add($"Candidate metadata is forbidden on BlockRecord '{block.Name}'.");
                }
                else
                {
                    global.Add($"Malformed candidate metadata exists on BlockRecord '{block.Name}'.");
                }
            }
        }

        globalInvalid = global.Distinct(StringComparer.Ordinal).ToArray();
        invalidByCandidate = perCandidate.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        return observations;
    }

    private static void AddObservation(
        Entity entity,
        Insert? parentInsert,
        ICollection<Observation> observations,
        ICollection<string> globalInvalid)
    {
        if (!CandidateMetadataCodec.HasCandidateApp(entity))
            return;

        if (!CandidateMetadataCodec.TryRead(entity, out var metadata))
        {
            globalInvalid.Add($"Malformed {CandidateMetadataCodec.AppId} metadata exists on {entity.GetType().Name}.");
            return;
        }

        observations.Add(new Observation(entity, metadata, parentInsert));
    }

    private static void ValidateEntity(
        Observation observation,
        ExpectedNativeEntity expected,
        ICollection<string> invalid)
    {
        if (!KindMatches(observation.Entity, expected.EntityKind))
        {
            invalid.Add(
                $"Role '{expected.Role}' has wrong entity type: expected {expected.EntityKind}, actual {observation.Entity.GetType().Name}.");
            return;
        }

        var actualFingerprint = DwgEntityFingerprint.ComputeGeometry(observation.Entity);
        if (!string.Equals(actualFingerprint, expected.GeometryFingerprint, StringComparison.Ordinal))
        {
            invalid.Add($"Role '{expected.Role}' geometry fingerprint mismatch.");
        }

        if (observation.Entity is AttributeEntity
            && string.Equals(expected.Role, "attribute", StringComparison.Ordinal))
        {
            var parent = observation.ParentInsert;
            var validParent = parent is not null
                && CandidateMetadataCodec.TryRead(parent, out var parentMetadata)
                && string.Equals(parentMetadata.CandidateId, expected.CandidateId, StringComparison.Ordinal);
            if (!validParent)
                invalid.Add($"Role '{expected.Role}' attribute parent INSERT does not carry the same CandidateId.");
        }

        if (expected.RequiredProperties.TryGetValue("expectedMeasurement", out var expectedMeasurementText))
        {
            if (observation.Entity is not Dimension dimension
                || !TryDouble(expectedMeasurementText, out var expectedMeasurement))
            {
                invalid.Add($"Role '{expected.Role}' property 'expectedMeasurement' failed: property requires a Dimension and numeric value.");
            }
            else
            {
                var tolerance = 1e-6;
                if (expected.RequiredProperties.TryGetValue("measurementTolerance", out var toleranceText))
                {
                    if (!TryDouble(toleranceText, out tolerance) || tolerance < 0d)
                    {
                        invalid.Add($"Role '{expected.Role}' property 'measurementTolerance' failed: tolerance must be non-negative numeric.");
                        tolerance = 0d;
                    }
                }

                if (Math.Abs(dimension.Measurement - expectedMeasurement) > tolerance)
                {
                    invalid.Add(
                        $"Role '{expected.Role}' property 'expectedMeasurement' failed: measurement {dimension.Measurement:R} != {expectedMeasurement:R} within {tolerance:R}.");
                }
            }
        }

        foreach (var property in expected.RequiredProperties
                     .Where(pair => pair.Key is not "expectedMeasurement" and not "measurementTolerance")
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!ValidateProperty(observation.Entity, property.Key, property.Value, out var error))
                invalid.Add($"Role '{expected.Role}' property '{property.Key}' failed: {error}");
        }
    }

    private static bool KindMatches(Entity entity, string expectedKind)
        => expectedKind switch
        {
            "Line" => entity is Line,
            "Dimension" => entity is Dimension,
            "Leader" => entity is Leader,
            "Text" => entity is TextEntity,
            "Insert" => entity is Insert,
            "AttributeEntity" => entity is AttributeEntity,
            "Hatch" => entity is Hatch,
            _ => string.Equals(entity.GetType().Name, expectedKind, StringComparison.Ordinal)
        };

    private static bool ValidateProperty(
        Entity entity,
        string key,
        string expectedValue,
        out string error)
    {
        error = string.Empty;
        switch (key)
        {
            case "minimumVertices":
                if (entity is not Leader leader || !int.TryParse(expectedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minimumVertices))
                {
                    error = "property requires Leader and integer value";
                    return false;
                }
                if (leader.Vertices.Count >= minimumVertices)
                    return true;
                error = $"vertex count {leader.Vertices.Count} < {minimumVertices}";
                return false;

            case "minimumHeight":
                if (!TryDouble(expectedValue, out var minimumHeight))
                {
                    error = "minimumHeight must be numeric";
                    return false;
                }
                var height = entity switch
                {
                    AttributeEntity attribute => attribute.Height,
                    TextEntity text => text.Height,
                    _ => double.NaN
                };
                if (!double.IsNaN(height) && height >= minimumHeight)
                    return true;
                error = $"height {height:R} < {minimumHeight:R}";
                return false;

            case "nonEmpty":
                if (entity is TextEntity textEntity
                    && bool.TryParse(expectedValue, out var requireNonEmpty))
                {
                    if (!requireNonEmpty || !string.IsNullOrWhiteSpace(textEntity.Value))
                        return true;
                    error = "text value is empty";
                    return false;
                }
                error = "nonEmpty requires TextEntity and boolean value";
                return false;

            case "blockName":
                if (entity is Insert insert)
                {
                    if (string.Equals(insert.Block?.Name, expectedValue, StringComparison.Ordinal))
                        return true;
                    error = $"block '{insert.Block?.Name}' != '{expectedValue}'";
                    return false;
                }
                error = "blockName requires Insert";
                return false;

            case "blockDefinitionFingerprint":
                if (entity is Insert blockInsert)
                {
                    var actual = DwgEntityFingerprint.ComputeBlockDefinition(blockInsert);
                    if (string.Equals(actual, expectedValue, StringComparison.Ordinal))
                        return true;
                    error = "block definition fingerprint mismatch";
                    return false;
                }
                error = "blockDefinitionFingerprint requires Insert";
                return false;

            case "minimumScale":
                if (entity is Insert scaleInsert && TryDouble(expectedValue, out var minimumScale))
                {
                    var scale = Math.Max(Math.Abs(scaleInsert.XScale), Math.Max(Math.Abs(scaleInsert.YScale), Math.Abs(scaleInsert.ZScale)));
                    if (scale >= minimumScale)
                        return true;
                    error = $"maximum scale {scale:R} < {minimumScale:R}";
                    return false;
                }
                error = "minimumScale requires Insert and numeric value";
                return false;

            case "minimumLength":
                if (entity is Insert lengthInsert && TryDouble(expectedValue, out var minimumLength))
                {
                    var length = Math.Abs(lengthInsert.XScale);
                    if (length >= minimumLength)
                        return true;
                    error = $"insert XScale length {length:R} < {minimumLength:R}";
                    return false;
                }
                error = "minimumLength requires Insert and numeric value";
                return false;

            case "attributeTag":
                if (entity is AttributeEntity attributeEntity)
                {
                    if (string.Equals(attributeEntity.Tag, expectedValue, StringComparison.Ordinal))
                        return true;
                    error = $"attribute tag '{attributeEntity.Tag}' != '{expectedValue}'";
                    return false;
                }
                error = "attributeTag requires AttributeEntity";
                return false;

            case "nonEmptyValue":
                if (entity is AttributeEntity attributeValue
                    && bool.TryParse(expectedValue, out var requireValue))
                {
                    if (!requireValue || !string.IsNullOrWhiteSpace(attributeValue.Value))
                        return true;
                    error = "attribute value is empty";
                    return false;
                }
                error = "nonEmptyValue requires AttributeEntity and boolean value";
                return false;

            case "minimumArea":
                if (entity is Hatch hatch && TryDouble(expectedValue, out var minimumArea))
                {
                    var box = hatch.GetBoundingBox();
                    var area = Math.Abs((box.Max.X - box.Min.X) * (box.Max.Y - box.Min.Y));
                    if (area >= minimumArea)
                        return true;
                    error = $"hatch bounding area {area:R} < {minimumArea:R}";
                    return false;
                }
                error = "minimumArea requires Hatch and numeric value";
                return false;

            case "boundaryFingerprint":
                if (entity is Hatch boundaryHatch)
                {
                    var actual = DwgEntityFingerprint.ComputeHatchBoundary(boundaryHatch);
                    if (string.Equals(actual, expectedValue, StringComparison.Ordinal))
                        return true;
                    error = "hatch boundary fingerprint mismatch";
                    return false;
                }
                error = "boundaryFingerprint requires Hatch";
                return false;

            default:
                error = "unknown required property";
                return false;
        }
    }

    private static bool TryDouble(string value, out double parsed)
        => double.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out parsed);

    private sealed record Observation(
        Entity Entity,
        CandidateEntityMetadata Metadata,
        Insert? ParentInsert);
}
