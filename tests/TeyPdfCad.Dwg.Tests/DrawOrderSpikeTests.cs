using System.Text.Json;
using System.Security.Cryptography;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DrawOrderSpikeTests
{
    private const string DefaultArtifactRoot =
        @"C:\Users\Admin\Documents\ChatGPT\TeyConvert\output\text-fidelity-review\spike-draw-order-report";

    private const string RedLine = "red-line";
    private const string WhiteHatch = "white-solid-hatch";
    private const string BlackText = "black-text";

    [Fact]
    public void Draw_order_spike_compares_insertion_order_with_sort_entities_table_after_reopen()
    {
        var mode = Environment.GetEnvironmentVariable("TEYPDFCAD_DRAW_ORDER_SPIKE_MODE") ?? "Both";
        var variants = mode switch
        {
            "InsertionOrder" => new[] { DrawOrderVariant.InsertionOrder },
            "ExplicitOrder" => new[] { DrawOrderVariant.ExplicitOrder },
            "Both" => new[] { DrawOrderVariant.InsertionOrder, DrawOrderVariant.ExplicitOrder },
            _ => throw new ArgumentException($"Unsupported draw-order spike mode: {mode}.")
        };

        var artifactRoot = GetArtifactRoot();
        Directory.CreateDirectory(artifactRoot);
        var summaries = new List<VariantSummary>();

        foreach (var variant in variants)
        {
            var path = Path.Combine(artifactRoot, $"{GetFileStem(variant)}.dwg");
            var document = CreateFixture(variant);
            var sourceOrder = document.Entities.Select(GetFixtureLabel).ToArray();

            if (variant == DrawOrderVariant.ExplicitOrder)
            {
                ApplyExplicitOrder(document);
            }

            Save(document, path);
            var dwgSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Assert.Matches("^[0-9a-f]{64}$", dwgSha256);
            var reopened = Read(path);
            var reopenedEntities = reopened.Entities.ToArray();
            AssertFixtureSurvived(reopenedEntities);

            var insertionOrder = reopenedEntities.Select(GetFixtureLabel).ToArray();
            var sortOrder = ReadSortOrder(reopened);
            if (variant == DrawOrderVariant.ExplicitOrder)
            {
                Assert.Equal(new[] { BlackText, WhiteHatch, RedLine }, sortOrder);
            }

            summaries.Add(new VariantSummary(
                variant.ToString(),
                path,
                sourceOrder,
                insertionOrder,
                sortOrder,
                insertionOrder.SequenceEqual(sourceOrder, StringComparer.Ordinal),
                sortOrder.Length > 0,
                dwgSha256));
        }

        File.WriteAllText(
            Path.Combine(artifactRoot, "read-back-summary.json"),
            JsonSerializer.Serialize(
                new
                {
                    acadSharpPackage = "3.7.1",
                    artifactRoot,
                    variants = summaries
                },
                new JsonSerializerOptions { WriteIndented = true }));

        Assert.Equal(variants.Length, summaries.Count);
    }

    private static string GetArtifactRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("TEYPDFCAD_DRAW_ORDER_ARTIFACT_ROOT");
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? DefaultArtifactRoot
            : Path.GetFullPath(configuredRoot);
    }

    private static CadDocument CreateFixture(DrawOrderVariant variant)
    {
        var document = new CadDocument();
        var lineLayer = AddLayer(document, "SPIKE_RED_LINE");
        var hatchLayer = AddLayer(document, "SPIKE_WHITE_HATCH");
        var textLayer = AddLayer(document, "SPIKE_BLACK_TEXT");

        var line = new Line(new XYZ(0d, 5d, 0d), new XYZ(30d, 5d, 0d))
        {
            Layer = lineLayer,
            Color = Color.FromTrueColor(0xFF0000)
        };

        var hatchBoundary = new LwPolyline(
            [
                new XY(0d, 0d),
                new XY(30d, 0d),
                new XY(30d, 10d),
                new XY(0d, 10d)
            ])
        {
            IsClosed = true
        };
        var hatch = new Hatch
        {
            IsSolid = true,
            Pattern = HatchPattern.Solid,
            Layer = hatchLayer,
            Color = Color.FromTrueColor(0xFFFFFF),
            SeedPoints = [new XY(15d, 5d)]
        };
        hatch.Paths.Add(new Hatch.BoundaryPath([hatchBoundary]));

        var text = new TextEntity
        {
            Value = "BLACK TEXT OVER HATCH",
            InsertPoint = new XYZ(8d, 4d, 0d),
            Height = 2d,
            Layer = textLayer,
            Color = Color.FromTrueColor(0x000000)
        };

        if (variant == DrawOrderVariant.InsertionOrder)
        {
            document.Entities.Add(line);
            document.Entities.Add(hatch);
            document.Entities.Add(text);
        }
        else
        {
            document.Entities.Add(text);
            document.Entities.Add(hatch);
            document.Entities.Add(line);
        }

        return document;
    }

    private static void ApplyExplicitOrder(CadDocument document)
    {
        var line = Assert.Single(document.Entities.OfType<Line>());
        var hatch = Assert.Single(document.Entities.OfType<Hatch>());
        var text = Assert.Single(document.Entities.OfType<TextEntity>());

        document.ModelSpace.CreateSortEntitiesTable();
        var sortEntitiesTable = document.ModelSpace.SortEntitiesTable;
        Assert.NotNull(sortEntitiesTable);
        sortEntitiesTable.MoveToBottom(line);
        sortEntitiesTable.MoveToTop(hatch);
        sortEntitiesTable.MoveToTop(text);
    }

    private static Layer AddLayer(CadDocument document, string name)
    {
        var layer = new Layer(name);
        document.Layers.Add(layer);
        return layer;
    }

    private static void Save(CadDocument document, string path)
    {
        using var stream = File.Create(path);
        using var writer = new DwgWriter(stream, document);
        writer.Write();
    }

    private static CadDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        return DwgReader.Read(stream);
    }

    private static string[] ReadSortOrder(CadDocument document)
    {
        var table = document.ModelSpace.SortEntitiesTable;
        return table is null
            ? []
            : table.Select(sorter => GetFixtureLabel(sorter.Entity)).ToArray();
    }

    private static void AssertFixtureSurvived(IReadOnlyCollection<Entity> entities)
    {
        Assert.Equal(3, entities.Count);

        var line = Assert.Single(entities.OfType<Line>());
        Assert.Equal(0xFF0000, line.Color.TrueColor);

        var hatch = Assert.Single(entities.OfType<Hatch>());
        Assert.True(hatch.IsSolid);
        Assert.Equal(0xFFFFFF, hatch.Color.TrueColor);
        Assert.NotEmpty(hatch.Paths);

        var text = Assert.Single(entities.OfType<TextEntity>());
        Assert.Equal("BLACK TEXT OVER HATCH", text.Value);
        Assert.Equal(0x000000, text.Color.TrueColor);
    }

    private static string GetFixtureLabel(Entity entity)
    {
        return entity.Layer?.Name switch
        {
            "SPIKE_RED_LINE" => RedLine,
            "SPIKE_WHITE_HATCH" => WhiteHatch,
            "SPIKE_BLACK_TEXT" => BlackText,
            _ => entity switch
            {
                TextEntity text when text.Value == "BLACK TEXT OVER HATCH" => BlackText,
                _ => throw new InvalidOperationException(
                    $"Unexpected fixture entity: {entity.GetType().FullName}.")
            }
        };
    }

    private static string GetFileStem(DrawOrderVariant variant) => variant switch
    {
        DrawOrderVariant.InsertionOrder => "insertion-order",
        DrawOrderVariant.ExplicitOrder => "explicit-order",
        _ => throw new InvalidOperationException($"Unknown variant: {variant}.")
    };

    private enum DrawOrderVariant
    {
        InsertionOrder,
        ExplicitOrder
    }

    private sealed record VariantSummary(
        string Variant,
        string DwgPath,
        string[] SourceOrder,
        string[] ReopenedInsertionOrder,
        string[] ReopenedSortOrder,
        bool InsertionOrderSurvived,
        bool SortEntitiesTablePresent,
        string DwgSha256)
    {
    }
}
