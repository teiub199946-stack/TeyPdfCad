using ACadSharp.Entities;
using CSMath;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DwgStructuralSanityTests
{
    [Fact]
    public void Final_inventory_equals_probe_minus_authorized_source_multiset()
    {
        var source = new PageSourceRef(1, "s1");
        var probe = Inventory(
            candidateMetadata: 2,
            ("native", 2),
            ("source-a", 2),
            ("source-b", 1));
        var emissions = Summary(
            (source, new Dictionary<string, int>
            {
                ["source-a"] = 1,
                ["source-b"] = 1
            }));
        var final = Inventory(
            candidateMetadata: 2,
            ("native", 2),
            ("source-a", 1));

        DwgStructuralSanity.ValidateFinal(
            probe,
            final,
            emissions,
            [source]);
    }

    [Fact]
    public void Same_entity_count_but_wrong_fingerprint_fails_closed()
    {
        var source = new PageSourceRef(1, "s1");
        var probe = Inventory(
            candidateMetadata: 1,
            ("native", 1),
            ("source", 1));
        var emissions = Summary(
            (source, new Dictionary<string, int> { ["source"] = 1 }));
        var wrongFinal = Inventory(
            candidateMetadata: 1,
            ("different-native", 1));

        Assert.Throws<InvalidDataException>(() =>
            DwgStructuralSanity.ValidateFinal(
                probe,
                wrongFinal,
                emissions,
                [source]));
    }

    [Fact]
    public void Writer_source_declaration_mismatch_is_rejected_against_closed_probe()
    {
        var source = new PageSourceRef(1, "s1");
        var probe = Inventory(
            candidateMetadata: 0,
            ("closed-probe-fingerprint", 1)) with
        {
            SourceFingerprintCountsBySource =
                new Dictionary<PageSourceRef, IReadOnlyDictionary<string, int>>
                {
                    [source] = new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["closed-probe-fingerprint"] = 1
                    }
                }
        };
        var declared = Summary(
            (source, new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["writer-only-fingerprint"] = 1
            }));

        Assert.Throws<InvalidDataException>(() =>
            DwgStructuralSanity.ValidateDeclaredSourceEmissionParity(
                probe,
                declared));
    }

    [Fact]
    public void Requested_suppression_multiset_must_exist_in_probe()
    {
        var source = new PageSourceRef(1, "s1");
        var probe = Inventory(
            candidateMetadata: 0,
            ("source", 1));
        var emissions = Summary(
            (source, new Dictionary<string, int> { ["source"] = 2 }));

        Assert.Throws<InvalidDataException>(() =>
            DwgStructuralSanity.BuildExpectedFinalFingerprintMultiset(
                probe,
                emissions,
                [source]));
    }

    [Fact]
    public void Duplicate_fingerprints_are_subtracted_by_count_not_presence()
    {
        var source = new PageSourceRef(1, "s1");
        var probe = Inventory(
            candidateMetadata: 0,
            ("same", 3));
        var emissions = Summary(
            (source, new Dictionary<string, int> { ["same"] = 2 }));

        var expected = DwgStructuralSanity.BuildExpectedFinalFingerprintMultiset(
            probe,
            emissions,
            [source]);

        Assert.Equal(1, expected["same"]);
    }


    [Fact]
    public void Wrong_source_removal_with_identical_geometry_is_detected_by_source_identity()
    {
        var sourceA = new PageSourceRef(1, "A");
        var sourceB = new PageSourceRef(1, "B");

        var lineA = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var lineB = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        SourceMetadataCodec.Write(lineA, sourceA);
        SourceMetadataCodec.Write(lineB, sourceB);

        var fingerprintA = DwgEntityFingerprint.ComputeOutput(lineA);
        var fingerprintB = DwgEntityFingerprint.ComputeOutput(lineB);
        Assert.NotEqual(fingerprintA, fingerprintB);

        var probe = Inventory(
            candidateMetadata: 0,
            (fingerprintA, 1),
            (fingerprintB, 1));
        var emissions = Summary(
            (sourceA, new Dictionary<string, int>
            {
                [fingerprintA] = 1
            }));

        // Wrong final: B was removed while authorized A incorrectly remained.
        // Entity count and geometry are unchanged relative to the intended final,
        // so only persistent source identity can distinguish the error.
        var wrongFinal = Inventory(
            candidateMetadata: 0,
            (fingerprintA, 1));

        Assert.Throws<InvalidDataException>(() =>
            DwgStructuralSanity.ValidateFinal(
                probe,
                wrongFinal,
                emissions,
                [sourceA]));
    }

    [Fact]
    public void Candidate_metadata_count_must_not_change_in_final()
    {
        var probe = Inventory(
            candidateMetadata: 2,
            ("native-a", 1),
            ("native-b", 1));
        var final = Inventory(
            candidateMetadata: 1,
            ("native-a", 1),
            ("native-b", 1));
        var emissions = Summary();

        Assert.Throws<InvalidDataException>(() =>
            DwgStructuralSanity.ValidateFinal(
                probe,
                final,
                emissions,
                []));
    }

    private static DwgStructuralInventory Inventory(
        int candidateMetadata,
        params (string Fingerprint, int Count)[] fingerprints)
        => new(
            fingerprints.Sum(pair => pair.Count),
            candidateMetadata,
            fingerprints.ToDictionary(
                pair => pair.Fingerprint,
                pair => pair.Count,
                StringComparer.Ordinal));

    private static SourceEmissionSummary Summary(
        params (PageSourceRef Source, IReadOnlyDictionary<string, int> Fingerprints)[] sources)
        => new(sources.ToDictionary(
            pair => pair.Source,
            pair => pair.Fingerprints));
}
