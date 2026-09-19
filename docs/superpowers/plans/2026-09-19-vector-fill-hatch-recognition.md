# Vector Fill and Hatch Recognition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve vector PDF closed paths and solid fills, identify only well-evidenced line hatches, and write editable boundaries and native solid hatches to the single multi-layout DWG.

**Architecture:** Add explicit fill-bearing source entities to Core, so the PDF adapter records path closure and fill semantics without referring to ACadSharp. A focused hatch recognizer uses closed boundaries plus regular parallel interior lines to make conservative candidates. The DWG adapter emits every boundary and only a proven solid fill as an associative-independent `HATCH`; anything uncertain remains editable source geometry.

**Tech Stack:** .NET 8, C#, PdfPig 0.1.9, ACadSharp 3.7.1, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-19-vector-pdf-multilayout-dwg-design.md`

## Global Constraints

- First release accepts vector PDFs only; it must not invent semantic content from raster pixels.
- PDF coordinates are converted to millimetres once in the PDF adapter; page metadata and point-named style fields stay in points.
- Every PDF produces exactly one DWG and one Layout per page; page regions remain isolated in Model Space.
- Low-confidence hatch candidates preserve the original lines and closed boundaries, with a diagnostic rather than an invented `HATCH`.
- No AutoCAD, `PDFIMPORT`, desktop UI or web component participates in conversion.

## Review Focus

- A filled concave or nested contour must preserve its closed boundaries even when native hatch construction is declined.
- A solid fill with a non-black RGB color must round-trip as a colored `HATCH` and still retain a visible editable boundary.
- Parallel construction lines not enclosed by a contour must never become a hatch.
- A sequence of nearly parallel hatch lines interrupted by text must remain source geometry rather than a misleading hatch.
- A stroked-and-filled PDF path must produce both a boundary and fill, without duplicate boundary segments.

### Task 1: Model closed filled paths and recognition diagnostics

**Files:**
- Modify: `src/TeyPdfCad.Core/Documents/VectorEntities.cs`
- Create: `src/TeyPdfCad.Core/Recognition/HatchRecognizer.cs`
- Create: `src/TeyPdfCad.Core/Semantics/HatchCandidate.cs`
- Test: `tests/TeyPdfCad.Tests/Recognition/HatchRecognizerTests.cs`

**Interfaces:** Produces `VectorFilledPath(string SourceId, IReadOnlyList<Point2> Boundary, VectorFillRule FillRule, VectorStyle Style, double Confidence = 1d)` and `HatchRecognitionResult Recognize(IReadOnlyList<VectorEntity> entities)`, containing native candidates and `SemanticWarning` instances.

- [ ] **Step 1: Write failing hatch-boundary tests**

```csharp
[Fact]
public void Closed_solid_fill_is_a_native_solid_hatch_candidate()
{
    var filled = new VectorFilledPath("fill-1", [new(0, 0), new(20, 0), new(20, 10), new(0, 10)],
        VectorFillRule.NonZero, new VectorStyle(RgbColor: 0x336699));

    var result = new HatchRecognizer().Recognize([filled]);

    var hatch = Assert.Single(result.NativeHatches);
    Assert.True(hatch.IsSolid);
    Assert.Equal(0x336699, hatch.Style.RgbColor);
}
```

Add a parallel-line-without-boundary test that expects no native hatch and `hatch-low-confidence`.

- [ ] **Step 2: Run tests to verify failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter FullyQualifiedName~HatchRecognizerTests -c Release`

Expected: compilation failure because fill and hatch recognition types do not exist.

- [ ] **Step 3: Implement immutable contracts and conservative recognizer**

```csharp
public sealed record HatchCandidate(
    IReadOnlyList<Point2> Boundary,
    bool IsSolid,
    double? PatternAngleRadians,
    double? PatternSpacingMillimetres,
    VectorStyle Style,
    double Confidence,
    IReadOnlyList<string>? SourceEntityIds = null);
