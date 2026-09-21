using ACadSharp.Entities;
using ACadSharp.XData;
using CSMath;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class CandidateMetadataCodecTests
{
    [Fact]
    public void Exact_two_string_metadata_round_trips_on_line()
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));

        CandidateMetadataCodec.Write(
            line,
            new CandidateEntityMetadata("v1:1:LINE:abc", "primary"));

        Assert.True(CandidateMetadataCodec.TryRead(line, out var metadata));
        Assert.Equal("v1:1:LINE:abc", metadata.CandidateId);
        Assert.Equal("primary", metadata.Role);
    }

    [Theory]
    [InlineData("", "primary")]
    [InlineData("   ", "primary")]
    [InlineData("candidate", "")]
    [InlineData("candidate", "   ")]
    public void Write_rejects_blank_candidate_or_role(string candidateId, string role)
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));

        Assert.Throws<ArgumentException>(() =>
            CandidateMetadataCodec.Write(
                line,
                new CandidateEntityMetadata(candidateId, role)));
    }

    [Fact]
    public void Write_rejects_existing_candidate_metadata()
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        CandidateMetadataCodec.Write(
            line,
            new CandidateEntityMetadata("candidate-1", "primary"));

        Assert.Throws<InvalidOperationException>(() =>
            CandidateMetadataCodec.Write(
                line,
                new CandidateEntityMetadata("candidate-2", "primary")));
    }

    [Fact]
    public void TryRead_returns_false_for_unknown_app()
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        line.ExtendedData.Add("OTHER_APP", new ExtendedData(
        [
            new ExtendedDataString("candidate"),
            new ExtendedDataString("primary")
        ]));

        Assert.False(CandidateMetadataCodec.TryRead(line, out _));
    }

    [Fact]
    public void TryRead_returns_false_for_one_string()
    {
        var line = WithCandidateData(new ExtendedDataString("candidate"));

        Assert.False(CandidateMetadataCodec.TryRead(line, out _));
    }

    [Fact]
    public void TryRead_returns_false_for_three_strings()
    {
        var line = WithCandidateData(
            new ExtendedDataString("candidate"),
            new ExtendedDataString("primary"),
            new ExtendedDataString("extra"));

        Assert.False(CandidateMetadataCodec.TryRead(line, out _));
    }

    [Fact]
    public void TryRead_returns_false_for_non_string_record()
    {
        var line = WithCandidateData(
            new ExtendedDataString("candidate"),
            new ExtendedDataInteger16(7));

        Assert.False(CandidateMetadataCodec.TryRead(line, out _));
    }

    [Theory]
    [InlineData("", "primary")]
    [InlineData("candidate", "")]
    [InlineData("candidate\ncontrol", "primary")]
    [InlineData("candidate", "role\tcontrol")]
    public void TryRead_returns_false_for_empty_or_control_fields(string candidateId, string role)
    {
        var line = WithCandidateData(
            new ExtendedDataString(candidateId),
            new ExtendedDataString(role));

        Assert.False(CandidateMetadataCodec.TryRead(line, out _));
    }

    [Fact]
    public void Candidate_id_is_ordinal_and_case_sensitive()
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        CandidateMetadataCodec.Write(
            line,
            new CandidateEntityMetadata("CaseSensitive", "primary"));

        Assert.True(CandidateMetadataCodec.TryRead(line, out var metadata));
        Assert.NotEqual("casesensitive", metadata.CandidateId);
        Assert.Equal("CaseSensitive", metadata.CandidateId);
    }

    private static Line WithCandidateData(params ExtendedDataRecord[] records)
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        line.ExtendedData.Add(
            CandidateMetadataCodec.AppId,
            new ExtendedData(records));
        return line;
    }
}
