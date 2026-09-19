# Model Space Template Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce Model Space-only DWG files that replace confirmed standard PDF sheets and annotations with editable template-based AutoCAD objects.

**Architecture:** The AutoCAD plug-in exports a read-only JSON description of the user template drawing. The platform-neutral Core library selects standard sheet templates and represents template inserts; the DWG writer materializes them in Model Space while preserving uncertain source geometry on review layers.

**Tech Stack:** C# 12, .NET 8/.NET Framework 4.8, Autodesk AutoCAD .NET API, ACadSharp, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-19-modelspace-template-library-design.md`

## Global Constraints

- Output DWG must have no newly-created user Layout or Viewport; all output is Model Space.
- Standard templates are used only at high confidence; otherwise preserve editable source geometry on `TEY_REVIEW_*`.
- Semantic service linework uses `LineWeight009` (0.09 mm).
- Template source DWG is read-only during export.
- Standard formats are A0–A4 in both orientations; non-standard sheets are never replaced.
- No AutoCAD runtime is required on the website conversion path after template export.

## Review Focus

- A standard-sized PDF with a slightly clipped frame must not be classified as a standard sheet without a confident title-block match.
- A 51-page DWG must contain zero non-model layouts and zero viewports after read-back.
- A custom proxy/SPDS entity must be reported by the extractor rather than omitted silently.
- A low-confidence leader, axis, break, level, section, or detail marker must retain source geometry on a review layer.
- Text inside a dense specification table must remain independently editable and must not be merged into one page-wide string.

---

### Task 1: Read-only AutoCAD template manifest exporter

**Files:**
- Create: `src/TeyPdfCad.AutoCAD/TemplateManifestExporter.cs`
- Create: `src/TeyPdfCad.AutoCAD/TemplateManifestModels.cs`
- Modify: `src/TeyPdfCad.AutoCAD/PluginEntryPoint.cs`
- Modify: `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs`
- Create: `tests/TeyPdfCad.AutoCAD.Tests/TemplateManifestExporterTests.cs`

**Interfaces:**
- Produces `TemplateManifestExporter.Export(Database database, Transaction transaction): TemplateLibraryManifest`.
- Produces `TemplateLibraryManifest.ToJson(): string` and command `TEYPDFEXPORTTEMPLATES`.
- Consumed by Task 2 as a stable JSON source.

- [ ] **Step 1: Write failing model tests**

```csharp
[Fact]
public void Manifest_serializes_block_attributes_and_unknown_classes()
{
    var manifest = new TemplateLibraryManifest("1", [], [], ["AeccDbProxyEntity"]);
    var json = manifest.ToJson();
    Assert.Contains("AeccDbProxyEntity", json);
}
```

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `dotnet test tests/TeyPdfCad.AutoCAD.Tests/TeyPdfCad.AutoCAD.Tests.csproj --filter TemplateManifestExporterTests`

- [ ] **Step 3: Implement manifest records and exporter**

```csharp
public sealed record TemplateLibraryManifest(
    string SchemaVersion,
    IReadOnlyList<TemplateBlockManifest> Blocks,
    IReadOnlyList<TemplateStyleManifest> Styles,
    IReadOnlyList<string> UnsupportedEntityClasses);

public TemplateLibraryManifest Export(Database database, Transaction transaction)
{
    // Enumerate block table records, entities, attribute definitions,
    // layers, text/dimension styles, dynamic block source and RxClass names.
}
```

- [ ] **Step 4: Add non-mutating command and registration test**

```csharp
[CommandMethod("TEYPDFEXPORTTEMPLATES", CommandFlags.Modal)]
public void ExportTemplates() => TemplateManifestExporter.ExportActiveDocument();
```

The command writes UTF-8 JSON to `%TEMP%\\TeyPdfCad`, prints the full path and
never opens an object for write or commits mutations.

- [ ] **Step 5: Run tests, build plug-in and commit**

Run: `dotnet test tests/TeyPdfCad.AutoCAD.Tests/TeyPdfCad.AutoCAD.Tests.csproj`

Commit: `feat: export AutoCAD template manifests`

### Task 2: Platform-neutral template catalog and sheet selection

