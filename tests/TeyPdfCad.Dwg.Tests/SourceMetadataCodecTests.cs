using ACadSharp.Entities;
using ACadSharp.XData;
using CSMath;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class SourceMetadataCodecTests
{
    [Fact]
    public void Page_source_identity_round_trips_exactly()
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));

        SourceMetadataCodec.Write(
            line,
            new PageSourceRef(7, "source-A"));

        Assert.True(SourceMetadataCodec.TryRead(line, out var source));
        Assert.Equal(7, source.PageNumber);
        Assert.Equal("source-A", source.SourceId);
    }

    [Fact]
    public void Identical_geometry_with_different_source_identity_has_different_output_fingerprint()
    {
        var first = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var second = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        SourceMetadataCodec.Write(first, new PageSourceRef(1, "A"));
        SourceMetadataCodec.Write(second, new PageSourceRef(1, "B"));

        Assert.NotEqual(
            DwgEntityFingerprint.ComputeOutput(first),
            DwgEntityFingerprint.ComputeOutput(second));
    }

    [Fact]
    public void Same_raw_source_id_on_different_pages_has_different_output_fingerprint()
    {
        var first = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        var second = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        SourceMetadataCodec.Write(first, new PageSourceRef(1, "shared"));
        SourceMetadataCodec.Write(second, new PageSourceRef(2, "shared"));

        Assert.NotEqual(
            DwgEntityFingerprint.ComputeOutput(first),
            DwgEntityFingerprint.ComputeOutput(second));
    }

    [Theory]
    [InlineData(0, "source")]
    [InlineData(-1, "source")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    public void Invalid_page_source_identity_is_rejected(
        int pageNumber,
        string sourceId)
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));

        Assert.Throws<ArgumentException>(() =>
            SourceMetadataCodec.Write(
                line,
                new PageSourceRef(pageNumber, sourceId)));
    }

    [Fact]
    public void Malformed_source_metadata_is_not_accepted()
    {
        var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
        line.ExtendedData.Add(
            SourceMetadataCodec.AppId,
            new ExtendedData(
            [
                new ExtendedDataString("not-a-page"),
                new ExtendedDataString("source")
            ]));

        Assert.False(SourceMetadataCodec.TryRead(line, out _));
    }
}
