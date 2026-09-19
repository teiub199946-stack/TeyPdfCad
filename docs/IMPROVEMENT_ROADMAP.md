# TeyPdfCad Improvement Roadmap

Status: accepted working roadmap, 2026-09-20.

## Priority order

1. Complete SPDS frame and title-block extraction and insertion.
2. Add a geometry normalization pass:
   - remove zero-length and duplicate entities;
   - snap near-coincident points with scale-aware tolerance;
   - merge safe collinear segments;
   - convert connected segments to lightweight polylines;
   - recover arcs and circles when mathematically verified.
3. Add a DWG optimization pass and measurable performance gate.
4. Detect repeated geometry and emit shared block definitions with inserts.
5. Improve text reconstruction and normalize subset font names.
6. Extend semantic recognizers for levels, breaks, sections and details.
7. Add automatic PDF to DWG render comparison and semantic acceptance.

## Application rules

- Preserve source geometry whenever confidence is insufficient.
- Route uncertain results to `TEY_REVIEW_*`.
- Do not apply an optimization that changes drawing meaning.
- Establish performance thresholds from real project fixtures rather than
  treating generic entity-count limits as universal.
- Every optimization must have before-and-after entity counts and regression
  tests.

## Current execution focus

The immediate blocker is deterministic AutoCAD 2022 Core Console startup with
the actual user configuration:

`C:\Users\Admin\AppData\Local\Autodesk\AutoCAD 2022\R24.1\rus\acad2022.cfg`

After the console health check passes, run the SPDS template exporter and then
the real PDF to DWG bridge acceptance.
