# Fidelity-first PDF → DWG Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a measured Fidelity path for page 29 that preserves source provenance and draw order, proves text fitting and clipping limits, and gates semantic DIMENSION/LEADER/AXIS/LEVEL output behind objective regression checks.

**Architecture:** Start with three isolated spikes before expanding the shared model: AutoCAD draw order, native text fitting, and text/graphics ordering. Then add the smallest provenance contract justified by those spikes, generate a Fidelity-only DWG, and compare it to the source PDF using structural and zonal raster metrics. Semantic reconstruction remains a separate opt-in phase.

**Tech Stack:** .NET 8, C#, PdfPig 0.1.9, ACadSharp 3.7.1, xUnit, AutoCAD 2022 Core Console for render/read-back acceptance, PowerShell for repeatable fixture execution.

**Spec:** `docs/superpowers/specs/2026-09-20-fidelity-first-pdf-dwg-design.md`

## Global Constraints

- Fidelity must not replace source geometry with semantic objects.
- `PaintOrder` for text is not introduced until Spike C proves a text/graphics mapping.
- Partial PDF clipping must resolve to `Flattened`, `Native`, `OutlineFallback`, `ReviewOnly`, or `Unsupported`; it must not be silently ignored.
- `Unknown` and `Unsupported` results must appear in the zonal artifact registry.
- White fills must not be removed by color alone.
- Every completed task produces a test or fixture, a report, and an independently reviewable commit.
- No production semantic DIMENSION/LEADER/AXIS/LEVEL changes occur before the Fidelity acceptance gate passes.
- Do not modify `output/CURRENT`; write new artifacts only below `output/text-fidelity-review` or a task-specific child directory.

## Review Focus

- **PDF text interleaving:** text-show operations may be decoded separately from graphics; Spike C must prove mapping or force an explicit fallback.
- **Save/reopen draw order:** insertion order may not survive ACadSharp/AutoCAD round-trip; Spike A must render after reopen.
- **Rotated text width:** a vertical baseline has zero X advance; Spike B must use baseline length and preserve angle.
- **Even-odd fills:** the reader carries `VectorFillRule`, but the writer currently emits HATCH boundaries without applying that rule; the fixture must expose holes.
- **Clip-affected text:** native TEXT cannot represent a partially clipped glyph; the acceptance report must show `Unknown`/`ReviewOnly` rather than claiming fidelity.

---

### Task 1: Draw-order spike

**Files:**
- Create: `tests/TeyPdfCad.Dwg.Tests/DrawOrderSpikeTests.cs`
- Create: `tools/draw-order-spike.ps1`
- Create: `tools/README-draw-order-spike.md`
- Create: `output/text-fidelity-review/spike-draw-order-report/` (generated, not committed)

**Interfaces:**
- Consumes: ACadSharp 3.7.1 `CadDocument`, `Line`, `LwPolyline`, `Hatch`, `TextEntity`, `DwgWriter`, `DwgReader`.
- Produces: a reproducible fixture DWG, insertion-order DWG, explicit-order DWG, post-reopen read-back summaries, AutoCAD PNG renders, and a pass/fail report.

- [ ] **Step 1: Write the failing fixture test**

Create a document containing, in source order:

```text
red LINE
white SOLID HATCH covering the line
black TEXT placed over the hatch
```

Create a second copy with the entities inserted in a different order. Save both
through `DwgWriter`, reopen through `DwgReader`, and assert that the entities
survive with their source IDs or deterministic test labels.

- [ ] **Step 2: Run the fixture without explicit draw order**

Run:

```powershell
pwsh -File tools/draw-order-spike.ps1 -Mode InsertionOrder
```

Expected: the script creates a DWG and a read-back summary; the test records
whether insertion order survives. A visual pass is not inferred from the
entity list.

- [ ] **Step 3: Add explicit ordering**

Use the ACadSharp `BlockRecord.CreateSortEntitiesTable()` /
`SortEntitiesTable` API verified in the ACadSharp 3.7.1 package. Move the text
to the top, the hatch below the text, and the line according to the fixture
order. Keep both variants in the generated report.

