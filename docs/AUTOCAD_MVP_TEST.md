# TeyPdfCad — AutoCAD MVP acceptance test

## Purpose

Prove the first end-to-end product invariant:

> A vector PDF dimension such as `5200` is reconstructed as a native AutoCAD `Dimension` whose real `Measurement` is approximately `5200`, not as exploded LINE/TEXT geometry and not as a fake text override.

This is a V0.1 engineering test. Source PDFIMPORT primitives are intentionally preserved after reconstruction for visual comparison.

## Supported test target

- AutoCAD 2026 initial .NET 8 generation.
- Vector PDF only.
- Linear / aligned / rotated dimensions.
- One detected drawing-scale group per selected region.

Raster/scanned PDFs, multi-scale automatic region partitioning, radius/diameter native reconstruction, hatch/table/block recovery and final source cleanup are intentionally outside this acceptance gate.

## Test package

GitHub Actions workflow: `AutoCAD Adapter CI`

Artifact name:

`TeyPdfCad-AutoCAD2026`

Expected files:

- `TeyPdfCad.AutoCAD.dll`
- `TeyPdfCad.Core.dll`
- `TeyPdfCad.AutoCAD.deps.json`

Keep the files together in the same folder.

## Test drawing

Use a simple vector PDF exported from CAD containing at least:

- one horizontal dimension, for example `5200`;
- preferably a short chain such as `1200 + 1800 + 2200`;
- ordinary geometry around the dimensions;
- TrueType text where possible for the first test.

For the first acceptance test prefer one scale only on the selected PDF fragment.

## Procedure

1. Start AutoCAD 2026 with a blank drawing.
2. Run `PDFIMPORT`.
3. Import the vector PDF as AutoCAD geometry/text.
4. Keep the imported objects at the scale produced by PDFIMPORT; do not manually correct scale before TeyPdfCad analysis.
5. Run `NETLOAD`.
6. Load `TeyPdfCad.AutoCAD.dll` from the artifact folder.
7. Run `TEYPDFANALYZE`.
8. Select only the objects that belong to the imported PDF fragment being tested.
9. Read the command-line report.

Expected analysis result:

- `dimensions > 0`;
- a plausible detected scale;
- confidence values shown for recognized dimensions;
- source object count shown through provenance;
- no drawing modification.

10. If exactly one scale group is detected, run `TEYPDFRECONSTRUCT`.
11. Select the same PDFIMPORT fragment.
12. The command must either:
    - commit a validated reconstruction, or
    - reject/roll back the operation with an explicit reason.

It must never silently commit a native dimension whose measured value disagrees with the semantic value outside the validation tolerance.

## Native dimension acceptance check

After successful reconstruction:

1. Select one of the newly created dimensions.
2. Open AutoCAD Properties.
3. Confirm the object is a native AutoCAD dimension (`Aligned Dimension` / rotated dimension family), not LINE/TEXT geometry.
4. Confirm `Measurement` is approximately the expected real value, e.g. `5200`.
5. Edit/move a definition point and confirm the displayed dimension reacts as a real associative dimension object would geometrically.

For an annotation such as `5200±10`, the dimension text may use AutoCAD's `<>` measurement placeholder internally so the true measurement remains native while the suffix is preserved.

## Scale verification

The command scales the selected imported PDF geometry into reconstructed real drawing units before creating dimensions.

Example:

- imported geometric length: `52`;
- PDF dimension text: `5200`;
- detected scale factor: `100`;
- reconstructed geometry length: approximately `5200`;
- native AutoCAD `Dimension.Measurement`: approximately `5200`.

A text override alone is not considered success.

## Multi-scale safety gate

If more than one scale group is detected in the selection, `TEYPDFRECONSTRUCT` intentionally refuses global scaling and leaves the drawing unchanged.

This prevents one global transform from corrupting a sheet containing, for example, a 1:100 plan and a 1:20 detail.

Spatial scale partitioning will be implemented as a later adapter stage.

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
- screenshot or minimal reproducible PDF/DWG when possible.

Do not lower recognition thresholds merely to make an individual drawing pass. Add the failure as a regression case first, then correct the algorithm.

## V0.1 pass condition

The first product proof is achieved when a representative vector PDF containing a dimension `5200` passes this chain:

`PDF -> PDFIMPORT -> TeyPdfCad semantic analysis -> geometry normalization -> native AutoCAD DIMENSION -> Measurement ≈ 5200`

and the transaction automatically rolls back when this invariant is violated.
