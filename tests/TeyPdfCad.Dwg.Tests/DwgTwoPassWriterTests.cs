using ACadSharp.Entities;
using ACadSharp.IO;
using TeyPdfCad.Core.Conversion;
using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class DwgTwoPassWriterTests
{
    [Fact]
    public void Probe_write_returns_level_manifest_and_keeps_caller_stream_open()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine("level-line", new(10, 10), new(15, 10), new VectorStyle()),
            new VectorText("level-text", "+3.600", new(17, 10), 2.5, new VectorStyle())
        ]);
        var document = new VectorPdfDocument([page]);
        var level = new LevelCandidate(
            new(10, 10),
            new(17, 10),
            "+3.600",
            0.95,
            ["level-line", "level-text"])
        {
            SourceClaims =
            [
                new("level-line", SourceUsageRole.LevelMarker, SourceClaimState.Valid, false),
                new("level-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Levels = [level]
        };
        var hatch = new HatchRecognizer().Recognize(page.Entities, page.Number);
        var replacement = new SourceReplacementPlanner().BuildPlan(
            page.Entities,
            semantics,
            hatch,
            page.Number);

        using var stream = new MemoryStream();
        var result = new AcadSharpDwgWriter().Write(
            stream,
            document,
            new DocumentLayoutPlanner().Create(document),
            hatchRecognitionByPage: new Dictionary<int, HatchRecognitionResult> { [1] = hatch },
            semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics },
            sourceReplacementPlansByPage: new Dictionary<int, SourceReplacementPlan> { [1] = replacement });

        Assert.True(stream.CanWrite);
        Assert.True(stream.Length > 0);

        var candidateId = SourceReplacementPlanner.GetCandidateKey(level, 1);
        var expected = Assert.Single(result.Manifest.Candidates, pair => pair.Key == candidateId).Value;
        Assert.Equal("LEVEL", expected.SemanticType);
        Assert.False(expected.SourceEquivalenceComplete);
        Assert.Contains("marker", expected.SourceEquivalenceReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "attribute", "primary" }, expected.Entities.Select(entity => entity.Role).OrderBy(x => x));

        stream.Position = 0;
        var drawing = DwgReader.Read(stream);
        var insert = Assert.Single(drawing.Entities.OfType<Insert>());
        Assert.True(CandidateMetadataCodec.TryRead(insert, out var insertMetadata));
        Assert.Equal(candidateId, insertMetadata.CandidateId);
        Assert.Equal("primary", insertMetadata.Role);

        var attribute = Assert.Single(insert.Attributes);
        Assert.True(CandidateMetadataCodec.TryRead(attribute, out var attributeMetadata));
        Assert.Equal(candidateId, attributeMetadata.CandidateId);
        Assert.Equal("attribute", attributeMetadata.Role);

        Assert.Contains(new PageSourceRef(1, "level-line"), result.SourceEmissionSummary.OutputFingerprintCountsBySource.Keys);
        Assert.Contains(new PageSourceRef(1, "level-text"), result.SourceEmissionSummary.OutputFingerprintCountsBySource.Keys);
    }

    [Fact]
    public void Filled_source_records_every_emitted_source_entity()
    {
        var fill = new VectorFilledPath(
            "fill",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)],
            VectorFillRule.NonZero,
            new VectorStyle("FILL"));
        var page = new VectorPdfPage(1, 72, 72, 0, [fill]);
        var document = new VectorPdfDocument([page]);

        using var stream = new MemoryStream();
        var result = new AcadSharpDwgWriter().Write(
            stream,
            document,
            new DocumentLayoutPlanner().Create(document));

        var source = new PageSourceRef(1, "fill");
        Assert.True(result.SourceEmissionSummary.OutputFingerprintCountsBySource.TryGetValue(source, out var counts));
        Assert.True(counts.Values.Sum() >= 2);
    }

    [Fact]
    public void Authorization_is_page_scoped_when_raw_source_ids_repeat_between_pages()
    {
        var firstPage = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine("shared", new(0, 0), new(10, 0), new VectorStyle("P1"))
        ]);
        var secondPage = new VectorPdfPage(2, 72, 72, 0,
        [
            new VectorLine("shared", new(0, 0), new(20, 0), new VectorStyle("P2"))
        ]);
        var document = new VectorPdfDocument([firstPage, secondPage]);

        var plans = new Dictionary<int, SourceReplacementPlan>
        {
            [1] = new(
                ["shared"],
                [],
                [],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["shared"] = ["verified-candidate"]
                },
                [],
                []),
            [2] = new(
                [],
                ["shared"],
                [],
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
                [],
                [])
        };

        using var stream = new MemoryStream();
        _ = new AcadSharpDwgWriter().Write(
            stream,
            document,
            new DocumentLayoutPlanner().Create(document),
            sourceReplacementPlansByPage: plans,
            authorizedSuppressedSources: new HashSet<PageSourceRef>
            {
                new(1, "shared")
            });

        stream.Position = 0;
        var drawing = DwgReader.Read(stream);
        var line = Assert.Single(drawing.Entities.OfType<Line>());
        Assert.Equal(20d, line.EndPoint.X - line.StartPoint.X, 6);
    }

    [Fact]
    public void Null_authorization_preserves_same_raw_source_id_on_both_pages()
    {
        var document = new VectorPdfDocument(
        [
            new VectorPdfPage(1, 72, 72, 0,
            [
                new VectorLine("shared", new(0, 0), new(10, 0), new VectorStyle())
            ]),
            new VectorPdfPage(2, 72, 72, 0,
            [
                new VectorLine("shared", new(0, 0), new(20, 0), new VectorStyle())
            ])
        ]);

        using var stream = new MemoryStream();
        _ = new AcadSharpDwgWriter().Write(
            stream,
            document,
            new DocumentLayoutPlanner().Create(document));

        stream.Position = 0;
        var drawing = DwgReader.Read(stream);
        Assert.Equal(2, drawing.Entities.OfType<Line>().Count());
    }


    [Fact]
    public void Probe_manifest_verifies_only_after_closed_on_disk_round_trip()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine("axis", new(0, 0), new(25, 0), new VectorStyle())
        ]);
        var axis = new AxisCandidate(new(0, 0), new(25, 0), 0.95, ["axis"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Axes = [axis]
        };
        var hatch = new HatchRecognizer().Recognize(page.Entities, page.Number);
        var replacement = new SourceReplacementPlanner().BuildPlan(
            page.Entities,
            semantics,
            hatch,
            page.Number);
        var document = new VectorPdfDocument([page]);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.dwg");

        try
        {
            DwgWriteResult result;
            using (var stream = File.Create(path))
            {
                result = new AcadSharpDwgWriter().Write(
                    stream,
                    document,
                    new DocumentLayoutPlanner().Create(document),
                    hatchRecognitionByPage: new Dictionary<int, HatchRecognitionResult> { [1] = hatch },
                    semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics },
                    sourceReplacementPlansByPage: new Dictionary<int, SourceReplacementPlan> { [1] = replacement });
                Assert.True(stream.CanWrite);
            }

            var verification = new DwgReadBackVerifier().Verify(path, result.Manifest);
            var candidateId = SourceReplacementPlanner.GetCandidateKey(axis, 1);
            var candidate = verification.Candidates[candidateId];

            Assert.True(candidate.IsVerified);
            Assert.False(candidate.SourceEquivalenceComplete);
            Assert.Empty(candidate.MissingRoles);
            Assert.Empty(candidate.DuplicateRoles);
            Assert.Empty(candidate.InvalidEntities);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void Writer_paint_order_is_stable_when_source_enumeration_is_reversed()
    {
        VectorEntity[] forward =
        [
            new VectorLine("z-line", new(0, 0), new(20, 0), new VectorStyle("GEOM")),
            new VectorText("a-text", "TOP", new(5, 0), 2.5, new VectorStyle("TEXT"))
        ];
        var reversed = forward.AsEnumerable().Reverse().ToArray();

        var first = WriteAndRead(new VectorPdfDocument(
        [
            new VectorPdfPage(1, 72, 72, 0, forward)
        ]));
        var second = WriteAndRead(new VectorPdfDocument(
        [
            new VectorPdfPage(1, 72, 72, 0, reversed)
        ]));

        var firstOrder = first.ModelSpace.SortEntitiesTable!
            .Select(entry => DwgEntityFingerprint.ComputeOutput(entry.Entity))
            .ToArray();
        var secondOrder = second.ModelSpace.SortEntitiesTable!
            .Select(entry => DwgEntityFingerprint.ComputeOutput(entry.Entity))
            .ToArray();

        Assert.Equal(firstOrder, secondOrder);
    }



    [Fact]
    public void Verified_probe_candidate_still_preserves_source_when_equivalence_is_incomplete()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine("axis-source", new(0, 0), new(25, 0), new VectorStyle())
        ]);
        var axis = new AxisCandidate(new(0, 0), new(25, 0), 0.95, ["axis-source"])
        {
            SourceClaims =
            [
                new("axis-source", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };
        var semantics = new SemanticReconstructionResult([], [], null, 0d)
        {
            Axes = [axis]
        };
        var hatch = new HatchRecognizer().Recognize(page.Entities, page.Number);
        var plan = new SourceReplacementPlanner().BuildPlan(
            page.Entities,
            semantics,
            hatch,
            page.Number);
        var document = new VectorPdfDocument([page]);
        var layout = new DocumentLayoutPlanner().Create(document);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, "probe.dwg");
        var finalPath = Path.Combine(directory, "final.dwg");

        try
        {
            DwgWriteResult probe;
            using (var stream = File.Create(probePath))
            {
                probe = new AcadSharpDwgWriter().Write(
                    stream,
                    document,
                    layout,
                    hatchRecognitionByPage: new Dictionary<int, HatchRecognitionResult> { [1] = hatch },
                    semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult> { [1] = semantics },
                    sourceReplacementPlansByPage: new Dictionary<int, SourceReplacementPlan> { [1] = plan });
            }

            var verification = new DwgReadBackVerifier().Verify(probePath, probe.Manifest);
            var decision = new SuppressionGate().Evaluate(plan, verification);
            Assert.Empty(decision.SuppressSourceIds);
            Assert.Contains("axis-source", decision.PreserveSourceIds);
            Assert.Contains(decision.Residuals, residual =>
                residual.Kind == ReplacementResidualKind.SourceEquivalenceIncomplete
                && residual.Severity == ReplacementResidualSeverity.Critical);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Source_emission_fingerprints_are_present_in_on_disk_probe_inventory()
    {
        var fill = new VectorFilledPath(
            "fill",
            [new(0, 0), new(12, 0), new(12, 8), new(0, 8)],
            VectorFillRule.NonZero,
            new VectorStyle("FILL", 0x336699));
        var line = new VectorLine(
            "line",
            new(20, 0),
            new(35, 0),
            new VectorStyle("GEOM", StrokeWidthPoints: 0.5));
        var document = new VectorPdfDocument(
        [
            new VectorPdfPage(1, 72, 72, 0, [fill, line])
        ]);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.dwg");

        try
        {
            DwgWriteResult result;
            using (var stream = File.Create(path))
            {
                result = new AcadSharpDwgWriter().Write(
                    stream,
                    document,
                    new DocumentLayoutPlanner().Create(document));
            }

            var inventory = new DwgReadBackVerifier().ReadStructuralInventory(path);
            foreach (var source in result.SourceEmissionSummary.OutputFingerprintCountsBySource)
            {
                foreach (var expected in source.Value)
                {
                    Assert.True(
                        inventory.OutputFingerprintCounts.TryGetValue(expected.Key, out var actualCount),
                        $"On-disk probe is missing source fingerprint for {source.Key}: {expected.Key}");
                    Assert.True(
                        actualCount >= expected.Value,
                        $"On-disk probe count {actualCount} is below source emission count {expected.Value} for {source.Key}.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void Confident_hatch_round_trip_still_preserves_pattern_sources_when_equivalence_is_incomplete()
    {
        var boundary = new VectorPolyline(
            "boundary",
            [new(0, 0), new(20, 0), new(20, 20), new(0, 20)],
            true,
            new VectorStyle("ШТРИХОВКА"));
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            boundary,
            new VectorLine("h1", new(1, 4), new(19, 4), new VectorStyle()),
            new VectorLine("h2", new(1, 8), new(19, 8), new VectorStyle()),
            new VectorLine("h3", new(1, 12), new(19, 12), new VectorStyle())
        ]);
        var document = new VectorPdfDocument([page]);
        var semantics = new SemanticReconstructionResult([], [], null, 0d);
        var hatchRecognition = new HatchRecognizer().Recognize(page.Entities, page.Number);
        var nativeHatch = Assert.Single(
            hatchRecognition.NativeHatches,
            candidate => !candidate.IsSolid);
        var plan = new SourceReplacementPlanner().BuildPlan(
            page.Entities,
            semantics,
            hatchRecognition,
            page.Number);
        var layout = new DocumentLayoutPlanner().Create(document);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "TeyPdfCad.Dwg.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, "probe.dwg");
        var finalPath = Path.Combine(directory, "final.dwg");

        try
        {
            DwgWriteResult probe;
            using (var probeStream = File.Create(probePath))
            {
                probe = new AcadSharpDwgWriter().Write(
                    probeStream,
                    document,
                    layout,
                    hatchRecognitionByPage: new Dictionary<int, HatchRecognitionResult>
                    {
                        [1] = hatchRecognition
                    },
                    semanticRecognitionByPage: new Dictionary<int, SemanticReconstructionResult>
                    {
                        [1] = semantics
                    },
                    sourceReplacementPlansByPage: new Dictionary<int, SourceReplacementPlan>
                    {
                        [1] = plan
                    });
            }

            var verifier = new DwgReadBackVerifier();
            var verification = verifier.Verify(probePath, probe.Manifest);
            var candidateId = SourceReplacementPlanner.GetCandidateKey(nativeHatch, 1);
            Assert.True(verification.Candidates[candidateId].IsVerified);

            var decision = new SuppressionGate().Evaluate(plan, verification);
            Assert.Empty(decision.SuppressSourceIds);
            Assert.Contains("boundary", decision.PreserveSourceIds);
            Assert.Contains("h1", decision.PreserveSourceIds);
            Assert.Contains("h2", decision.PreserveSourceIds);
            Assert.Contains("h3", decision.PreserveSourceIds);
            Assert.Contains(decision.Residuals, residual =>
                residual.Kind == ReplacementResidualKind.SourceEquivalenceIncomplete
                && residual.Severity == ReplacementResidualSeverity.Critical);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }


    [Fact]
    public void Writer_preserves_empty_source_id_without_source_metadata()
    {
        var document = new VectorPdfDocument(
        [
            new VectorPdfPage(1, 72, 72, 0,
            [
                new VectorLine(
                    string.Empty,
                    new(0, 0),
                    new(10, 0),
                    new VectorStyle())
            ])
        ]);

        using var stream = new MemoryStream();
        var result = new AcadSharpDwgWriter().Write(
            stream,
            document,
            new DocumentLayoutPlanner().Create(document));

        stream.Position = 0;
        var drawing = DwgReader.Read(stream);
        var line = Assert.Single(drawing.Entities.OfType<Line>());
        Assert.False(SourceMetadataCodec.TryRead(line, out _));
        Assert.Contains(
            new PageSourceRef(1, string.Empty),
            result.SourceEmissionSummary.OutputFingerprintCountsBySource.Keys);
    }

    [Fact]
    public void Source_emission_summary_rejects_unknown_page_source()
    {
        var summary = new SourceEmissionSummary(
            new Dictionary<PageSourceRef, IReadOnlyDictionary<string, int>>
            {
                [new(1, "known")] = new Dictionary<string, int>
                {
                    ["fingerprint"] = 1
                }
            });

        Assert.Throws<InvalidOperationException>(() =>
            summary.GetExpectedSuppressionFingerprintMultiset(
            [
                new PageSourceRef(2, "known")
            ]));
    }
    private static ACadSharp.CadDocument WriteAndRead(VectorPdfDocument document)
    {
        using var stream = new MemoryStream();
        _ = new AcadSharpDwgWriter().Write(
            stream,
            document,
            new DocumentLayoutPlanner().Create(document));
        stream.Position = 0;
        return DwgReader.Read(stream);
    }

}