- [ ] **Step 4: Run AutoCAD save/reopen/render**

The script must invoke the repository's existing AutoCAD batch-run convention
with the generated DWG, save/reopen it, and render a PNG for both variants.
Record renderer version, DWG SHA, reopen result, and output PNG paths.

- [ ] **Step 5: Write the report and commit**

The report must state one of:

```text
insertion-order-stable
explicit-sort-required
draw-order-blocked
```

Commit only the test, script, and README:

```powershell
git add tests/TeyPdfCad.Dwg.Tests/DrawOrderSpikeTests.cs tools/draw-order-spike.ps1 tools/README-draw-order-spike.md
git commit -m "test: prove DWG draw order after save and reopen"
```

### Task 2: Native text-fitting spike

**Files:**
- Create: `tests/TeyPdfCad.Dwg.Tests/TextFitSpikeTests.cs`
- Create: `tools/text-fit-spike.ps1`
- Create: `tools/README-text-fit-spike.md`
- Create: `output/text-fidelity-review/spike-text-fit-report/` (generated, not committed)

**Interfaces:**
- Consumes: `VectorText.AdvanceWidthPoints`, `VectorText.HeightPoints`,
  `VectorText.RotationRadians`, ACadSharp `TextEntity.WidthFactor`,
  `TextHorizontalAlignment.Fit`, `AlignmentPoint`.
- Produces: three DWG candidates per fixture word: default, Fit, and calibrated
  WidthFactor; read-back metrics; AutoCAD renders; pass/fail decision.

- [ ] **Step 1: Create horizontal and 90-degree fixtures**

Use the same source word at a fixed height in horizontal and vertical
orientation. The expected start/end baseline points must be written into the
fixture metadata, not recovered from the DWG after creation.

- [ ] **Step 2: Write the three candidate entities**

Candidate 1:

```csharp
new TextEntity
{
    Value = value,
    InsertPoint = start,
    Height = heightMm,
    Rotation = angle
};
```

Candidate 2 additionally sets:

```csharp
HorizontalAlignment = TextHorizontalAlignment.Fit;
AlignmentPoint = end;
```

Candidate 3 sets `WidthFactor` from a documented calibration measurement. It
must not use `AdvanceWidthPoints` directly as `WidthFactor`.

- [ ] **Step 3: Save/reopen and inspect**

Read the three DWGs back through ACadSharp and assert that value, height,
rotation, insertion point, alignment mode, and alignment point survive.

- [ ] **Step 4: Render and measure**

Measure baseline endpoint error, height error, angle error, word mask IoU, and
character order. Select `Fit`, `WidthFactor`, or `font-mapping-blocked` only
from the report.

- [ ] **Step 5: Commit the isolated spike**

```powershell
git add tests/TeyPdfCad.Dwg.Tests/TextFitSpikeTests.cs tools/text-fit-spike.ps1 tools/README-text-fit-spike.md
git commit -m "test: compare native PDF text fitting strategies"
```

### Task 3: Text/graphics ordering spike

**Files:**
- Create: `tests/TeyPdfCad.Pdf.Tests/PdfTextOrderingSpikeTests.cs`
- Create: `src/TeyPdfCad.Pdf/TextOrderingProbe.cs`
- Create: `tools/text-ordering-spike.ps1`
- Create: `tools/README-text-ordering-spike.md`

**Interfaces:**
- Consumes: `PdfPage.Operations`, `PdfPage.Letters`, `NearestNeighbourWordExtractor`,
  `PdfGraphicsOperationInterpreter`.
- Produces: a deterministic mapping report from text-show operations to letters/
  words, including unmatched operations and order conflicts.

- [ ] **Step 1: Create synthetic PDFs**

Cover:

- text between two path-paint operations;
- multiple text-show operations forming one word;
- nested `q/Q`;
- text matrix rotation;
- active clipping;
- a word interrupted by a graphics operation.

