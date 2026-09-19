# Vector PDF Multi-layout DWG Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an AutoCAD-independent vector-PDF conversion core that produces one editable DWG with Model Space and one paper-space Layout per PDF page.

**Architecture:** PdfPig reads page dimensions, positioned text and raw graphics operations into a page-oriented Core document model. A dedicated DWG adapter maps that model to ACadSharp DWG entities, layouts and viewports; the existing AutoCAD plug-in is retained solely as the final acceptance/audit harness, not as the conversion engine.

**Tech Stack:** .NET 8, C#; PdfPig (Apache-2.0) for vector-PDF content; ACadSharp (MIT) for cross-platform DWG writing; xUnit; existing AutoCAD 2022 plug-in for acceptance diagnostics.

**Spec:** `docs/superpowers/specs/2026-09-19-vector-pdf-multilayout-dwg-design.md`

## Global Constraints

- First release accepts vector PDFs only; reject pages with no usable vector content.
- A document produces exactly one DWG and one Layout per processed PDF page.
- Convert PDF points using exactly `25.4 / 72` millimetres per point.
- Preserve source geometry and styles; never invent a native dimension or leader below the configured confidence threshold.
- Do not depend on AutoCAD, `PDFIMPORT`, or a desktop UI during conversion.
- Preserve actual page dimensions; recognise A0–A4 only as validation metadata.
- Deliver one combined AutoCAD acceptance package after the large block, not incremental user-run tests.

## Review Focus

- A rotated A3 page must retain its actual landscape dimensions and must not be swapped or stretched.
- A 51-page PDF must create 51 non-overlapping layouts with deterministic names and page order.
- A PDF consisting only of a scanned image must fail as unsupported rather than return a misleading DWG.
- Mixed solid, dashed and dash-dot source strokes must retain distinct linetype and lineweight definitions.
- A partially unreadable document must not be reported as fully successful; the report must identify the failed page.

---

## Planned file structure

- Create `src/TeyPdfCad.Core/Documents/VectorPdfDocument.cs`: immutable document/page/diagnostic contracts.
- Create `src/TeyPdfCad.Core/Documents/VectorEntities.cs`: style-bearing line, polyline, arc, circle, text, hatch-candidate and semantic-candidate contracts.
- Create `src/TeyPdfCad.Core/Conversion/DocumentLayoutPlanner.cs`: page numbering, format validation and Model/Paper placement plan.
- Create `src/TeyPdfCad.Core/Conversion/ConversionReport.cs`: serialisable per-page outcome and aggregate result.
- Create `src/TeyPdfCad.Pdf/TeyPdfCad.Pdf.csproj`, `PdfPigVectorDocumentReader.cs`, `PdfGraphicsOperationInterpreter.cs`.
- Create `src/TeyPdfCad.Dwg/TeyPdfCad.Dwg.csproj`, `AcadSharpDwgWriter.cs`, `AcadSharpStyleCatalog.cs`.
- Create `src/TeyPdfCad.Cli/TeyPdfCad.Cli.csproj`, `Program.cs`, `ConversionPipeline.cs`.
- Create focused xUnit projects under `tests/TeyPdfCad.Pdf.Tests`, `tests/TeyPdfCad.Dwg.Tests`, and `tests/TeyPdfCad.Cli.Tests`.
- Modify `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs` and `AutoCadPrimitiveReader.cs` only to add post-write audit commands.
- Create `scripts/Run-VectorPdfAcceptance.ps1` and `scripts/RUN_VECTOR_PDF_ACCEPTANCE.scr` once automated tests pass.

### Task 1: Establish page-oriented conversion contracts

**Files:**
- Create: `src/TeyPdfCad.Core/Documents/VectorPdfDocument.cs`
- Create: `src/TeyPdfCad.Core/Documents/VectorEntities.cs`
- Create: `src/TeyPdfCad.Core/Conversion/ConversionReport.cs`
- Test: `tests/TeyPdfCad.Tests/Documents/VectorPdfDocumentTests.cs`

