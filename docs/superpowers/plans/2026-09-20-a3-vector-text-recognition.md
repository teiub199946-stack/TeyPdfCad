# A3 Vector Text Recognition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Confirm the real A3 title block from vector-glyph linework, select the SPDS A3 template safely, and preserve every undecoded variable glyph as editable review geometry.

**Architecture:** Add a scale-aware glyph-evidence layer upstream of title-block detection. Use provenance and connected components for segmentation, compare glyph-run regions with MText regions from the provisional SPDS template, and permit template replacement only when independent grid and spatial text evidence agree. Character decoding remains an optional explicit-catalog stage and is never required to preserve unknown content.

**Tech Stack:** C# 12, .NET 8/.NET Framework 4.8, xUnit, ACadSharp, AutoCAD 2022 runtime fixtures.

**Spec:** `docs/superpowers/specs/2026-09-20-a3-vector-text-recognition-design.md`

## Global Constraints

- Never infer a character solely because a title-block cell is expected to contain that character.
- Never lower dimension-recognition thresholds to compensate for missing text.
- Never delete unknown glyph geometry.
- Do not mutate the source `PrimitiveScene`.
- Do not add raster OCR in this stage.
- Low-confidence or unknown content remains editable on `TEY_REVIEW_TEXT`.
- All size and distance thresholds use millimetres after applying `DrawingUnitsPerMm`.
- No user Layout or Viewport may be created.

## Review Focus

- One PDFIMPORT provenance handle containing two disconnected glyphs must be split rather than treated as one character; Task 1 includes this regression.
- A page imported at 1 unit/mm and the same page imported at 100 units/mm must produce equivalent glyph evidence; Task 1 includes this scale-invariance test.
- Repeated frame/grid lines must not be mistaken for glyphs merely because they are compact; Task 1 tests aspect and size rejection.
- A spatial match to expected template text must not fabricate decoded content; Task 2 tests that `DecodedValue` remains null.
- Unknown variable glyphs must survive template replacement on `TEY_REVIEW_TEXT`; Tasks 3 and 4 test replacement source IDs and final DWG layers.

---

### Task 1: Scale-aware glyph segmentation

**Files:**
- Create: `src/TeyPdfCad.Core/Recognition/VectorGlyphEvidence.cs`
- Create: `src/TeyPdfCad.Core/Recognition/VectorGlyphSegmenter.cs`
- Create: `tests/TeyPdfCad.Tests/Recognition/VectorGlyphSegmenterTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<LinePrimitive>`, `SheetPageBounds.DrawingUnitsPerMm`
- Produces: `VectorGlyphSegmentationResult VectorGlyphSegmenter.Segment(PrimitiveScene scene, VectorGlyphSegmentationOptions? options = null)`

- [ ] **Step 1: Write failing tests for provenance grouping and deterministic normalization**

```csharp
[Fact]
public void Groups_connected_strokes_by_provenance_and_normalizes_shape()
{
    var scene = SceneWithScale(100);
    scene.Lines.Add(Line(0, 0, 100, 0, "G1"));
    scene.Lines.Add(Line(100, 0, 100, 200, "G1"));

    var result = new VectorGlyphSegmenter().Segment(scene);

    var glyph = Assert.Single(result.Candidates);
    Assert.Equal(["G1"], glyph.ProvenanceIds);
    Assert.Equal(1, glyph.NormalizedStrokes[0].End.X, 6);
    Assert.Equal(1, glyph.NormalizedStrokes[1].End.Y, 6);
}
```

```csharp
[Fact]
public void Splits_disconnected_components_that_share_one_handle()
{
    var scene = SceneWithScale(100);
    scene.Lines.Add(Line(0, 0, 100, 100, "G1"));
    scene.Lines.Add(Line(1000, 0, 1100, 100, "G1"));

    var result = new VectorGlyphSegmenter().Segment(scene);

    Assert.Equal(2, result.Candidates.Count);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore --filter FullyQualifiedName~VectorGlyphSegmenterTests
```

Expected: compilation failure because `VectorGlyphSegmenter` and evidence contracts do not exist.

- [ ] **Step 3: Implement evidence contracts**