- [ ] **Step 2: Probe operation order**

Record each text-show operation index, decoded letter sequence, word grouping,
graphics paint operation index, and transform/clip state. Do not modify the
production reader while probing.

- [ ] **Step 3: Apply the numerical gate**

The synthetic fixtures pass only when:

- 100% of text-show operations map unambiguously to decoded letters/words;
- there are zero order conflicts;
- the mapping is identical on repeated runs.

For page 29, record coverage and every unmatched operation. Partial real-page
coverage selects `TextOrderingStrategy` fallback rather than global text
`PaintOrder`.

- [ ] **Step 4: Commit the probe**

```powershell
git add tests/TeyPdfCad.Pdf.Tests/PdfTextOrderingSpikeTests.cs src/TeyPdfCad.Pdf/TextOrderingProbe.cs tools/text-ordering-spike.ps1 tools/README-text-ordering-spike.md
git commit -m "test: determine whether PDF text order can be reconstructed"
```

### Task 4: Minimal provenance and Fidelity mode

**Files:**
- Modify: `src/TeyPdfCad.Core/Documents/VectorEntities.cs`
- Modify: `src/TeyPdfCad.Core/Documents/VectorPdfDocument.cs`
- Modify: `src/TeyPdfCad.Pdf/PdfGraphicsOperationInterpreter.cs`
- Modify: `src/TeyPdfCad.Pdf/PdfPigVectorDocumentReader.cs`
- Modify: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Modify: `src/TeyPdfCad.Cli/ConversionPipeline.cs`
- Create: `src/TeyPdfCad.Core/Documents/PaintOrder.cs`
- Create: `src/TeyPdfCad.Core/Documents/ClipProvenance.cs`
- Create: `tests/TeyPdfCad.Tests/Documents/ProvenanceContractTests.cs`
- Create: `tests/TeyPdfCad.Cli.Tests/FidelityModeTests.cs`

**Interfaces:**
- Produces `PaintOrder`, `SourceOperationIndex`, `SourcePaintOperation`,
  `EmittedRole`, `ClipStateId`, and `ClipResolution` only when the corresponding
  spike has passed.
- Adds an explicit Fidelity setting that skips semantic recognition and never
  consumes source IDs.
- Keeps `VectorText` v0 fields limited to the currently implemented
  `InsertionPoint`, `HeightPoints`, `AdvanceWidthPoints`, and rotation until
  baseline/quad fields are intentionally added in v1.

- [ ] **Step 1: Write provenance contract tests**

Assert monotonic per-page paint order for emitted graphics entities, stable
source-operation provenance, separate emitted roles for fill/stroke, and
explicit `Unknown`/`ReviewOnly` clip resolution.

- [ ] **Step 2: Add Fidelity settings**

Add a setting with default:

```text
Mode = Fidelity
SemanticDimensions = false
SemanticLeaders = false
SemanticAxes = false
SemanticLevels = false
SemanticHatches = false
```

The writer must emit source entities without filtering them through semantic
consumption in this mode.

- [ ] **Step 3: Preserve graphics order**

Pass paint order from the interpreter to the writer. Sort only by the
spike-approved mechanism; never assume collection insertion order is enough.

- [ ] **Step 4: Add clipping status**

Flatten line/polygon clips where the interpreter can prove the result. Mark
text affected by unresolved clip as `Unknown`/`ReviewOnly`; do not silently
write native text as if it were exact.