**Interfaces:** Produces `VectorPdfDocument`, `VectorPdfPage`, `VectorStyle`, `VectorEntity`, `ConversionReport`, and `PageConversionReport`.

- [ ] **Step 1: Write the failing page-unit test**

```csharp
[Fact]
public void Page_converts_pdf_points_to_millimetres_exactly()
{
    var page = new VectorPdfPage(1, 1190.551, 841.89, 0, []);
    Assert.Equal(420.0, page.WidthMillimetres, 6);
    Assert.Equal(297.0, page.HeightMillimetres, 6);
}
```

- [ ] **Step 2: Run it to verify failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter FullyQualifiedName~VectorPdfDocumentTests -c Release`

Expected: compilation failure because `VectorPdfPage` does not exist.

- [ ] **Step 3: Implement immutable contracts**

```csharp
public sealed record VectorPdfPage(int Number, double WidthPoints, double HeightPoints,
    int RotationDegrees, IReadOnlyList<VectorEntity> Entities)
{
    public const double MillimetresPerPoint = 25.4d / 72d;
    public double WidthMillimetres => WidthPoints * MillimetresPerPoint;
    public double HeightMillimetres => HeightPoints * MillimetresPerPoint;
}
```

Attach source page number, confidence and provenance to every entity; make reports serialisable without AutoCAD types.

- [ ] **Step 4: Run focused and full Core tests**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release`

Expected: all existing and new Core tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Core/Documents src/TeyPdfCad.Core/Conversion tests/TeyPdfCad.Tests/Documents
git commit -m "feat: add page-oriented vector document contracts"
```

### Task 2: Plan deterministic Layout, Model Space and viewport placement

**Files:**
- Create: `src/TeyPdfCad.Core/Conversion/DocumentLayoutPlanner.cs`
- Test: `tests/TeyPdfCad.Tests/Conversion/DocumentLayoutPlannerTests.cs`

**Interfaces:** Consumes `VectorPdfDocument`. Produces `DwgDocumentPlan Create(VectorPdfDocument document)` with `SheetPlan` entries named `Лист-001` onward.

- [ ] **Step 1: Write failing planning tests**

```csharp
[Fact]
public void Fifty_one_pages_produce_fifty_one_stable_layout_names()
{
    var plan = new DocumentLayoutPlanner().Create(TestDocument.WithPages(51));
    Assert.Equal(51, plan.Sheets.Count);
    Assert.Equal("Лист-001", plan.Sheets[0].LayoutName);
    Assert.Equal("Лист-051", plan.Sheets[50].LayoutName);
}
```

Add tests for A3 landscape, A4 portrait, non-standard dimensions, and a page whose model viewport confidence is below threshold.

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter FullyQualifiedName~DocumentLayoutPlannerTests -c Release`

Expected: compilation failure because planner types do not exist.

- [ ] **Step 3: Implement planning**

Create `DwgDocumentPlan` and `SheetPlan`. Reuse `StandardSheetDetector` for A0–A4 metadata but retain original millimetres. Allocate isolated Model Space regions and create a viewport plan only when scale confidence meets threshold; otherwise retain paper-space placement and warning.

- [ ] **Step 4: Run Core tests**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release`

Expected: PASS, including 51-layout and no-guessing tests.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Core/Conversion tests/TeyPdfCad.Tests/Conversion
git commit -m "feat: plan multi-layout model and paper spaces"
```

### Task 3: Read vector PDF pages, paths, text and styles

**Files:**
- Create: `src/TeyPdfCad.Pdf/TeyPdfCad.Pdf.csproj`
- Create: `src/TeyPdfCad.Pdf/PdfPigVectorDocumentReader.cs`
- Create: `src/TeyPdfCad.Pdf/PdfGraphicsOperationInterpreter.cs`
- Create: `tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj`
- Create: `tests/TeyPdfCad.Pdf.Tests/PdfPigVectorDocumentReaderTests.cs`
- Create: `tests/fixtures/vector-pdf/minimal-a3-vector.pdf`