```csharp
public sealed record VectorGlyphEvidence(
    Point2 Min,
    Point2 Max,
    IReadOnlyList<VectorGlyphTemplateStroke> NormalizedStrokes,
    IReadOnlyList<string> ProvenanceIds,
    string? Layer,
    double SegmentationConfidence,
    string? DecodedValue = null,
    string? CatalogId = null,
    double? ClassificationConfidence = null);

public sealed record VectorGlyphSegmentationResult(
    IReadOnlyList<VectorGlyphEvidence> Candidates,
    IReadOnlyList<VectorGlyphRejection> Rejections);

public sealed record VectorGlyphRejection(
    string Reason,
    IReadOnlyList<string> ProvenanceIds,
    Point2 Min,
    Point2 Max,
    int StrokeCount);

public sealed record VectorGlyphSegmentationOptions
{
    public double EndpointJoinToleranceMm { get; init; } = 0.02;
    public double MinWidthMm { get; init; } = 0.2;
    public double MaxWidthMm { get; init; } = 15;
    public double MinHeightMm { get; init; } = 0.2;
    public double MaxHeightMm { get; init; } = 15;
    public int MaxStrokeCount { get; init; } = 80;
}
```

- [ ] **Step 4: Implement scale-aware segmentation**

Use provenance handle as the first partition. Within each partition, union
strokes whose endpoints are within:

```csharp
options.EndpointJoinToleranceMm * drawingUnitsPerMm
```

Reject candidates outside:

```csharp
MinWidthMm = 0.2;
MaxWidthMm = 15;
MinHeightMm = 0.2;
MaxHeightMm = 15;
MaxStrokeCount = 80;
```

Normalize each accepted component to a unit bounding box, sort strokes
deterministically, and calculate confidence from finite geometry, compact size
and connectedness.

- [ ] **Step 5: Add scale-equivalence and frame-line rejection tests**

```csharp
[Fact]
public void Equivalent_geometry_at_one_and_one_hundred_units_per_mm_has_same_signature()
{
    var one = SegmentGlyph(drawingUnitsPerMm: 1, scale: 1);
    var hundred = SegmentGlyph(drawingUnitsPerMm: 100, scale: 100);
    Assert.Equal(one.NormalizedStrokes, hundred.NormalizedStrokes);
}

[Fact]
public void Rejects_long_title_block_grid_line_as_glyph()
{
    var scene = SceneWithScale(100);
    scene.Lines.Add(Line(0, 0, 13000, 0, "FRAME"));
    Assert.Empty(new VectorGlyphSegmenter().Segment(scene).Candidates);
}
```

- [ ] **Step 6: Run Core tests**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/TeyPdfCad.Core/Recognition/VectorGlyphEvidence.cs src/TeyPdfCad.Core/Recognition/VectorGlyphSegmenter.cs tests/TeyPdfCad.Tests/Recognition/VectorGlyphSegmenterTests.cs
git commit -m "feat: segment scale-aware vector glyph evidence"
```

### Task 2: Template-guided static text evidence

**Files:**
- Create: `src/TeyPdfCad.Core/Templates/TemplateTextEvidenceMatcher.cs`
- Create: `src/TeyPdfCad.Core/Templates/TemplateTextEvidenceMatch.cs`
- Create: `tests/TeyPdfCad.Tests/Templates/TemplateTextEvidenceMatcherTests.cs`
- Modify: `src/TeyPdfCad.Core/Templates/TemplateLibrary.cs`

**Interfaces:**
- Consumes: `TemplateBlockDefinition`, `SheetMetadata`, `IReadOnlyList<VectorGlyphEvidence>`
- Produces: `TemplateTextEvidenceResult Match(TemplateBlockDefinition template, SheetMetadata sheet, IReadOnlyList<VectorGlyphEvidence> glyphs)`

- [ ] **Step 1: Write failing tests for spatial evidence without implicit decoding**

```csharp
[Fact]
public void Matches_two_static_text_regions_without_fabricating_values()
{
    var template = new TemplateBlockDefinition(
        "A3-landscape",
        [
            TemplateText("T1", 300, 20, "Лист"),
            TemplateText("T2", 350, 20, "Стадия")
        ],
        []);
    var glyphs = new[]
    {
        Glyph("G1", 299, 19, 315, 23),
        Glyph("G2", 349, 19, 365, 23)
    };

    var result = new TemplateTextEvidenceMatcher().Match(template, A3(), glyphs);

    Assert.True(result.IsConfirmed);
    Assert.Equal(2, result.Matches.Count);
    Assert.All(result.Matches, match => Assert.Null(match.DecodedValue));
}
```

```csharp
[Fact]
public void Rejects_single_matching_region()
{
    var template = new TemplateBlockDefinition(
        "A3-landscape",
        [
            TemplateText("T1", 300, 20, "Лист"),
            TemplateText("T2", 350, 20, "Стадия")
        ],
        []);
    var result = new TemplateTextEvidenceMatcher().Match(
        template,
        A3(),
        [Glyph("G1", 299, 19, 315, 23)]);

    Assert.False(result.IsConfirmed);
    Assert.Equal("insufficient-static-text-regions", result.Reason);
}
```

Add private literal helpers to the test file:

```csharp
private static TemplateGeometryEntity TemplateText(
    string handle, double x, double y, string value)
    => new(
        "AcDbMText", handle, [new TemplatePoint(x, y)], value, 3.5, "0",
        MinX: x, MinY: y, MaxX: x + 16, MaxY: y + 4);

