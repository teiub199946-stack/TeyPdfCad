using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

/// <summary>
/// Spike B: verifies only that ACadSharp 3.7.1 persists TEXT layout fields.
/// It intentionally makes no visual-width claim; AutoCAD fixtures are the visual gate.
/// </summary>
public sealed class TextLayoutSpikeTests
{
    public static TheoryData<TextLayoutMode, double> LayoutModesAndRotations => new()
    {
        { TextLayoutMode.PlainLeft, 0d },
        { TextLayoutMode.PlainLeft, Math.PI / 4d },
        { TextLayoutMode.PlainLeft, Math.PI / 2d },
        { TextLayoutMode.WidthFactor, 0d },
        { TextLayoutMode.WidthFactor, Math.PI / 4d },
        { TextLayoutMode.WidthFactor, Math.PI / 2d },
        { TextLayoutMode.Fit, 0d },
        { TextLayoutMode.Fit, Math.PI / 4d },
        { TextLayoutMode.Fit, Math.PI / 2d }
    };

    [Theory]
    [MemberData(nameof(LayoutModesAndRotations))]
    public void AcadsSharp_round_trip_preserves_explicit_text_layout(
        TextLayoutMode mode,
        double rotation)
    {
        var insertPoint = new XYZ(100d, 200d, 0d);
        const double baselineLength = 37d;
        var alignmentPoint = new XYZ(
            insertPoint.X + baselineLength * Math.Cos(rotation),
            insertPoint.Y + baselineLength * Math.Sin(rotation),
            0d);
        var expectedWidthFactor = mode == TextLayoutMode.WidthFactor ? 0.8d : 1d;
        var expectedHorizontalAlignment = mode == TextLayoutMode.Fit
            ? TextHorizontalAlignment.Fit
            : TextHorizontalAlignment.Left;

        var document = new CadDocument();
        document.Entities.Add(new TextEntity
        {
            Value = "ABCDE",
            InsertPoint = insertPoint,
            AlignmentPoint = mode == TextLayoutMode.Fit ? alignmentPoint : XYZ.Zero,
            Height = 5d,
            Rotation = rotation,
            HorizontalAlignment = expectedHorizontalAlignment,
            VerticalAlignment = TextVerticalAlignmentType.Baseline,
            WidthFactor = expectedWidthFactor
        });

        using var bytes = new MemoryStream();
        using (var writer = new DwgWriter(bytes, document))
        {
            writer.Write();
        }

        var payload = bytes.ToArray();
        var reopened = DwgReader.Read(new MemoryStream(payload));
        var text = Assert.Single(reopened.Entities.OfType<TextEntity>());

        Assert.Equal(insertPoint.X, text.InsertPoint.X, 6);
        Assert.Equal(insertPoint.Y, text.InsertPoint.Y, 6);
        Assert.Equal(rotation, text.Rotation, 6);
        Assert.Equal(expectedHorizontalAlignment, text.HorizontalAlignment);
        Assert.Equal(TextVerticalAlignmentType.Baseline, text.VerticalAlignment);
        Assert.Equal(expectedWidthFactor, text.WidthFactor, 6);

        if (mode == TextLayoutMode.Fit)
        {
            Assert.Equal(alignmentPoint.X, text.AlignmentPoint.X, 6);
            Assert.Equal(alignmentPoint.Y, text.AlignmentPoint.Y, 6);
        }
    }

    public enum TextLayoutMode
    {
        PlainLeft,
        WidthFactor,
        Fit
    }
}
