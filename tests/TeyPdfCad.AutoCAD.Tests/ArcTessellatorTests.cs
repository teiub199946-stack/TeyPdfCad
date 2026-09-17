using Xunit;

namespace TeyPdfCad.AutoCAD.Tests;

public sealed class ArcTessellatorTests
{
    [Fact]
    public void Semicircle_IsSplitIntoDeterministicTenDegreeChords()
    {
        var steps = ArcTessellator.BuildSteps(0, Math.PI, "2C0#segment:1");
        var last = steps[steps.Count - 1];

        Assert.Equal(18, steps.Count);
        Assert.Equal(0, steps[0].StartParameter, 12);
        Assert.Equal(Math.PI, last.EndParameter, 12);
        Assert.Equal("2C0#segment:1#arc:0", steps[0].SourceId);
        Assert.Equal("2C0#segment:1#arc:17", last.SourceId);

        for (var i = 1; i < steps.Count; i++)
            Assert.Equal(steps[i - 1].EndParameter, steps[i].StartParameter, 12);
    }

    [Fact]
    public void ShortArc_StillProducesOneChordAndPreservesEndpoints()
    {
        var start = Math.PI / 6.0;
        var end = start + Math.PI / 36.0;

        var steps = ArcTessellator.BuildSteps(start, end, "ABC#segment:4");

        var step = Assert.Single(steps);
        Assert.Equal(start, step.StartParameter, 12);
        Assert.Equal(end, step.EndParameter, 12);
        Assert.Equal("ABC#segment:4#arc:0", step.SourceId);
    }

    [Fact]
    public void FullCircle_IsBoundedAndDeterministic()
    {
        var steps = ArcTessellator.BuildSteps(0, Math.PI * 2.0, "circle#segment:0");
        var last = steps[steps.Count - 1];

        Assert.Equal(36, steps.Count);
        Assert.Equal("circle#segment:0#arc:35", last.SourceId);
        Assert.All(steps, step => Assert.True(step.EndParameter > step.StartParameter));
    }
}