**Files:**
- Create: `src/TeyPdfCad.Core/Templates/TemplateLibrary.cs`
- Create: `src/TeyPdfCad.Core/Templates/TemplateLibraryManifestReader.cs`
- Create: `src/TeyPdfCad.Core/Templates/TemplateSheetSelector.cs`
- Modify: `src/TeyPdfCad.Core/Sheets/StandardSheetDetector.cs`
- Create: `tests/TeyPdfCad.Tests/Templates/TemplateSheetSelectorTests.cs`

**Interfaces:**
- Consumes `TemplateLibraryManifestReader.Read(string json): TemplateLibrary`.
- Produces `TemplateSheetSelector.Select(SheetMetadata, TitleBlockRegion?): TemplateSelection`.
- `TemplateSelection` exposes `IsConfirmed`, `TemplateName`, `Reason`, and `Attributes`.

- [ ] **Step 1: Write failing standard/non-standard selection tests**

```csharp
[Theory]
[InlineData(420, 297, "A3-landscape")]
[InlineData(297, 420, "A3-portrait")]
public void Selects_matching_standard_template(double width, double height, string expected) { }

[Fact]
public void Rejects_non_standard_sheet_without_replacing_frame() { }
```

- [ ] **Step 2: Run Core tests and confirm failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter TemplateSheetSelectorTests`

- [ ] **Step 3: Implement immutable template catalog**

```csharp
public sealed record TemplateSelection(
    bool IsConfirmed, string? TemplateName, string Reason,
    IReadOnlyDictionary<string, string> Attributes);
```

Require both format tolerance and a confirmed title-block region before setting
`IsConfirmed=true`.

- [ ] **Step 4: Add title-block attribute mapping tests**

Test a mapped value, an absent value, and overlapping/unassigned source text;
the last case must remain review text rather than populate an attribute.

- [ ] **Step 5: Run Core tests and commit**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj`

Commit: `feat: select standard drawing templates safely`

### Task 3: Model Space-only DWG writing and standard frame insertion

**Files:**
- Modify: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Modify: `src/TeyPdfCad.Dwg/AcadSharpStyleCatalog.cs`
- Modify: `src/TeyPdfCad.Cli/ConversionPipeline.cs`
- Modify: `tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs`
- Modify: `tests/TeyPdfCad.Cli.Tests/ConversionPipelineTests.cs`

**Interfaces:**
- Consumes `TemplateSelection` and `TemplateLibrary` through `DwgWriteOptions`.
- Produces `DwgWriteReport` with `LayoutCount`, `ViewportCount`, `TemplateInsertCount`, and review entity counts.

- [ ] **Step 1: Write DWG read-back tests**

```csharp
[Fact]
public void Standard_A3_writes_frame_insert_in_modelspace_and_no_layouts_or_viewports() { }

[Fact]
public void Non_standard_page_preserves_source_frame_without_template_insert() { }
```

- [ ] **Step 2: Run focused DWG tests and confirm failure**

Run: `dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "Modelspace|Non_standard"`

- [ ] **Step 3: Add explicit `DwgOutputMode.ModelSpaceOnly`**

```csharp
public enum DwgOutputMode { ModelSpaceOnly, LayoutPerPage }
```

Make `ModelSpaceOnly` the direct-vector conversion default. Place pages by the
layout planner's non-overlapping model coordinates and do not call any layout or
viewport writer in this mode.

- [ ] **Step 4: Define and insert neutral frame blocks**

Create a named block definition from the catalog, insert it at page origin in
1:1 millimetres, create `AttributeEntity` values from `TemplateSelection`, and
omit only the confirmed source frame/title-block primitives.

- [ ] **Step 5: Run CLI and DWG tests, then commit**

Run: `dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj; dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj`

Commit: `feat: write template-backed modelspace drawings`

### Task 4: Native annotations, template blocks, and review fallback

**Files:**
- Modify: `src/TeyPdfCad.Core/Semantics/AnnotationCandidates.cs`
- Modify: `src/TeyPdfCad.Core/Recognition/SemanticReconstructionEngine.cs`
- Modify: `src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs`
- Modify: `tests/TeyPdfCad.Tests/Recognition/SemanticReconstructionEngineTests.cs`
- Modify: `tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs`