**Interfaces:** Produces `Task<VectorPdfDocument> ReadAsync(Stream pdf, CancellationToken token)`. Reports `UnsupportedRasterOnly` when a page contains no usable vector paths/text.

- [ ] **Step 1: Write failing reader tests**

```csharp
[Fact]
public async Task Reader_preserves_page_size_text_and_stroked_line()
{
    await using var input = File.OpenRead(Fixture("minimal-a3-vector.pdf"));
    var document = await new PdfPigVectorDocumentReader().ReadAsync(input, default);
    Assert.Equal(1, document.Pages.Count);
    Assert.Contains(document.Pages[0].Entities, e => e is VectorLine);
    Assert.Contains(document.Pages[0].Entities, e => e is VectorText { Value: "A3" });
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release`

Expected: project/type missing.

- [ ] **Step 3: Implement the PdfPig adapter**

Add a pinned PdfPig PackageReference. Interpret `page.Operations` with graphics transforms, stroke colour, width, dash array and path paint operators. Use `page.Letters` for positioned text. Convert PDF coordinates to the shared page convention once, in this adapter.

- [ ] **Step 4: Add boundary tests and run them**

Add dashed stroke, cubic-curve/arc candidate and raster-only fixtures. Run: `dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release`

Expected: PASS; raster-only fixture reports page-level unsupported diagnostic.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Pdf tests/TeyPdfCad.Pdf.Tests tests/fixtures/vector-pdf
git commit -m "feat: read vector PDF pages paths text and styles"
```

### Task 4: Recognise axes, dimensions and leaders conservatively

**Files:**
- Create: `src/TeyPdfCad.Core/Recognition/AxisRecognizer.cs`
- Create: `src/TeyPdfCad.Core/Recognition/LeaderRecognizer.cs`
- Modify: `src/TeyPdfCad.Core/Recognition/SemanticReconstructionEngine.cs`
- Modify: `src/TeyPdfCad.Core/Semantics/SemanticReconstructionResult.cs`
- Test: `tests/TeyPdfCad.Tests/Recognition/AxisRecognizerTests.cs`
- Test: `tests/TeyPdfCad.Tests/Recognition/LeaderRecognizerTests.cs`

**Interfaces:** Consumes page-local vector entities and existing dimension candidates. Produces candidates with `double Confidence`, source IDs and `KeepAsGeometry`.

- [ ] **Step 1: Write failing confidence-boundary tests**

```csharp
[Fact]
public void Ambiguous_arrow_and_text_stay_geometry()
{
    var result = new LeaderRecognizer().Recognize(TestPage.AmbiguousLeader());
    Assert.Empty(result.NativeLeaders);
    Assert.Contains(result.Warnings, x => x.Code == "leader-low-confidence");
}
```

Add tests for dash-dot axis recognition and confirmed linear dimensions using existing recognisers.

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter "FullyQualifiedName~AxisRecognizerTests|FullyQualifiedName~LeaderRecognizerTests" -c Release`

Expected: compilation failure because recognisers do not exist.

- [ ] **Step 3: Implement recognition composition**

Preserve source IDs. Extend the existing semantic engine without regressing current dimension tests. Create a native candidate only when geometry, attached text and arrow/line evidence pass the threshold; otherwise warn and leave source primitives intact.