```

Emit a solid candidate only for a valid closed `VectorFilledPath` with three or more distinct points. Do not create a line-pattern candidate until a closed boundary, at least three parallel interior lines and a regular spacing estimate have all been verified.

- [ ] **Step 4: Run Core recognition tests**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release`

Expected: PASS, including existing dimension, axis and leader tests.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Core/Documents src/TeyPdfCad.Core/Recognition src/TeyPdfCad.Core/Semantics tests/TeyPdfCad.Tests/Recognition
git commit -m "feat: model filled paths and conservative hatches"
```

### Task 2: Extract closed paths and RGB fills from vector PDF operations

**Files:**
- Modify: `src/TeyPdfCad.Pdf/PdfGraphicsOperationInterpreter.cs`
- Modify: `tests/TeyPdfCad.Pdf.Tests/PdfPigVectorDocumentReaderTests.cs`

**Interfaces:** `PdfGraphicsOperationInterpreter.Interpret(IEnumerable<object> operations, int pageNumber)` returns a `VectorFilledPath` for `f`, `f*`, `B`, `B*`, `b`, and `b*` PDF path paints. Its `Boundary` is in millimetres, `FillRule` retains non-zero/even-odd, and `Style.RgbColor` comes from `SetNonStrokeColorDeviceRgb`.

- [ ] **Step 1: Write failing vector-PDF fixture test**

```csharp
const string contents = "0.2 0.4 0.6 rg 10 10 m 110 10 l 110 60 l 10 60 l h f";
var filled = Assert.Single(page.Entities.OfType<VectorFilledPath>());
Assert.Equal(4, filled.Boundary.Count);
Assert.Equal(0x336699, filled.Style.RgbColor);
```

Also assert a `B` path returns exactly one boundary and one fill, rather than duplicated line entities.

- [ ] **Step 2: Run PDF tests to verify failure**

Run: `dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release`

Expected: FAIL because fill operators are not interpreted.

- [ ] **Step 3: Implement path state and paint dispatch**

Track `subpathStart`, ordered vertices, closure and non-stroking RGB independently from stroke state. On a fill operator, close the active subpath when required and emit one `VectorFilledPath`; on fill-and-stroke, emit the filled path plus one `VectorPolyline` boundary with the stroke style. Clear only the painted active path.

- [ ] **Step 4: Run PDF tests**

Run: `dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release`

Expected: PASS, preserving existing straight-line, text, unit and dash assertions.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Pdf tests/TeyPdfCad.Pdf.Tests
git commit -m "feat: extract vector PDF fills and closed paths"
```

### Task 3: Write editable boundaries and native solid hatches to DWG

**Files:**
- Modify: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Test: `tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs`

**Interfaces:** `AcadSharpDwgWriter.Write(VectorPdfDocument source, DwgDocumentPlan plan)` emits an `LwPolyline` for every `VectorFilledPath` boundary and an `ACadSharp.Entities.Hatch { IsSolid = true }` for a valid solid fill, using the source color/layer and page-local Model Space offset.

- [ ] **Step 1: Write failing round-trip DWG test**

```csharp
var filled = new VectorFilledPath("fill", [new(0, 0), new(10, 0), new(10, 5), new(0, 5)],
    VectorFillRule.NonZero, new VectorStyle("ЗАЛИВКА", 0x336699));
var drawing = DwgReader.Read(new MemoryStream(writer.Write(new VectorPdfDocument([page]), plan)));
Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.LwPolyline>().Where(x => x.IsClosed));
var hatch = Assert.Single(drawing.Entities.OfType<ACadSharp.Entities.Hatch>());
Assert.True(hatch.IsSolid);
Assert.Equal(0x336699, hatch.Color.TrueColor);
```

Add a non-solid candidate test asserting no native Hatch is emitted while its source lines stay present.

- [ ] **Step 2: Run DWG tests to verify failure**

Run: `dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release`

Expected: FAIL because writer does not handle filled paths.

- [ ] **Step 3: Implement solid-hatch mapping**

