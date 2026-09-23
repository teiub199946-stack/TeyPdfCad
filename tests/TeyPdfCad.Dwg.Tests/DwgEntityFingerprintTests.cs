using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DwgEntityFingerprintTests
{
    [Fact]
    public void Dimension_style_fingerprint_covers_visual_style_fields()
    {
        var baseline = Fingerprint(CreateStyle());

        var dimensionColor = CreateStyle();
        dimensionColor.DimensionLineColor = Color.Red;

        var extensionColor = CreateStyle();
        extensionColor.ExtensionLineColor = Color.Blue;

        var textColor = CreateStyle();
        textColor.TextColor = Color.Green;

        var dimensionWeight = CreateStyle();
        dimensionWeight.DimensionLineWeight = LineWeightType.W9;

        var extensionWeight = CreateStyle();
        extensionWeight.ExtensionLineWeight = LineWeightType.W9;

        var separateArrows = CreateStyle();
        separateArrows.SeparateArrowBlocks = true;

        var differentTextStyle = CreateStyle();
        differentTextStyle.Style = new TextStyle("TEYPDFCAD_OTHER_TEXT")
        {
            Filename = "romans.shx",
            Height = 0d,
            Width = 0.8d
        };

        Assert.NotEqual(baseline, Fingerprint(dimensionColor));
        Assert.NotEqual(baseline, Fingerprint(extensionColor));
        Assert.NotEqual(baseline, Fingerprint(textColor));
        Assert.NotEqual(baseline, Fingerprint(dimensionWeight));
        Assert.NotEqual(baseline, Fingerprint(extensionWeight));
        Assert.NotEqual(baseline, Fingerprint(separateArrows));
        Assert.NotEqual(baseline, Fingerprint(differentTextStyle));
    }

    [Fact]
    public void Dimension_geometry_fingerprint_covers_native_text_placement_and_rotation()
    {
        var baseline = new DimensionAligned(
            new CSMath.XYZ(0, 0, 0),
            new CSMath.XYZ(10, 0, 0))
        {
            DefinitionPoint = new CSMath.XYZ(5, 5, 0),
            Style = CreateStyle()
        };
        var moved = new DimensionAligned(
            new CSMath.XYZ(0, 0, 0),
            new CSMath.XYZ(10, 0, 0))
        {
            DefinitionPoint = new CSMath.XYZ(5, 5, 0),
            Style = CreateStyle(),
            TextMiddlePoint = new CSMath.XYZ(8, 7, 0),
            TextRotation = Math.PI / 6d,
            IsTextUserDefinedLocation = true
        };

        Assert.NotEqual(
            DwgEntityFingerprint.ComputeGeometry(baseline),
            DwgEntityFingerprint.ComputeGeometry(moved));
    }

    [Fact]
    public void Dimension_geometry_fingerprint_covers_ocs_normal_for_text_coordinates()
    {
        var positiveZ = new DimensionAligned(
            new CSMath.XYZ(0, 0, 0),
            new CSMath.XYZ(10, 0, 0))
        {
            DefinitionPoint = new CSMath.XYZ(5, 5, 0),
            TextMiddlePoint = new CSMath.XYZ(5, 5, 0),
            IsTextUserDefinedLocation = true,
            Normal = new CSMath.XYZ(0, 0, 1),
            Style = CreateStyle()
        };
        var negativeZ = new DimensionAligned(
            new CSMath.XYZ(0, 0, 0),
            new CSMath.XYZ(10, 0, 0))
        {
            DefinitionPoint = new CSMath.XYZ(5, 5, 0),
            TextMiddlePoint = new CSMath.XYZ(5, 5, 0),
            IsTextUserDefinedLocation = true,
            Normal = new CSMath.XYZ(0, 0, -1),
            Style = CreateStyle()
        };

        Assert.NotEqual(
            DwgEntityFingerprint.ComputeGeometry(positiveZ),
            DwgEntityFingerprint.ComputeGeometry(negativeZ));
    }

    private static DimensionStyle CreateStyle()
        => new("TEYPDFCAD_SCALE_1")
        {
            LinearScaleFactor = 1d,
            TextHeight = 2.5d,
            ArrowSize = 2.5d,
            ExtensionLineOffset = 0.75d,
            ExtensionLineExtension = 1.25d,
            ScaleFactor = 1d,
            Style = new TextStyle("TEYPDFCAD_TEXT")
            {
                Filename = "arial.ttf",
                Height = 0d,
                Width = 1d
            }
        };

    private static string Fingerprint(DimensionStyle style)
    {
        var dimension = new DimensionAligned(
            new CSMath.XYZ(0, 0, 0),
            new CSMath.XYZ(10, 0, 0))
        {
            DefinitionPoint = new CSMath.XYZ(5, 5, 0),
            Style = style
        };

        return DwgEntityFingerprint.ComputeDimensionStyle(dimension);
    }
}
