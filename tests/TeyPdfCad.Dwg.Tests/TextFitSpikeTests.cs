using System.Security.Cryptography;
using System.Text.Json;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class TextFitSpikeTests
{
    private const string DefaultArtifactRoot =
        @"C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-text-fit-report";

    private const string Value = "NATIVE FIT";
    private const double Height = 5d;
    private const double Epsilon = 1e-9;

    [Fact]
    public void Native_text_fit_candidates_survive_save_reopen_with_structural_metrics()
    {
        var artifactRoot = GetArtifactRoot();
        Directory.CreateDirectory(artifactRoot);

        var fixture = CreateFixtureMetadata();
        var fixtureMetadataPath = Path.Combine(artifactRoot, "fixture-metadata.json");
        File.WriteAllText(
            fixtureMetadataPath,
            JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true }));

        var referencePath = Path.Combine(artifactRoot, "natural-width-reference.dwg");
        var horizontal = fixture.Orientations[0];
        var naturalWidthReference = CreateText(
            horizontal.Start,
            horizontal.AngleRadians,
            TextCandidateKind.Reference,
            widthFactor: 1d);
        Save(new[] { naturalWidthReference }, referencePath);
        var reopenedReference = ReadSingleText(referencePath);
        var naturalWidth = reopenedReference.GetBoundingBox().LengthX;
        var referenceWidthFactor = reopenedReference.WidthFactor;

        var candidates = new List<CandidateSummary>();
        foreach (var orientation in fixture.Orientations)
        {
            var targetLength = Distance(orientation.Start, orientation.End);
            var widthFactor = naturalWidth > Epsilon
                ? targetLength / naturalWidth
                : referenceWidthFactor;
            Assert.True(widthFactor > 0d && double.IsFinite(widthFactor));

            foreach (var kind in new[]
            {
                TextCandidateKind.Default,
                TextCandidateKind.Fit,
                TextCandidateKind.CalibratedWidthFactor
            })
            {
                var candidate = CreateText(
                    orientation.Start,
                    orientation.AngleRadians,
                    kind,
                    widthFactor);
                if (kind == TextCandidateKind.Fit)
                {
                    candidate.HorizontalAlignment = TextHorizontalAlignment.Fit;
                    candidate.AlignmentPoint = orientation.End;
                }

                var fileName = $"{orientation.Name}-{kind.ToString().ToLowerInvariant()}.dwg";
                var path = Path.Combine(artifactRoot, fileName);
                Save(new[] { candidate }, path);
                var reopened = ReadSingleText(path);

                Assert.Equal(Value, reopened.Value);
                Assert.Equal(Height, reopened.Height, 12);
                Assert.Equal(orientation.AngleRadians, reopened.Rotation, 12);
                AssertPointEqual(orientation.Start, reopened.InsertPoint);
                Assert.Equal(
                    kind == TextCandidateKind.Fit
                        ? TextHorizontalAlignment.Fit
                        : TextHorizontalAlignment.Left,
                    reopened.HorizontalAlignment);

                if (kind == TextCandidateKind.Fit)
                {
                    AssertPointEqual(orientation.End, reopened.AlignmentPoint);
                }

                if (kind == TextCandidateKind.CalibratedWidthFactor)
                {
                    Assert.Equal(widthFactor, reopened.WidthFactor, 12);
                }

                candidates.Add(new CandidateSummary(
                    orientation.Name,
                    kind.ToString(),
                    path,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                    new StructuralMetrics(
                        BaselineStartError: Distance(orientation.Start, reopened.InsertPoint),
                        BaselineEndError: kind == TextCandidateKind.Fit
                            ? Distance(orientation.End, reopened.AlignmentPoint)
                            : null,
                        HeightError: Math.Abs(Height - reopened.Height),
                        AngleErrorRadians: NormalizeAngleError(
                            orientation.AngleRadians,
                            reopened.Rotation),
                        WordMaskIoU: null,
                        CharacterOrder: null),
                    new ReadBackMetrics(
                        reopened.Value,
                        reopened.Height,
                        reopened.Rotation,
                        reopened.InsertPoint,
                        reopened.HorizontalAlignment.ToString(),
                        reopened.AlignmentPoint,
                        reopened.WidthFactor),
                    kind == TextCandidateKind.CalibratedWidthFactor
                        ? "configured-from-acadsharp-natural-width-reference; rendered calibration pending"
                        : "structural-only; rendered extents pending"));
            }
        }

        var summary = new SpikeSummary(
            "ACadSharp 3.7.1",
            artifactRoot,
            fixtureMetadataPath,
            fixture,
            new NaturalWidthReference(
                referencePath,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(referencePath))).ToLowerInvariant(),
                naturalWidth,
                referenceWidthFactor,
                naturalWidth > Epsilon
                    ? "ACadSharp TextEntity.GetBoundingBox().LengthX; not a renderer measurement"
                    : "ACadSharp TextEntity.GetBoundingBox().LengthX unavailable; persisted reference WidthFactor used and visual calibration is pending"),
            candidates);

        File.WriteAllText(
            Path.Combine(artifactRoot, "read-back-summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal(
            fixture.Orientations.Count * 3,
            candidates.Count);
    }

    private static FixtureMetadata CreateFixtureMetadata()
        => new(
            Value,
            Height,
            [
                new OrientationMetadata(
                    "horizontal",
                    new XYZ(10d, 20d, 0d),
                    new XYZ(45d, 20d, 0d),
                    0d),
                new OrientationMetadata(
                    "rotated-90",
                    new XYZ(60d, 20d, 0d),
                    new XYZ(60d, 55d, 0d),
                    Math.PI / 2d)
            ]);

    private static TextEntity CreateText(
        XYZ start,
        double angleRadians,
        TextCandidateKind kind,
        double widthFactor)
    {
        var text = new TextEntity
        {
            Value = Value,
            InsertPoint = start,
            Height = Height,
            Rotation = angleRadians
        };

        if (kind == TextCandidateKind.CalibratedWidthFactor)
        {
            text.WidthFactor = widthFactor;
        }

        return text;
    }

    private static string GetArtifactRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("TEYPDFCAD_TEXT_FIT_ARTIFACT_ROOT");
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? DefaultArtifactRoot
            : Path.GetFullPath(configuredRoot);
    }

    private static void Save(IReadOnlyCollection<TextEntity> entities, string path)
    {
        var document = new CadDocument();
        foreach (var entity in entities)
        {
            document.Entities.Add(entity);
        }

        using var stream = File.Create(path);
        using var writer = new DwgWriter(stream, document);
        writer.Write();
    }

    private static TextEntity ReadSingleText(string path)
    {
        using var stream = File.OpenRead(path);
        var document = DwgReader.Read(stream);
        return Assert.Single(document.Entities.OfType<TextEntity>());
    }

    private static void AssertPointEqual(XYZ expected, XYZ actual)
    {
        Assert.Equal(expected.X, actual.X, 12);
        Assert.Equal(expected.Y, actual.Y, 12);
        Assert.Equal(expected.Z, actual.Z, 12);
    }

    private static double Distance(XYZ left, XYZ right)
        => Math.Sqrt(
            Math.Pow(left.X - right.X, 2d)
            + Math.Pow(left.Y - right.Y, 2d)
            + Math.Pow(left.Z - right.Z, 2d));

    private static double NormalizeAngleError(double expected, double actual)
    {
        var error = Math.Abs(expected - actual) % (2d * Math.PI);
        return Math.Min(error, 2d * Math.PI - error);
    }

    private enum TextCandidateKind
    {
        Reference,
        Default,
        Fit,
        CalibratedWidthFactor
    }

    private sealed record FixtureMetadata(
        string Value,
        double Height,
        IReadOnlyList<OrientationMetadata> Orientations);

    private sealed record OrientationMetadata(
        string Name,
        XYZ Start,
        XYZ End,
        double AngleRadians)
    {
        public double BaselineLength => Distance(Start, End);
    }

    private sealed record NaturalWidthReference(
        string DwgPath,
        string DwgSha256,
        double NaturalWidth,
        double PersistedWidthFactor,
        string MeasurementMethod);

    private sealed record StructuralMetrics(
        double BaselineStartError,
        double? BaselineEndError,
        double HeightError,
        double AngleErrorRadians,
        double? WordMaskIoU,
        string? CharacterOrder);

    private sealed record ReadBackMetrics(
        string Value,
        double Height,
        double RotationRadians,
        XYZ InsertPoint,
        string HorizontalAlignment,
        XYZ AlignmentPoint,
        double WidthFactor);

    private sealed record CandidateSummary(
        string Orientation,
        string Candidate,
        string DwgPath,
        string DwgSha256,
        StructuralMetrics Metrics,
        ReadBackMetrics ReadBack,
        string VisualStatus);

    private sealed record SpikeSummary(
        string AcadSharpPackage,
        string ArtifactRoot,
        string FixtureMetadataPath,
        FixtureMetadata Fixture,
        NaturalWidthReference NaturalWidthReference,
        IReadOnlyList<CandidateSummary> Candidates);
}