private static VectorGlyphEvidence Glyph(
    string sourceId, double minX, double minY, double maxX, double maxY)
    => new(
        new Point2(minX, minY),
        new Point2(maxX, maxY),
        [],
        [sourceId],
        "PDF _Геометрия",
        1d);

private static SheetMetadata A3()
    => new(420, 297, StandardSheetFormat.A3, SheetOrientation.Landscape)
    {
        PageBounds = new SheetPageBounds(0, 0, 420, 297)
    };
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore --filter FullyQualifiedName~TemplateTextEvidenceMatcherTests
```

Expected: compilation failure for missing matcher contracts.

- [ ] **Step 3: Extend template text metadata**

Add template text bounds and role without changing schema version:

```csharp
public sealed record TemplateGeometryEntity(
    ...,
    double? MinX = null,
    double? MinY = null,
    double? MaxX = null,
    double? MaxY = null);
```

Populate these values from the existing manifest DTO. Treat `AcDbText` and
`AcDbMText` with non-empty `Text` as static-label regions for matching.

- [ ] **Step 4: Implement matcher**

Normalize template coordinates by `TemplateBlockDefinition.EffectiveOrigin`.
Transform them to the page using `SheetPageBounds.MinX/MinY` and
`DrawingUnitsPerMm`. Match glyph evidence to each expected text region using:

- centre-distance tolerance: `2.0 mm`;
- height ratio: `0.55-1.8`;
- horizontal occupancy ratio: at least `0.35`;
- at least two independent template text regions;
- no decoded value assignment.

Return matched and unmatched glyph provenance separately.

- [ ] **Step 5: Add ambiguity and scale tests**

```csharp
[Fact]
public void Rejects_when_two_template_regions_compete_for_same_glyph_run() { }

[Fact]
public void Matching_is_equivalent_at_one_hundred_drawing_units_per_mm() { }
```

The ambiguity test must assert reason `ambiguous-static-text-region`.

- [ ] **Step 6: Run Core tests and commit**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore
git add src/TeyPdfCad.Core/Templates tests/TeyPdfCad.Tests/Templates
git commit -m "feat: match vector text evidence to template labels"
```

### Task 3: Title-block confirmation and replacement contract

**Files:**
- Modify: `src/TeyPdfCad.Core/Sheets/TitleBlockRegion.cs`
- Modify: `src/TeyPdfCad.Core/Sheets/TitleBlockDetector.cs`
- Modify: `src/TeyPdfCad.Core/Templates/TemplateSheetSelector.cs`
- Modify: `src/TeyPdfCad.Core/Templates/TemplateLibrary.cs`
- Modify: `tests/TeyPdfCad.Tests/TitleBlockDetectorTests.cs`
- Modify: `tests/TeyPdfCad.Tests/Templates/TemplateSheetSelectorTests.cs`

**Interfaces:**
- Consumes: grid lines plus `TemplateTextEvidenceResult`
- Produces: `TitleBlockMetadata.StaticTextSourceIds`, `UnknownGlyphSourceIds`
- Produces: `TemplateSelection.SourceIdsToReplace`, `SourceIdsToReviewAsText`

- [ ] **Step 1: Write failing test for template-guided confirmation**