- [ ] **Step 4: Run all Core recognition tests**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Core/Recognition src/TeyPdfCad.Core/Semantics tests/TeyPdfCad.Tests/Recognition
git commit -m "feat: recognise axes and conservative leaders"
```

### Task 5: Write independent DWG with layouts, entities, styles and viewports

**Files:**
- Create: `src/TeyPdfCad.Dwg/TeyPdfCad.Dwg.csproj`
- Create: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Create: `src/TeyPdfCad.Dwg/AcadSharpStyleCatalog.cs`
- Create: `tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj`
- Create: `tests/TeyPdfCad.Dwg.Tests/AcadSharpDwgWriterTests.cs`

**Interfaces:** Consumes `VectorPdfDocument`, `DwgDocumentPlan`, semantic candidates. Produces `Task<DwgWriteResult> WriteAsync(Stream destination, DwgDocumentPlan plan, CancellationToken token)`.

- [ ] **Step 1: Write failing round-trip tests**

```csharp
[Fact]
public async Task Writer_creates_one_layout_per_page_with_native_entities()
{
    var bytes = await Write(TestDocument.WithPages(51));
    var drawing = DwgReader.Read(new MemoryStream(bytes));
    Assert.Equal(51, drawing.Layouts.Count(x => x.Name.StartsWith("Лист-")));
    Assert.Contains(drawing.Entities, x => x is ACadSharp.Entities.Line);
}
```

Also test A3 paper size, isolated Model Space region, paper-space frame, viewport creation, CENTER/dashed linetypes, lineweight, and only high-confidence Dimension/MLeader entities.

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release`

Expected: project/type missing.

- [ ] **Step 3: Implement ACadSharp writer**

Add a pinned ACadSharp package. Create DWG tables before entities. Map vector paths to Line, LwPolyline, Arc, Circle, Text/MText or Hatch. Create page-specific layouts and associated paper-space blocks. Write frames/annotations in paper space; write confirmed model regions into isolated named Model Space blocks; create viewports only from high-confidence SheetPlan entries.

- [ ] **Step 4: Run read-back tests**

Run: `dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release`