Create the same closed `LwPolyline` boundary first, then build `Hatch` with `IsSolid = true`, `Pattern = HatchPattern.Solid`, source style, one polyline boundary path and a seed point computed from the boundary centroid. If ACadSharp rejects a degenerate path, write only the boundary and surface the diagnostic to the caller once reports exist.

- [ ] **Step 4: Run DWG tests**

Run: `dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release`

Expected: PASS, including source text, line styles, page offsets and viewports.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Dwg tests/TeyPdfCad.Dwg.Tests
git commit -m "feat: write editable solid fills to DWG"
```

### Task 4: Add regular-line hatch candidate analysis and end-to-end safety coverage

**Files:**
- Modify: `src/TeyPdfCad.Core/Recognition/HatchRecognizer.cs`
- Modify: `tests/TeyPdfCad.Tests/Recognition/HatchRecognizerTests.cs`
- Modify: `tests/TeyPdfCad.Pdf.Tests/PdfPigVectorDocumentReaderTests.cs`
- Modify: `tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs`

**Interfaces:** `HatchRecognizer.Recognize` emits `HatchCandidate` with `IsSolid=false`, `PatternAngleRadians` and `PatternSpacingMillimetres` only for a closed boundary enclosing three or more parallel lines with coefficient-of-variation of normal spacing at most 0.15 and no text inside the boundary.

- [ ] **Step 1: Write failing regular-pattern and rejection tests**

```csharp
[Fact]
public void Three_regular_parallel_lines_inside_closed_boundary_form_pattern_candidate()
{
    var result = new HatchRecognizer().Recognize(Entities.RectangleWithHorizontalLines(0, 0, 20, 20, [4, 8, 12]));
    var hatch = Assert.Single(result.NativeHatches.Where(x => !x.IsSolid));
    Assert.Equal(0d, hatch.PatternAngleRadians, 6);
    Assert.Equal(4d, hatch.PatternSpacingMillimetres, 6);
}
```

Add tests for irregular spacing, lines outside a boundary and text inside a boundary; each must emit no pattern hatch and retain source entities.

- [ ] **Step 2: Run Core tests to verify failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter FullyQualifiedName~HatchRecognizerTests -c Release`

Expected: FAIL because the recognizer initially supports solid fills only.

- [ ] **Step 3: Implement regularity, containment and text guards**

Estimate a unit direction from the first line, reject lines whose angular deviation exceeds 2 degrees, project their midpoints onto the normal, sort spacings and calculate mean/standard deviation. Find one containing closed boundary via point-in-polygon. Reject if a text insertion point lies within it. Preserve all lines regardless of the decision.

- [ ] **Step 4: Run full automated suites sequentially**

```powershell
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Pdf.Tests/TeyPdfCad.Pdf.Tests.csproj -c Release
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj -c Release
```

Expected: all suites pass without AutoCAD or a desktop PDF import.

- [ ] **Step 5: Commit**

```bash
git add src/TeyPdfCad.Core src/TeyPdfCad.Pdf src/TeyPdfCad.Dwg tests
git commit -m "feat: recognise regular vector hatch patterns"
```

## Plan self-review

- Spec coverage: Tasks 1–2 implement immutable filled-path data and vector-PDF fill extraction; Task 3 writes an editable boundary plus conservative solid hatch; Task 4 supplies regular line-hatch evidence and rejection behavior.
- Placeholder scan: no incomplete implementation references or undefined interfaces remain; pattern `HATCH` output for non-solid candidates is deliberately deferred until its DWG boundary API is proven by Task 3 and candidates remain geometry in this block.
- Type consistency: `VectorFilledPath` is defined in Task 1, emitted in Task 2, and consumed by Task 3; `HatchCandidate` is defined in Task 1 and extended by Task 4.
- Review focus coverage: concave/nested contours are preserved by Task 3 boundary tests; color and solid fill by Task 3; non-enclosed lines and text-interrupted patterns by Task 4; fill-and-stroke de-duplication by Task 2.