```csharp
[Fact]
public void Confirms_title_block_from_grid_and_two_static_text_matches()
{
    var result = TitleBlockDetector.DetectWithTemplateEvidence(
        scene,
        sheet,
        ConfirmedEvidence(["static-1", "static-2"], ["value-1"]));

    Assert.True(result!.IsCandidate);
    Assert.Equal(["static-1", "static-2"], result.StaticTextSourceIds);
    Assert.Equal(["value-1"], result.UnknownGlyphSourceIds);
}
```

- [ ] **Step 2: Run test and verify RED**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore --filter FullyQualifiedName~TitleBlockDetectorTests
```

- [ ] **Step 3: Extend contracts**

```csharp
public sealed record TitleBlockMetadata(...)
{
    public IReadOnlyList<LinePrimitive> Lines { get; init; } = [];
    public IReadOnlyList<string> StaticTextSourceIds { get; init; } = [];
    public IReadOnlyList<string> UnknownGlyphSourceIds { get; init; } = [];
    public double EvidenceConfidence { get; init; }
}

public sealed record TemplateSelection(...)
{
    public IReadOnlyList<string> SourceIdsToReviewAsText { get; init; } = [];
}
```

- [ ] **Step 4: Implement detection**

`DetectWithTemplateEvidence` requires:

- confirmed template evidence;
- at least two line primitives intersecting the title-block region;
- finite confidence of at least `0.85`.

Grid replacement IDs include only provenance from line primitives classified
as grid/frame plus matched static text IDs. A line is grid/frame evidence only
when its provenance ID does not occur in any segmented glyph candidate and its
length or region span exceeds the configured glyph-size limits. Unknown glyph
IDs are excluded from replacement.

- [ ] **Step 5: Update selector**

Return:

```csharp
new TemplateSelection(
    true,
    template.Name,
    "format-grid-and-vector-text-confirmed",
    replacementIds)
{
    SourceIdsToReviewAsText = titleBlock.UnknownGlyphSourceIds
};
```

- [ ] **Step 6: Add fail-closed tests**

Test:

- one static region rejects;
- grid only rejects in production detection;
- unknown glyph IDs never enter `SourceIdsToReplace`;
- standard format without matching template rejects.

- [ ] **Step 7: Run Core tests and commit**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore
git add src/TeyPdfCad.Core/Sheets src/TeyPdfCad.Core/Templates tests/TeyPdfCad.Tests
git commit -m "feat: confirm title blocks with vector text evidence"
```

### Task 4: CLI integration and review-text preservation

**Files:**
- Modify: `src/TeyPdfCad.Cli/ConversionPipeline.cs`
- Modify: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Modify: `tests/TeyPdfCad.Cli.Tests/ConversionPipelineTests.cs`
- Modify: `tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3 evidence, matching and selection contracts
- Produces: page report glyph/template diagnostics
- Produces: DWG layer `TEY_REVIEW_TEXT`

- [ ] **Step 1: Write failing writer test**

```csharp
[Fact]
public void Template_replacement_preserves_unknown_glyph_source_on_review_text_layer()
{
    var selection = new TemplateSelection(
        true,
        "A3-landscape",
        "test",
        ["frame"])
    {
        SourceIdsToReviewAsText = ["unknown-glyph"]
    };

    var page = new VectorPdfPage(
        1, 72, 72, 0,
        [
            new VectorLine("frame", new Point2(0, 0), new Point2(10, 0), new VectorStyle()),
            new VectorLine("unknown-glyph", new Point2(2, 2), new Point2(3, 3), new VectorStyle())
        ]);
    var document = new VectorPdfDocument([page]);
    var library = new TemplateLibrary([], [new TemplateBlockDefinition(
        "A3-landscape",
        [new TemplateGeometryEntity(
            "AcDbLine", "T1",
            [new TemplatePoint(0, 0), new TemplatePoint(10, 0)],
            null, null, "0")],
        [])]);
    var drawing = DwgReader.Read(new MemoryStream(new AcadSharpDwgWriter().Write(
        document,
        new DocumentLayoutPlanner().Create(document),
        templateLibrary: library,
        templateSelectionsByPage: new Dictionary<int, TemplateSelection> { [1] = selection })));

    Assert.DoesNotContain(
        drawing.Entities.OfType<Line>(),
        line => line.StartPoint == new XYZ(0, 0, 0) && line.EndPoint == new XYZ(10, 0, 0));
    Assert.Contains(
        drawing.Entities,
        entity => entity.Layer.Name == "TEY_REVIEW_TEXT");
}
```

- [ ] **Step 2: Run focused DWG test and verify RED**

```powershell
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --no-restore --filter FullyQualifiedName~Template_replacement_preserves_unknown
```

- [ ] **Step 3: Update writer**

Before writing source lines/text:

```csharp
foreach (var sourceId in templateSelection.SourceIdsToReviewAsText)
    reviewLayersBySourceId[sourceId] = "TEY_REVIEW_TEXT";