- [ ] **Step 5: Run focused tests and commit**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj -c Release
git add src tests
git commit -m "feat: add measured Fidelity conversion mode"
```

### Task 5: Golden page-29 acceptance stand

**Files:**
- Create: `tools/page29-fidelity-metrics.ps1`
- Create: `tools/README-page29-fidelity-metrics.md`
- Create: `tests/TeyPdfCad.Cli.Tests/Page29FidelityBaselineTests.cs`
- Create: `tests/fixtures/page29-fidelity-zones.json`
- Create: `output/text-fidelity-review/page29-fidelity-baseline/` (generated, not committed)

**Interfaces:**
- Consumes: source page-29 PDF, Fidelity DWG, AutoCAD renderer, fixture zones,
  renderer metadata.
- Produces: source baseline v0/v1, PDF render, DWG render, two-run renderer
  noise baseline, registered diff maps, zonal metrics, structural diff, and
  artifact registry.

- [ ] **Step 1: Freeze source baseline v0**

Record only fields available before provenance v1: entity counts, coordinates,
styles, text insertion point/height/advance/rotation, fill boundaries/rules,
and diagnostics.

- [ ] **Step 2: Define manual zones**

Store verified rectangles for drawing, dimensions, text, title block, and table
in `tests/fixtures/page29-fidelity-zones.json`. The metrics tool must reject
missing or overlapping zone definitions.

- [ ] **Step 3: Establish renderer noise**

Render the same control DWG twice using identical AutoCAD/Core Console
settings. Measure the run-to-run diff and reject any acceptance threshold
smaller than that noise baseline.

- [ ] **Step 4: Add structural comparisons**

Compare source and read-back entities by source ID/provenance where available.
For lines compare endpoints, angle, and length; for text compare insertion,
height, advance, rotation; for fills compare area, boundaries, rule, and order.

- [ ] **Step 5: Add zonal raster comparisons**

Register PDF and DWG images by page frame, apply a 1–2 pixel morphology
tolerance only after recording registration error, and report per-zone diff.
Persist source/DWG renders, diff maps, tolerance mask, metadata, and JSON.

- [ ] **Step 6: Commit the acceptance stand**

```powershell
git add tools tests/fixtures tests/TeyPdfCad.Cli.Tests/Page29FidelityBaselineTests.cs
git commit -m "test: add page-29 Fidelity acceptance metrics"
```

### Task 6: Semantic return gates

**Files:**
- Modify: `src/TeyPdfCad.Cli/ConversionPipeline.cs`
- Modify: `src/TeyPdfCad.Core/Recognition/SemanticReconstructionEngine.cs`
- Modify: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Test: `tests/TeyPdfCad.Tests/Recognition/SemanticReconstructionEngineTests.cs`
- Test: `tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs`
- Create: `tests/TeyPdfCad.Cli.Tests/SemanticNoRegressionTests.cs`

**Interfaces:**
- Each semantic type has an independent setting and report section.
- A candidate is replaced only when confidence and provenance rules pass.
- Low-confidence candidates leave source geometry intact.

- [ ] **Step 1: Gate DIMENSION only**

Run the existing candidate-kind regression: `Rotated` creates
`DimensionLinear`; `Aligned` creates `DimensionAligned`. Verify rotation,
definition points, text point, and read-back type.

- [ ] **Step 2: Run page-29 structural/raster no-regression**

Compare Fidelity baseline versus Fidelity + DIMENSION. Reject if any zone
crosses its threshold or if source geometry is consumed without provenance.

- [ ] **Step 3: Repeat independently for LEADER, AXIS, LEVEL, semantic HATCH**

Each type gets its own flag, fixture, report, and commit. Do not combine
multiple semantic types in one unreviewed change.

- [ ] **Step 4: Commit each accepted semantic gate**

Use one commit per type:

```powershell
git commit -m "feat: enable measured semantic dimensions"
git commit -m "feat: enable measured semantic leaders"
git commit -m "feat: enable measured semantic axes"
git commit -m "feat: enable measured semantic levels"
git commit -m "feat: enable measured semantic hatches"
```

## Self-Review Checklist

- [ ] Every spec section maps to at least one task.
- [ ] No production `PaintOrder` for text is added before Spike C.
- [ ] No semantic replacement occurs in Fidelity mode.
- [ ] Clipping has explicit statuses and artifact accounting.
- [ ] Render noise is measured before raster acceptance thresholds.
- [ ] The known HATCH fill-rule gap has a dedicated fixture.
- [ ] Page 29 zones are fixture metadata, not production heuristics.
- [ ] Each task ends with a focused test/report and commit.
