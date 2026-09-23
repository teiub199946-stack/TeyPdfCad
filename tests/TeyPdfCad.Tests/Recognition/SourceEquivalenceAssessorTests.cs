using TeyPdfCad.Core.Documents;
using TeyPdfCad.Core.Geometry;
using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Core.Semantics;
using TeyPdfCad.Core.Semantics.Dimensions;
using Xunit;

namespace TeyPdfCad.Tests.Recognition;

public sealed class SourceEquivalenceAssessorTests
{
    [Fact]
    public void Axis_is_fail_closed_when_source_appearance_is_not_fully_represented()
    {
        var page = new VectorPdfPage(1, 72, 72, 0,
        [
            new VectorLine(
                "axis",
                new Point2(0, 0),
                new Point2(25, 0),
                new VectorStyle(
                    StrokeWidthPoints: 0.5,
                    DashPatternPoints: [10, 2, 2, 2]))
        ]);
        var axis = new AxisCandidate(
            new Point2(0, 0),
            new Point2(25, 0),
            0.95,
            ["axis"])
        {
            SourceClaims =
            [
                new(
                    "axis",
                    SourceUsageRole.AxisGeometry,
                    SourceClaimState.Valid,
                    false)
            ]
        };
        var semantics = EmptySemantics() with { Axes = [axis] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var id = SourceReplacementPlanner.GetCandidateKey(axis, 1);
        var assessment = result.GetRequired(id);

        Assert.False(assessment.IsComplete);
        Assert.Equal("AXIS", assessment.SemanticType);
        Assert.Contains("dash", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lineweight", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_with_captured_source_appearance_stays_fail_closed_until_native_mapping_is_proven()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("arrow", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native DIMENSION", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_appearance_is_rejected_when_snapshot_differs_from_raw_page()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with { HeightMm = 3.0 }
            }
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains(
            "differs from the raw VectorPdfPage evidence",
            assessment.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_current_destructive_semantic_type_is_incomplete_until_source_appearance_contract_exists()
    {
        var dimension = new DimensionCandidate(
            DimensionKind.Aligned,
            new(0, 0),
            new(10, 0),
            new(0, 5),
            10,
            10,
            1,
            0.95,
            "10",
            1,
            ["d-line", "d-ext", "d-text"])
        {
            SourceClaims =
            [
                new("d-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("d-ext", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var leader = new LeaderCandidate(
            new(0, 10),
            new(10, 10),
            "K-1",
            0.95,
            ["l-shaft", "l-arrow", "l-text"])
        {
            SourceClaims =
            [
                new("l-shaft", SourceUsageRole.LeaderShaft, SourceClaimState.Valid, false),
                new("l-arrow", SourceUsageRole.LeaderArrow, SourceClaimState.Valid, false),
                new("l-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var level = new LevelCandidate(
            new(0, 20),
            new(10, 20),
            "+3.600",
            0.95,
            ["level-marker", "level-text"])
        {
            SourceClaims =
            [
                new("level-marker", SourceUsageRole.LevelMarker, SourceClaimState.Valid, false),
                new("level-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var arc = new ArcDimensionCandidate(
            new(0, 30),
            10,
            0,
            Math.PI / 2,
            new(8, 38),
            "R10",
            0.95,
            ["arc-line", "arc-text"])
        {
            SourceClaims =
            [
                new("arc-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("arc-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var axis = new AxisCandidate(
            new(0, 40),
            new(20, 40),
            0.95,
            ["axis"])
        {
            SourceClaims =
            [
                new("axis", SourceUsageRole.AxisGeometry, SourceClaimState.Valid, false)
            ]
        };

        var page = new VectorPdfPage(
            1,
            100,
            100,
            0,
            [
                new VectorLine("d-line", new(0, 0), new(10, 0), new VectorStyle()),
                new VectorLine("d-ext", new(0, 0), new(0, 5), new VectorStyle()),
                new VectorText("d-text", "10", new(5, 5), 2.5, new VectorStyle()),
                new VectorLine("l-shaft", new(0, 10), new(10, 10), new VectorStyle()),
                new VectorLine("l-arrow", new(0, 10), new(1, 11), new VectorStyle()),
                new VectorText("l-text", "K-1", new(10, 10), 2.5, new VectorStyle()),
                new VectorPolyline(
                    "level-marker",
                    [new(0, 20), new(2, 21), new(2, 19)],
                    true,
                    new VectorStyle()),
                new VectorText("level-text", "+3.600", new(10, 20), 2.5, new VectorStyle()),
                new VectorLine("arc-line", new(0, 30), new(10, 30), new VectorStyle()),
                new VectorText("arc-text", "R10", new(8, 38), 2.5, new VectorStyle()),
                new VectorLine("axis", new(0, 40), new(20, 40), new VectorStyle())
            ]);
        var semantics = EmptySemantics() with
        {
            Dimensions = [dimension],
            Leaders = [leader],
            Levels = [level],
            ArcDimensions = [arc],
            Axes = [axis]
        };

        var assessments = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        Assert.Equal(5, assessments.Candidates.Count);
        Assert.All(
            assessments.Candidates.Values,
            assessment => Assert.False(assessment.IsComplete));
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_semantic_text_drift_from_raw_appearance()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceText = "20",
            DisplayedValue = 20
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("semantic text", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("raw source appearance", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_semantic_geometry_drift_from_raw_appearance()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            DefinitionPoint1 = new Point2(2, 0),
            DefinitionPoint2 = new Point2(12, 0),
            DimensionLinePoint = new Point2(5, 7)
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("semantic geometry", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source-appearance", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_is_fail_closed_when_font_identity_is_unavailable()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with { FontName = null }
            }
        };
        var rawText = Assert.Single(page.Entities.OfType<VectorText>());
        page = page with
        {
            Entities = page.Entities
                .Select(entity => ReferenceEquals(entity, rawText)
                    ? (VectorEntity)(rawText with { FontName = null })
                    : entity)
                .ToArray()
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("font identity is unavailable", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_is_fail_closed_when_advance_width_is_unavailable()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with { AdvanceWidthMm = null }
            }
        };
        var rawText = Assert.Single(page.Entities.OfType<VectorText>());
        page = page with
        {
            Entities = page.Entities
                .Select(entity => ReferenceEquals(entity, rawText)
                    ? (VectorEntity)(rawText with { AdvanceWidthPoints = 0d })
                    : entity)
                .ToArray()
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("advance width is unavailable", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_appearance_is_rejected_when_font_identity_differs_from_raw_page()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with { FontName = "Arial" }
            }
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains(
            "differs from the raw VectorPdfPage evidence",
            assessment.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_appearance_is_rejected_when_advance_width_differs_from_raw_page()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with
                {
                    AdvanceWidthMm = candidate.SourceAppearance.Text.AdvanceWidthMm!.Value + 1d
                }
            }
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains(
            "differs from the raw VectorPdfPage evidence",
            assessment.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_requires_endpoint_separated_arrow_sources()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var sharedArrow = new DimensionSourceLineAppearance(
            new(0, 5),
            new(10, 5.1),
            "DIM",
            0,
            0.25,
            [],
            ["d-arrow-shared"]);

        candidate = candidate with
        {
            SourceClaims =
            [
                new("d-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("d-ext-1", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-ext-2", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-arrow-shared", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("d-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ],
            SourceAppearance = candidate.SourceAppearance! with
            {
                ArrowLines = [sharedArrow]
            }
        };
        page = page with
        {
            Entities = page.Entities
                .Where(entity => entity.SourceId is not "d-arrow-1" and not "d-arrow-2")
                .Append((VectorEntity)new VectorLine(
                    "d-arrow-shared",
                    sharedArrow.Start,
                    sharedArrow.End,
                    new VectorStyle(
                        SourceLayer: "DIM",
                        RgbColor: 0,
                        StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint)))
                .ToArray()
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("arrow", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("endpoint", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_mismatched_extension_line_styles_as_unrepresentable()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var changedExtension = candidate.SourceAppearance!.ExtensionLines[1] with { RgbColor = 0xFF0000 };
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance with
            {
                ExtensionLines =
                [
                    candidate.SourceAppearance.ExtensionLines[0],
                    changedExtension
                ]
            }
        };
        page = page with
        {
            Entities = page.Entities
                .Select(entity => entity.SourceId == "d-ext-2"
                    ? (VectorEntity)new VectorLine(
                        "d-ext-2",
                        changedExtension.Start,
                        changedExtension.End,
                        new VectorStyle(
                            SourceLayer: "DIM",
                            RgbColor: 0xFF0000,
                            StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint))
                    : entity)
                .ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("extension", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same native DIMSTYLE", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_arrow_style_drift_from_dimension_line()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var changedArrow = candidate.SourceAppearance!.ArrowLines[0] with { StrokeWidthMm = 0.50 };
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance with
            {
                ArrowLines =
                [
                    changedArrow,
                    candidate.SourceAppearance.ArrowLines[1]
                ]
            }
        };
        page = page with
        {
            Entities = page.Entities
                .Select(entity => entity.SourceId == "d-arrow-1"
                    ? (VectorEntity)new VectorLine(
                        "d-arrow-1",
                        changedArrow.Start,
                        changedArrow.End,
                        new VectorStyle(
                            SourceLayer: "DIM",
                            RgbColor: 0,
                            StrokeWidthPoints: 0.50 / VectorPdfPage.MillimetresPerPoint))
                    : entity)
                .ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("arrow", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dimension-line style", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_nonstandard_pdf_lineweight_for_exact_native_mapping()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var appearance = candidate.SourceAppearance!;
        var dimensionLine = appearance.DimensionLine with { StrokeWidthMm = 0.27 };
        var arrows = appearance.ArrowLines
            .Select(line => line with { StrokeWidthMm = 0.27 })
            .ToArray();
        candidate = candidate with
        {
            SourceAppearance = appearance with
            {
                DimensionLine = dimensionLine,
                ArrowLines = arrows
            }
        };
        page = page with
        {
            Entities = page.Entities.Select(entity => entity.SourceId switch
            {
                "d-line" or "d-arrow-1" or "d-arrow-2" => entity is VectorLine line
                    ? (VectorEntity)(line with
                    {
                        Style = line.Style with
                        {
                            StrokeWidthPoints = 0.27 / VectorPdfPage.MillimetresPerPoint
                        }
                    })
                    : entity,
                _ => entity
            }).ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("lineweight", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0.27", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Dimension_source_equivalence_keeps_dashed_source_fail_closed_until_dash_phase_is_captured()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var appearance = candidate.SourceAppearance!;
        var dash = new[] { 4d, 2d };
        candidate = candidate with
        {
            SourceAppearance = appearance with
            {
                DimensionLine = appearance.DimensionLine with { DashPatternMm = dash },
                ArrowLines = appearance.ArrowLines
                    .Select(line => line with { DashPatternMm = dash })
                    .ToArray()
            }
        };
        page = page with
        {
            Entities = page.Entities.Select(entity => entity.SourceId switch
            {
                "d-line" or "d-arrow-1" or "d-arrow-2" => entity is VectorLine line
                    ? (VectorEntity)(line with
                    {
                        Style = line.Style with
                        {
                            DashPatternPoints = dash
                                .Select(value => value / VectorPdfPage.MillimetresPerPoint)
                                .ToArray()
                        }
                    })
                    : entity,
                _ => entity
            }).ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("dash phase", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_asymmetric_extension_beyond_dimension_line()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var appearance = candidate.SourceAppearance!;
        var changed = appearance.ExtensionLines[1] with { End = new Point2(10, 6.0) };
        candidate = candidate with
        {
            SourceAppearance = appearance with
            {
                ExtensionLines = [appearance.ExtensionLines[0], changed]
            }
        };
        page = page with
        {
            Entities = page.Entities
                .Select(entity => entity.SourceId == "d-ext-2"
                    ? (VectorEntity)new VectorLine(
                        "d-ext-2",
                        changed.Start,
                        changed.End,
                        new VectorStyle(
                            SourceLayer: "DIM",
                            RgbColor: 0,
                            StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint))
                    : entity)
                .ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("extension", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beyond", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_nonparallel_source_text_rotation_until_native_mapping_is_proven()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var appearance = candidate.SourceAppearance!;
        candidate = candidate with
        {
            SourceAppearance = appearance with
            {
                Text = appearance.Text with { RotationDegrees = 30d }
            }
        };
        var rawText = Assert.Single(page.Entities.OfType<VectorText>());
        page = page with
        {
            Entities = page.Entities
                .Select(entity => ReferenceEquals(entity, rawText)
                    ? (VectorEntity)(rawText with { RotationRadians = Math.PI / 6d })
                    : entity)
                .ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("text rotation", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parallel", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_rejects_dimension_line_endpoints_not_bound_to_extension_lines()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var appearance = candidate.SourceAppearance!;
        var dimensionLine = appearance.DimensionLine with
        {
            Start = new Point2(-1, 5),
            End = new Point2(11, 5)
        };
        var arrows = new[]
        {
            appearance.ArrowLines[0] with
            {
                Start = new Point2(-1, 5),
                End = new Point2(0, 6)
            },
            appearance.ArrowLines[1] with
            {
                Start = new Point2(11, 5),
                End = new Point2(10, 6)
            }
        };
        candidate = candidate with
        {
            SourceAppearance = appearance with
            {
                DimensionLine = dimensionLine,
                ArrowLines = arrows
            }
        };

        page = page with
        {
            Entities = page.Entities.Select(entity => entity.SourceId switch
            {
                "d-line" => (VectorEntity)new VectorLine(
                    "d-line",
                    dimensionLine.Start,
                    dimensionLine.End,
                    new VectorStyle(
                        SourceLayer: "DIM",
                        RgbColor: 0,
                        StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint)),
                "d-arrow-1" => (VectorEntity)new VectorLine(
                    "d-arrow-1",
                    arrows[0].Start,
                    arrows[0].End,
                    new VectorStyle(
                        SourceLayer: "DIM",
                        RgbColor: 0,
                        StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint)),
                "d-arrow-2" => (VectorEntity)new VectorLine(
                    "d-arrow-2",
                    arrows[1].Start,
                    arrows[1].End,
                    new VectorStyle(
                        SourceLayer: "DIM",
                        RgbColor: 0,
                        StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint)),
                _ => entity
            }).ToArray()
        };

        var assessment = SourceEquivalenceAssessor.Build(
                page,
                EmptySemantics() with { Dimensions = [candidate] },
                new HatchRecognitionResult([], []))
            .GetRequired(SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("dimension-line endpoint", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extension line", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_exposes_exact_native_font_and_width_proof_as_separate_blocker()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("arial.ttf", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Helvetica", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("advance width", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("independently", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_identifies_endpoint_single_wing_arrow_as_unmapped()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("single-wing", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arrow", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_identifies_crossing_oblique_ticks_but_keeps_tick_size_unproven()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        var firstTick = new DimensionSourceLineAppearance(
            new(-1, 4), new(1, 6), "DIM", 0, 0.25, [], ["d-arrow-1"]);
        var secondTick = new DimensionSourceLineAppearance(
            new(9, 6), new(11, 4), "DIM", 0, 0.25, [], ["d-arrow-2"]);
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                ArrowLines = [firstTick, secondTick]
            }
        };
        page = page with
        {
            Entities = page.Entities
                .Select(entity => entity.SourceId switch
                {
                    "d-arrow-1" => (VectorEntity)new VectorLine(
                        "d-arrow-1", firstTick.Start, firstTick.End,
                        new VectorStyle(
                            SourceLayer: "DIM", RgbColor: 0,
                            StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint)),
                    "d-arrow-2" => (VectorEntity)new VectorLine(
                        "d-arrow-2", secondTick.Start, secondTick.End,
                        new VectorStyle(
                            SourceLayer: "DIM", RgbColor: 0,
                            StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint)),
                    _ => entity
                })
                .ToArray()
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("oblique", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TickSize", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("calibration", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_appearance_is_rejected_when_visual_center_differs_from_raw_page()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with { VisualCenter = new Point2(8.5, 6.25) }
            }
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("differs from the raw VectorPdfPage evidence", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_equivalence_is_fail_closed_when_visual_center_is_unavailable()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceAppearance = candidate.SourceAppearance! with
            {
                Text = candidate.SourceAppearance.Text with { VisualCenter = null }
            }
        };
        var rawText = Assert.Single(page.Entities.OfType<VectorText>());
        page = page with
        {
            Entities = page.Entities
                .Select(entity => ReferenceEquals(entity, rawText)
                    ? (VectorEntity)(rawText with { VisualCenter = null })
                    : entity)
                .ToArray()
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("visual text center is unavailable", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_source_appearance_requires_exact_role_binding_not_only_source_id_set()
    {
        var (candidate, page) = CreateDimensionAppearanceFixture();
        candidate = candidate with
        {
            SourceClaims =
            [
                new("d-line", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("d-ext-1", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-ext-2", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-arrow-1", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("d-arrow-2", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("d-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ]
        };
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("role", assessment.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source-appearance", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dimension_polyline_segment_source_is_fail_closed_for_whole_source_suppression()
    {
        var (candidate, sourcePage) = CreateDimensionAppearanceFixture();
        var lineStyle = new VectorStyle(
            SourceLayer: "DIM",
            RgbColor: 0,
            StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint);
        var page = new VectorPdfPage(
            sourcePage.Number,
            sourcePage.WidthPoints,
            sourcePage.HeightPoints,
            sourcePage.RotationDegrees,
            sourcePage.Entities
                .Where(entity => !string.Equals(entity.SourceId, "d-line", StringComparison.Ordinal))
                .Prepend((VectorEntity)new VectorPolyline(
                    "d-line",
                    [new(0, 5), new(10, 5), new(10, 10)],
                    false,
                    lineStyle))
                .ToArray());
        var semantics = EmptySemantics() with { Dimensions = [candidate] };

        var result = SourceEquivalenceAssessor.Build(
            page,
            semantics,
            new HatchRecognitionResult([], []));

        var assessment = result.GetRequired(
            SourceReplacementPlanner.GetCandidateKey(candidate, 1));

        Assert.False(assessment.IsComplete);
        Assert.Contains("VectorPolyline", assessment.Reason, StringComparison.Ordinal);
        Assert.Contains("whole-source", assessment.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static (DimensionCandidate Candidate, VectorPdfPage Page) CreateDimensionAppearanceFixture()
    {
        var candidate = new DimensionCandidate(
            DimensionKind.Rotated,
            new(0, 0),
            new(10, 0),
            new(5, 5),
            10,
            10,
            1,
            0.95,
            "10",
            1,
            ["d-line", "d-ext-1", "d-ext-2", "d-arrow-1", "d-arrow-2", "d-text"])
        {
            RotationRadians = 0,
            SourceClaims =
            [
                new("d-line", SourceUsageRole.DimensionLine, SourceClaimState.Valid, false),
                new("d-ext-1", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-ext-2", SourceUsageRole.ExtensionLine, SourceClaimState.Valid, false),
                new("d-arrow-1", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("d-arrow-2", SourceUsageRole.ArrowGeometry, SourceClaimState.Valid, false),
                new("d-text", SourceUsageRole.Text, SourceClaimState.Valid, false)
            ],
            SourceAppearance = new DimensionSourceAppearance(
                new DimensionSourceTextAppearance(
                    "10",
                    new(5, 5),
                    2.5,
                    0,
                    "DIM",
                    0,
                    ["d-text"],
                    "Helvetica",
                    5.0,
                    new(7.5, 6.25)),
                new DimensionSourceLineAppearance(
                    new(0, 5),
                    new(10, 5),
                    "DIM",
                    0,
                    0.25,
                    [],
                    ["d-line"]),
                [
                    new DimensionSourceLineAppearance(
                        new(0, 0),
                        new(0, 6.25),
                        "DIM",
                        0,
                        0.25,
                        [],
                        ["d-ext-1"]),
                    new DimensionSourceLineAppearance(
                        new(10, 0),
                        new(10, 6.25),
                        "DIM",
                        0,
                        0.25,
                        [],
                        ["d-ext-2"])
                ],
                [
                    new DimensionSourceLineAppearance(
                        new(0, 5),
                        new(1, 6),
                        "DIM",
                        0,
                        0.25,
                        [],
                        ["d-arrow-1"]),
                    new DimensionSourceLineAppearance(
                        new(10, 5),
                        new(9, 6),
                        "DIM",
                        0,
                        0.25,
                        [],
                        ["d-arrow-2"])
                ])
        };

        var lineStyle = new VectorStyle(
            SourceLayer: "DIM",
            RgbColor: 0,
            StrokeWidthPoints: 0.25 / VectorPdfPage.MillimetresPerPoint);
        var page = new VectorPdfPage(
            1,
            100,
            100,
            0,
            [
                new VectorLine("d-line", new(0, 5), new(10, 5), lineStyle),
                new VectorLine("d-ext-1", new(0, 0), new(0, 6.25), lineStyle),
                new VectorLine("d-ext-2", new(10, 0), new(10, 6.25), lineStyle),
                new VectorLine("d-arrow-1", new(0, 5), new(1, 6), lineStyle),
                new VectorLine("d-arrow-2", new(10, 5), new(9, 6), lineStyle),
                new VectorText(
                    "d-text",
                    "10",
                    new(5, 5),
                    2.5 / VectorPdfPage.MillimetresPerPoint,
                    new VectorStyle(SourceLayer: "DIM", RgbColor: 0),
                    AdvanceWidthPoints: 5.0 / VectorPdfPage.MillimetresPerPoint,
                    FontName: "Helvetica",
                    VisualCenter: new(7.5, 6.25))
            ]);

        return (candidate, page);
    }

    private static SemanticReconstructionResult EmptySemantics()
        => new([], [], null, 0d);
}