```

Do not add these IDs to `templateSourceIds` or `consumedSemanticIds`.

- [ ] **Step 4: Integrate segmentation and matching in CLI**

In `SelectTemplate`:

1. construct `PrimitiveScene` with sheet bounds and drawing scale;
   convert each `VectorPolyline` segment to a `LinePrimitive` for evidence
   analysis while keeping the original polyline `SourceId` in provenance so
   writer preservation decisions operate on real source entities;
2. run `VectorGlyphSegmenter`;
3. locate provisional template by format/orientation;
4. run `TemplateTextEvidenceMatcher`;
5. call `TitleBlockDetector.DetectWithTemplateEvidence`;
6. call `TemplateSheetSelector`.

Return a page analysis object containing both selection and glyph diagnostics,
instead of returning only `TemplateSelection`.

- [ ] **Step 5: Add report fields**

Add:

```csharp
int GlyphCandidateCount,
int DecodedGlyphCount,
int UndecodedGlyphCount,
int MatchedStaticTextRegionCount,
double? TitleBlockEvidenceConfidence,
int TemplateReplacementSourceCount,
int PreservedReviewTextSourceCount
```

- [ ] **Step 6: Add CLI integration tests**

Use a reduced fixture containing an A3 grid, two template-aligned glyph groups
and one unknown value glyph. Assert:

- `templateSelected=true`;
- template name `A3-landscape`;
- two matched static regions;
- one preserved review-text source;
- output DWG contains template insert and `TEY_REVIEW_TEXT`;
- no Layout or Viewport.

- [ ] **Step 7: Run CLI/DWG suites and commit**

```powershell
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj --no-restore
git add src/TeyPdfCad.Cli src/TeyPdfCad.Dwg tests/TeyPdfCad.Cli.Tests tests/TeyPdfCad.Dwg.Tests
git commit -m "feat: select A3 template from vector text evidence"
```

### Task 5: Explicit glyph catalog and decoded runs

**Files:**
- Create: `src/TeyPdfCad.Core/Recognition/VectorGlyphCatalog.cs`
- Create: `src/TeyPdfCad.Core/Recognition/VectorGlyphClassifier.cs`
- Modify: `src/TeyPdfCad.Core/Recognition/VectorTextRecognizer.cs`
- Modify: `src/TeyPdfCad.Core/Recognition/VectorTextRecognitionOptions.cs`
- Create: `tests/TeyPdfCad.Tests/Recognition/VectorGlyphClassifierTests.cs`
- Modify: `tests/TeyPdfCad.Tests/Recognition/VectorTextRecognizerTests.cs`

**Interfaces:**
- Consumes: `VectorGlyphEvidence`, explicit `VectorGlyphCatalog`
- Produces: classified evidence and fully decoded `TextPrimitive` runs

- [ ] **Step 1: Write failing ambiguity-margin tests**

```csharp
[Fact]
public void Accepts_only_when_best_match_has_required_margin()
{
    var result = Classify(candidate, Catalog(best: 0.97, second: 0.82));
    Assert.Equal("2", result.DecodedValue);
}

[Fact]
public void Rejects_ambiguous_best_and_second_matches()
{
    var result = Classify(candidate, Catalog(best: 0.94, second: 0.91));
    Assert.Null(result.DecodedValue);
    Assert.Equal("ambiguous-template-match", result.Reason);
}
```

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore --filter FullyQualifiedName~VectorGlyphClassifierTests
```

- [ ] **Step 3: Implement catalog and classifier**

```csharp
public sealed record VectorGlyphCatalog(
    string Id,
    string Version,
    IReadOnlyList<VectorGlyphTemplate> Templates);

public sealed record VectorGlyphClassificationOptions(
    double MinimumScore = 0.93,
    double MinimumMargin = 0.08,
    double MaximumAspectRatioError = 0.20);
```

