# TeyPdfCad — AutoCAD 2022 MVP acceptance test

## Purpose

Prove the first end-to-end product invariant:

> A vector PDF dimension such as `203`, `212`, `168` or `5200` is reconstructed as a native AutoCAD `Dimension` whose real `Measurement` matches the semantic value, not as exploded LINE/TEXT geometry and not as a fake text override.

This is a V0.1 engineering test. Source PDFIMPORT primitives are intentionally preserved after reconstruction for visual comparison.

## Supported test target

- AutoCAD 2022 / release family 24.1.
- .NET Framework 4.8 host.
- Vector PDF only.
- Linear / aligned / rotated dimensions.
- One detected drawing-scale group per selected region.

Raster/scanned PDFs, automatic vector-glyph/SHX text recognition, multi-scale automatic region partitioning, radius/diameter native reconstruction, hatch/table/block recovery and final source cleanup are intentionally outside this acceptance gate.

## Test package

GitHub Actions workflow: `AutoCAD 2022 Adapter CI`

Artifact name:

`TeyPdfCad-AutoCAD2022`

Expected files:

- `TeyPdfCad.AutoCAD.dll`
- `TeyPdfCad.Core.dll`
- `AUTOCAD_MVP_TEST.md`
- `BUILD_INFO.txt`

Keep the DLL files together in the same folder.

Current diagnostic commands:

- `TEYPDFPING` — confirms plugin registration;
- `TEYPDFANALYZE` — read-only semantic analysis;
- `TEYPDFDUMP` — read-only export of the exact `PrimitiveScene` supplied by the AutoCAD adapter to a deterministic JSON fixture under `%TEMP%\TeyPdfCad\`;
- `TEYPDFRECONSTRUCT` — validated native dimension reconstruction.
- `TEYPDFSHEETCONFIG` — set measured page bounds for the current AutoCAD session without changing the drawing.
- `TEYPDFSHEETAUDIT` — read-only report of the generated A3 layout, media name, title-block block contents, and lineweights.

## Test drawing

Use a simple vector PDF exported from CAD containing at least three dimensions, preferably including an angled one. The current real smoke drawing uses approximately:

- vertical dimension `203`;
- angled dimension `212`;
- lower dimension `168`.

For the first acceptance pass use TrueType dimension text so PDFIMPORT produces `DBText`/`MText` primitives. A PDF where the visible digits are imported only as line/polyline glyph geometry is a separate required regression case for the future `VectorTextRecognizer`; do not lower Core recognition thresholds to force that file through this gate.

Use one drawing scale on the selected fragment.

## Procedure

1. Start AutoCAD 2022 with a blank drawing.
2. Run `PDFIMPORT` and import the test vector PDF.
3. Keep the imported objects at the scale produced by PDFIMPORT; do not manually correct scale before TeyPdfCad analysis.
4. Run `NETLOAD` and load `TeyPdfCad.AutoCAD.dll` from the artifact folder.
5. Confirm `TEYPDFPING` prints that plugin commands are registered.
6. Run `TEYPDFANALYZE`.
7. Window-select the entire imported PDF fragment and press Enter.
8. Read the command-line report.

The report includes the number of selected AutoCAD entities as well as generated line/text primitives. For the TrueType proof the expected shape is similar to:

`selected=..., lines=..., texts=3, dimensions=3, ...`

Exact line/entity counts depend on PDFIMPORT segmentation and are not acceptance metrics.

If the report shows `texts=0` while line primitives exist, TeyPdfCad now emits an explicit diagnostic that the PDF text may be vector glyph geometry. That file is not a valid TrueType proof input; preserve it as a regression fixture for vector-text recognition.

Expected analysis result for the TrueType proof:

- `texts > 0`;
- `dimensions > 0`;
- a plausible detected drawing scale;
- confidence values shown for recognized dimensions;
- no drawing modification.

9. If exactly one scale group is detected, run `TEYPDFRECONSTRUCT`.
10. Select the same PDFIMPORT fragment.
11. The command must either:
    - commit a validated reconstruction, or
    - reject/roll back the operation with an explicit reason.

It must never silently commit a native dimension whose measured value disagrees with the semantic value outside the validation tolerance.

## Controlled sheet-layout hook

For a measured A3 page only, the temporary adapter can receive explicit page bounds before running `TEYPDFRECONSTRUCTALL`:

```text
TEYPDFCAD_SHEET_WIDTH_MM=420
TEYPDFCAD_SHEET_HEIGHT_MM=297
TEYPDFCAD_SHEET_MIN_X=0
TEYPDFCAD_SHEET_MIN_Y=0
```

The adapter creates the layout and title-block block only after native dimension validation succeeds. These variables are not a PDF page reader and must not be populated from guessed geometry.

## Vector-glyph failure capture

For a real PDF where `TEYPDFANALYZE` reports `texts=0` despite visible dimension labels:

1. Preserve the PDF unchanged as a regression input.
2. Run `TEYPDFDUMP`.
3. Select the entire same PDFIMPORT fragment and press Enter.
4. The command writes a versioned JSON fixture under `%TEMP%\TeyPdfCad\` and prints the full file path plus `selected`, `lines`, `texts`, and `INSUNITS` counts.
5. Preserve/upload that JSON fixture before changing any recognition thresholds or implementing glyph templates.

`TEYPDFDUMP` is intentionally read-only and records the adapter's exact `PrimitiveScene`. It does not recognize vector text and does not modify the drawing.

## Native dimension acceptance check

After successful reconstruction:

1. Select one of the newly created dimensions.
2. Open AutoCAD Properties.
3. Confirm the object is a native AutoCAD dimension (`Aligned Dimension` / rotated dimension family), not LINE/TEXT geometry.
4. Confirm `Measurement` is approximately the expected real value, for example `203`, `212`, `168` or `5200`.
5. Move a definition point and confirm the dimension reacts geometrically as a native AutoCAD dimension.

For an annotation such as `5200±10`, the dimension text may use AutoCAD's `<>` measurement placeholder internally so the true measurement remains native while the suffix is preserved.

## Scale verification

The reconstruction command scales the selected imported PDF geometry into reconstructed drawing units before creating native dimensions.

Example:

- imported geometric length: `52`;
- PDF dimension text: `5200`;
- detected scale factor: `100`;
- reconstructed geometry length: approximately `5200`;
- native AutoCAD `Dimension.Measurement`: approximately `5200`.

A text override alone is not considered success.

## Multi-scale safety gate

If more than one scale group is detected in the selection, `TEYPDFRECONSTRUCT` intentionally refuses global scaling and leaves the drawing unchanged.

This prevents one global transform from corrupting a sheet containing, for example, a 1:100 plan and a 1:20 detail. Spatial scale partitioning is a later adapter stage.

## Failure data to record

For every failed real PDF test record:

- PDF source/application if known;
- AutoCAD version;
- number of selected entities;
- `TEYPDFANALYZE` output;
- expected dimension value/type;
- actual recognized value/type;
- detected scale(s);
- confidence;
- whether the dimension line was continuous or split around text;
- whether text arrived as DBText/MText or vector glyph geometry;
- screenshot or minimal reproducible PDF/DWG when possible.

Do not lower recognition thresholds merely to make an individual drawing pass. Add the failure as a regression case first, then correct the responsible layer.

## V0.1 pass condition

The first product proof is achieved when a representative vector PDF passes this chain:

`PDF -> PDFIMPORT -> TeyPdfCad semantic analysis -> geometry normalization -> native AutoCAD DIMENSION -> Measurement ≈ semantic value`

and the transaction automatically rolls back when this invariant is violated.