**Interfaces:**
- Consumes recognized annotation candidates with score and provenance.
- Produces `MLEADER`, `DIMENSION`, `HATCH`, or named blocks `TEY_AXIS`,
  `TEY_LEVEL`, `TEY_BREAK`, `TEY_SECTION`, and `TEY_DETAIL`.

- [ ] **Step 1: Write tests for semantic objects and fallback**

```csharp
[Fact]
public void High_confidence_axis_is_written_as_editable_block_with_attribute() { }

[Fact]
public void Low_confidence_section_marker_is_preserved_on_review_layer() { }

[Fact]
public void Semantic_service_linework_uses_lineweight_009() { }
```

- [ ] **Step 2: Run focused semantic and DWG tests and confirm failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter SemanticReconstructionEngineTests; dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "Axis|Review|Lineweight"`

- [ ] **Step 3: Extend candidate evidence and thresholds**

Add explicit kind, label, sheet reference, confidence and source primitives for
axis, level, break, section and detail. Enforce a high-confidence threshold for
replacement and route each rejection to `TEY_REVIEW_<kind>`.

- [ ] **Step 4: Materialize native and block entities**

Use editable attributes for labels/levels, `LineWeight.LineWeight009` for
service geometry, existing native dimension/leader writers for `DIMENSION` and
`MLEADER`, and existing closed boundaries for `HATCH`.

- [ ] **Step 5: Run complete Core/DWG test suites and commit**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj; dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj`

Commit: `feat: emit editable template annotations`

### Task 5: Text spacing, diagnostics, and acceptance package

**Files:**
- Modify: `src/TeyPdfCad.Pdf/PdfPigVectorDocumentReader.cs`
- Modify: `src/TeyPdfCad.Core/Recognition/VectorTextRecognizer.cs`
- Modify: `src/TeyPdfCad.Cli/ConversionPipeline.cs`
- Modify: `src/TeyPdfCad.AutoCAD/DwgAcceptanceAudit.cs`
- Modify: `scripts/Run-VectorPdfAcceptance.ps1`
- Create: `tests/TeyPdfCad.Tests/Recognition/VectorTextLayoutTests.cs`
- Modify: `tests/TeyPdfCad.AutoCAD.Tests/DwgAcceptanceAuditTests.cs`

**Interfaces:**
- Produces line-aware `VectorText` runs and `DwgAcceptanceSnapshot` with zero-layout assertions.

- [ ] **Step 1: Write failing dense-table text tests**

```csharp
[Fact]
public void Separates_adjacent_table_cells_into_independent_editable_text_runs() { }

[Fact]
public void Keeps_multiline_text_baselines_and_height_in_millimetres() { }
```

- [ ] **Step 2: Run focused Core tests and confirm failure**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter VectorTextLayoutTests`

- [ ] **Step 3: Implement baseline and gap segmentation**

Group glyphs by rotation, baseline and measured glyph height; split runs at a
width-relative gap; preserve each cell/run's origin, height, spacing, alignment
and transform. Emit `DBText` for a short run and `MText` only for a verified
multiline region.

- [ ] **Step 4: Extend acceptance audit and script**

Require `layouts=0`, `viewports=0`, inspect Model Space insert/attribute,
`DIMENSION`, `MLEADER`, `HATCH`, and review-layer totals. Include command
instructions that run `TEYPDFEXPORTTEMPLATES` once and attach the produced JSON
to the test package.

- [ ] **Step 5: Run all affected suites, build and commit**

Run: `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj; dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj; dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj; dotnet test tests/TeyPdfCad.AutoCAD.Tests/TeyPdfCad.AutoCAD.Tests.csproj`

Commit: `feat: verify modelspace template acceptance`

## Final verification

- [ ] Run `git diff --check` and inspect only changes belonging to this feature.
- [ ] Run the complete solution test suite with a local AutoCAD API path when available.
- [ ] Build the AutoCAD plug-in in Release configuration.
- [ ] Run the A3 and 51-page acceptance script; retain the DWG, JSON reports,
  manifest and script in the artifact folder.
- [ ] Run a final read-back that asserts no user layout or viewport was created.