The classifier compares normalized strokes, aspect ratio and stroke count.
No fallback character is permitted.

- [ ] **Step 4: Adapt `VectorTextRecognizer`**

Reuse `VectorGlyphSegmenter` instead of rebuilding connected components.
Classify evidence through the supplied catalog and assemble only fully decoded
runs. Keep current seven-segment tests passing through a built-in catalog.

- [ ] **Step 5: Preserve real fixture fail-closed behavior**

The A3 fixture with no supplied catalog must assert:

```csharp
Assert.Empty(result.Texts);
Assert.NotEmpty(result.Rejections);
```

The `203 / 212 / 168` fixture remains undecoded until a reviewed catalog is
supplied.

- [ ] **Step 6: Run Core tests and commit**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore
git add src/TeyPdfCad.Core/Recognition tests/TeyPdfCad.Tests/Recognition
git commit -m "feat: classify vector glyphs with explicit catalogs"
```

### Task 6: Real A3 runtime acceptance

**Files:**
- Create: `tools/run-a3-vector-text-acceptance.ps1`
- Modify: `docs/AUTOCAD_MVP_TEST.md`
- Modify: `docs/PROJECT_STATE.md`
- Create: `docs/A3_VECTOR_TEXT_ACCEPTANCE_2026-09-20.md`

**Interfaces:**
- Consumes: Release CLI, real A3 PDF, exported SPDS manifest
- Produces: DWG, JSON report, hashes and AutoCAD reopen audit

- [ ] **Step 1: Create acceptance script**

Parameters:

```powershell
param(
    [Parameter(Mandatory)] [string] $InputPdf,
    [Parameter(Mandatory)] [string] $TemplateManifest,
    [Parameter(Mandatory)] [string] $OutputDirectory
)
```

The script builds Release, runs CLI conversion, validates JSON fields, opens the
DWG with ACadSharp and fails unless:

```text
templateSelected=true
templateName=A3-landscape
viewportCount=0
preservedReviewTextSourceCount>0
```

- [ ] **Step 2: Run script on `Тест А3.pdf`**

Expected output directory contains:

- `result.dwg`;
- `conversion-report.json`;
- `acceptance-summary.json`;
- SHA-256 files.

- [ ] **Step 3: Run AutoCAD reopen audit**

Open the produced DWG through isolated AutoCAD 2022 Core Console and run
`TEYPDFAUDITDWG`. Capture `TEYPDFCAD_AUDIT` and require zero non-paper
viewports.

- [ ] **Step 4: Run complete regression suites**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.AutoCAD.Tests/TeyPdfCad.AutoCAD.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.AutoCAD.Bridge.Tests/TeyPdfCad.AutoCAD.Bridge.Tests.csproj --no-restore
dotnet test tests/TeyPdfCad.Web.Tests/TeyPdfCad.Web.Tests.csproj --no-restore
```

- [ ] **Step 5: Build release artifacts**

```powershell
dotnet build src/TeyPdfCad.AutoCAD/TeyPdfCad.AutoCAD.csproj -c Release --no-restore
dotnet build src/TeyPdfCad.Cli/TeyPdfCad.Cli.csproj -c Release --no-restore
dotnet build src/TeyPdfCad.AutoCAD.Bridge/TeyPdfCad.AutoCAD.Bridge.csproj -c Release --no-restore
```

- [ ] **Step 6: Document evidence and commit**

```powershell
git add tools/run-a3-vector-text-acceptance.ps1 docs/AUTOCAD_MVP_TEST.md docs/PROJECT_STATE.md docs/A3_VECTOR_TEXT_ACCEPTANCE_2026-09-20.md
git commit -m "test: verify A3 vector text template acceptance"
```

## Final verification

- [ ] `git diff --check` reports no errors.
- [ ] Working tree contains no unrelated changes.
- [ ] All seven test suites pass.
- [ ] Release AutoCAD, CLI and Bridge builds have zero errors and warnings.
- [ ] Real A3 conversion selects `A3-landscape`.
- [ ] Unknown variable glyphs survive on `TEY_REVIEW_TEXT`.
- [ ] Saved DWG reopens and contains no generated user viewport.
- [ ] No report claims decoded text that is not backed by an explicit catalog.
