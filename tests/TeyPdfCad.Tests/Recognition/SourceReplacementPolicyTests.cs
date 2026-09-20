using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class SourceReplacementPolicyTests
{
    [Fact]
    public void Removes_only_whole_objects_with_validated_replacements()
    {
        var result = SourceReplacementPolicy.SelectWholeObjects(
            ["A1", "A2", "A3"], ["A1", "A2#segment:0"]);
        Assert.Equal(["A1"], result);
    }

    [Fact]
    public void No_validated_replacements_preserves_every_object()
    {
        Assert.Empty(SourceReplacementPolicy.SelectWholeObjects(["A1", "A2"], []));
    }

    [Fact]
    public void Unknown_provenance_cannot_delete_another_object()
    {
        Assert.Empty(SourceReplacementPolicy.SelectWholeObjects(["A1"], ["A10", "A1#segment:2"]));
    }
}
