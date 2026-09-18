# Sheet Fidelity Contract

Status: implementation in progress. This document defines acceptance criteria and does not claim that the feature is complete.

## Goal

Convert a vector PDF drawing into an editable DWG while preserving the visual hierarchy and sheet structure required for engineering work. A PDF sheet must not become a single image or a flat collection of indistinguishable lines.

## Lineweight

- Preserve distinct source stroke widths when the source API exposes them.
- Store the source width in millimetres as optional `LinePrimitive.StrokeWidthMm`.
- Keep `null` when the source width is unavailable. Do not infer a precise width from colour alone.
- Preserve lineweight provenance through deterministic line merging only when both merged segments have the same known width.
- Write explicit AutoCAD `LineWeight` values where a native output entity supports them.
- Retain separate layers for source geometry classes when layer information is available.
- A fixture without `strokeWidthMm` remains valid schema v1 input.

## Sheet Detection

- Detect page width, height, units, and orientation from the source PDF page.
- Preserve optional page-coordinate bounds (`MinX`, `MinY`, width, height, units) separately from the detected standard format so imported drawings do not assume an origin of `(0,0)`.
- Recognize an A3 sheet as 420 x 297 mm or 297 x 420 mm within a documented tolerance.
- Keep the measured page bounds and the detected standard format separately.
- Fail closed to `Unknown` when page units or bounds are ambiguous.

## AutoCAD Layout

- The temporary AutoCAD adapter accepts explicit `TEYPDFCAD_SHEET_WIDTH_MM`, `TEYPDFCAD_SHEET_HEIGHT_MM`, and optional `TEYPDFCAD_SHEET_MIN_X`/`TEYPDFCAD_SHEET_MIN_Y` as controlled page-bounds input. It must remain unset when no measured page bounds are available.
- Create a Paper Space layout matching the detected A3 orientation.
- Preserve the drawing geometry as editable native entities.
- Preserve the sheet frame as editable line or polyline entities.
- Do not rasterize the sheet, frame, or title block.
- Keep Model Space geometry and Paper Space sheet structure explicitly separated.

## Editable Title Block

- Core now carries an optional `PrimitiveScene.TitleBlock` contract with a candidate region, source provenance, and editable text fields.
- Accepted candidates retain the selected source `LinePrimitive` list so a future adapter writer can recreate the frame/grid without re-querying erased entities.
- `TitleBlockDetector` only returns a candidate when the region contains text and at least two source line segments; fields remain `Unknown` until semantic recognition is proven.
- Detect a candidate title-block region from the page geometry without deleting source objects.
- Store title-block lines and texts with source provenance.
- Create a named AutoCAD block for the accepted title block.
- Write recognized fields as editable attributes when their semantic role is known.
- Keep unclassified title-block text as editable `DBText` or `MText`.
- Allow the block to be edited or exploded using normal AutoCAD tools.
- Never fabricate missing title-block values.

Candidate semantic attributes include:

- drawing name;
- drawing number;
- stage;
- sheet number;
- sheet count;
- author;
- checker;
- approval;
- date;
- scale.

## Acceptance Fixtures

At least two independent fixtures are required:

1. A TrueType dimension PDF proving native `Dimension.Measurement` values.
2. A real A3 vector PDF with a frame, editable title block, multiple lineweights, and representative Cyrillic text.

The A3 fixture passes only when all of the following are independently verified:

- page format and orientation match the PDF;
- the DWG contains an A3 layout;
- the frame is editable geometry;
- the title block is an editable block;
- title-block values are editable text or attributes;
- at least two distinct source lineweights remain distinct in DWG;
- native dimension measurements remain within the configured tolerance;
- the source PDFIMPORT or direct-reader geometry is retained when `preserveSourceGeometry=true`;
- the saved DWG reopens successfully in AutoCAD.

## Evidence Boundary

- Source inspection is not AutoCAD runtime evidence.
- Fixture JSON is not proof that a DWG contains native dimensions or an editable title block.
- A GUI screenshot proves only the visible object and properties shown in that screenshot.
- Core Console readiness remains a separate gate from GUI plugin acceptance.
- Production readiness requires a saved, reopenable DWG plus automated and manual acceptance evidence.