Expected: PASS, including 51-layout test and read-back of every entity category.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Dwg tests/TeyPdfCad.Dwg.Tests
git commit -m "feat: write independent multi-layout DWG documents"
```

### Task 6: Provide headless conversion and reports

**Files:**
- Create: `src/TeyPdfCad.Cli/TeyPdfCad.Cli.csproj`
- Create: `src/TeyPdfCad.Cli/Program.cs`
- Create: `src/TeyPdfCad.Cli/ConversionPipeline.cs`
- Create: `tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj`
- Create: `tests/TeyPdfCad.Cli.Tests/ConversionPipelineTests.cs`

**Interfaces:** Command: `TeyPdfCad.Cli convert --input <pdf> --output <dwg> --report <json>`. Exit: 0 complete; 2 unsupported/non-vector; 3 partial; 4 invalid arguments/I/O.

- [ ] **Step 1: Write failing pipeline tests**

```csharp
[Fact]
public async Task Pipeline_writes_dwg_and_json_report_for_all_pages()
{
    var result = await pipeline.ConvertAsync(Fixture("multipage-vector.pdf"), output, report, default);
    Assert.Equal(ConversionOutcome.Complete, result.Outcome);
    Assert.Equal(51, JsonDocument.Parse(File.ReadAllText(report)).RootElement.GetProperty("pagesProcessed").GetInt32());
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj -c Release`

Expected: project/type missing.

- [ ] **Step 3: Implement orchestration and atomic output**

Write DWG and JSON to temporary files in the destination directory. Move them to final paths only for complete outcomes; for partial outcomes retain a report marked `complete=false`. Include page counts, dimensions, format metadata, entity counts, semantic counts and page diagnostics.

- [ ] **Step 4: Run pipeline tests**

Run: `dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj -c Release`

Expected: PASS for complete, raster-only rejection and partial diagnostic fixtures.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Cli tests/TeyPdfCad.Cli.Tests
git commit -m "feat: add headless vector PDF conversion pipeline"
```

### Task 7: Build the one-shot AutoCAD acceptance package

**Files:**
- Modify: `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs`
- Modify: `src/TeyPdfCad.AutoCAD/AutoCadPrimitiveReader.cs`
- Create: `scripts/Run-VectorPdfAcceptance.ps1`
- Create: `scripts/RUN_VECTOR_PDF_ACCEPTANCE.scr`
- Modify: `docs/AUTOCAD_MVP_TEST.md`
- Test: `tests/TeyPdfCad.AutoCAD.Tests/AcceptanceAuditCommandTests.cs`

**Interfaces:** `TEYPDFAUDITDWG` emits layout count, page dimensions, Model Space entity count, viewport count, linetype/layer counts, dimensions and leaders as JSON.

- [ ] **Step 1: Write failing audit test**

```csharp
[Fact]
public void Audit_json_lists_layout_and_semantic_counts()
{
    var json = DwgAcceptanceAudit.Format(new DwgAcceptanceSnapshot(51, 51, 102, 8, 3));
    Assert.Contains("\"layoutCount\":51", json);
    Assert.Contains("\"viewportCount\":51", json);
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/TeyPdfCad.AutoCAD.Tests/TeyPdfCad.AutoCAD.Tests.csproj --filter FullyQualifiedName~AcceptanceAuditCommandTests -c Release`

Expected: compilation failure because acceptance audit types do not exist.

- [ ] **Step 3: Implement audit and scripts**

PowerShell invokes the headless CLI once on supplied PDF, stages DWG/report/log, then supplies one AutoCAD script that loads the existing plug-in and runs `TEYPDFAUDITDWG`. It never invokes `PDFIMPORT`. The AutoCAD command reads the DWG and only writes its documented audit log.

- [ ] **Step 4: Run all automated suites sequentially**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.AutoCAD.Tests/TeyPdfCad.AutoCAD.Tests.csproj -c Release
```

Expected: all pass without parallel DLL locking.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.AutoCAD scripts docs/AUTOCAD_MVP_TEST.md tests/TeyPdfCad.AutoCAD.Tests
git commit -m "feat: package one-shot DWG acceptance audit"
```

### Task 8: Produce user acceptance artefacts from the real fixtures

**Files:**
- Create: `artifacts/vector-pdf-acceptance/<timestamp>/README.md`
- Create: `artifacts/vector-pdf-acceptance/<timestamp>/acceptance-manifest.json`

**Interfaces:** Consumes the user-provided A3 and 51-page PDFs, released CLI and audit package. Produces one DWG/report per PDF and a manifest with checksums.

- [ ] **Step 1: Run headless conversion on real fixtures**

Run: `scripts/Run-VectorPdfAcceptance.ps1 -InputPdf '<absolute fixture path>' -OutputDirectory '<timestamped artifact path>'`

Expected: one DWG/report per input; the 51-page result reports 51 layouts.

- [ ] **Step 2: Validate manifest**

Verify layout count equals PDF page count; every layout has actual page dimensions; report is complete; DWG can be read back by ACadSharp.

- [ ] **Step 3: Give the user one AutoCAD instruction**

README tells the user to run the supplied script once, then return JSON audit, AutoCAD command log and saved DWG only if audit shows a discrepancy.

- [ ] **Step 4: Commit non-fixture docs and manifests only**

```bash
git add docs scripts artifacts/vector-pdf-acceptance
git commit -m "docs: publish vector PDF acceptance package"
```

Do not commit the user PDF or generated DWG unless the user explicitly asks to publish them.

## Plan self-review

- Spec coverage: Tasks 1–3 implement vector input and page units; Task 2 implements Layout/Model/Viewport decisions; Task 4 covers conservative semantics; Task 5 preserves native DWG geometry/styles; Task 6 implements reports/errors; Tasks 7–8 deliver the single acceptance package. Website and scans remain out of scope.
- Placeholder scan: no unresolved markers or undefined implementation references remain.
- Type consistency: `VectorPdfDocument` flows from reader to planner/writer; `DwgDocumentPlan` is produced by planner and consumed by writer; `ConversionReport` is emitted by pipeline.
- Review focus coverage: rotation/format is Task 2; 51 pages is Tasks 2 and 5; raster-only rejection is Tasks 3 and 6; linetype preservation is Tasks 3 and 5; partial report behavior is Task 6.
