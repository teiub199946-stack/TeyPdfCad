using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Primitives;
using TeyPdfCad.Core.Recognition;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class LeaderRecognizerTests
{
    [Fact]
    public void Structural_intersection_is_not_an_arrowhead()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new(new(0, 0), new(0, 180), SourceIds: ["column"]));
        scene.Lines.Add(new(new(0, 0), new(60, 0), SourceIds: ["beam"]));
        scene.Lines.Add(new(new(0, 0), new(-60, 0), SourceIds: ["beam2"]));
        scene.Texts.Add(new("арматура", new(3, 182), 2.5, 0));
        Assert.Empty(new LeaderRecognizer().Recognize(scene).NativeLeaders);
    }

    [Fact]
    public void Unrelated_long_line_at_arrow_tip_is_not_consumed()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new(new(0, 0), new(30, 0), SourceIds: ["shaft"]));
        scene.Lines.Add(new(new(0, 0), new(4, 2), SourceIds: ["a"]));
        scene.Lines.Add(new(new(0, 0), new(4, -2), SourceIds: ["b"]));
        scene.Lines.Add(new(new(0, 0), new(0, 100), SourceIds: ["wall"]));
        scene.Texts.Add(new("Note", new(31, 1), 2.5, 0));
        var leader = Assert.Single(new LeaderRecognizer().Recognize(scene).NativeLeaders);
        Assert.DoesNotContain("wall", leader.ProvenanceIds);
    }

    [Fact]
    public void Arrow_text_and_shaft_create_a_high_confidence_leader()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(30, 0), SourceIds: ["shaft"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(4, 2), SourceIds: ["arrow-a"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(4, -2), SourceIds: ["arrow-b"]));
        scene.Texts.Add(new TextPrimitive("Позиция 1", new Point2(31, 1), 2.5, 0, SourceIds: ["note"]));

        var result = new LeaderRecognizer().Recognize(scene);

        var leader = Assert.Single(result.NativeLeaders);
        Assert.Equal("Позиция 1", leader.Text);
        Assert.True(leader.Confidence >= 0.85);
        Assert.Equal(4, leader.ProvenanceIds.Count);
    }

    [Fact]
    public void Ambiguous_arrow_and_text_stay_geometry()
    {
        var scene = new PrimitiveScene();
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(30, 0), SourceIds: ["shaft"]));
        scene.Lines.Add(new LinePrimitive(new Point2(0, 0), new Point2(4, 2), SourceIds: ["arrow-a"]));
        scene.Texts.Add(new TextPrimitive("Позиция 1", new Point2(31, 1), 2.5, 0, SourceIds: ["note"]));

        var result = new LeaderRecognizer().Recognize(scene);

        Assert.Empty(result.NativeLeaders);
        Assert.Contains(result.Warnings, warning => warning.Code == "leader-low-confidence");
    }
}
