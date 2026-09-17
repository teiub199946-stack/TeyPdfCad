# TeyPdfCad

Precision vector PDF -> DWG semantic reconstruction engine.

## Primary goal
Turn vector engineering PDFs into editable DWG while reconstructing native CAD semantics instead of returning exploded geometry.

V0.1 success criterion: a PDF dimension such as `5200` is reconstructed as a native AutoCAD `DIMENSION` with the correct definition points and measurement.

## Scope for the first sprint
- Vector PDF only; raster/scanned PDFs are out of scope.
- Geometry primitives: line, polyline, arc, circle.
- Text primitives.
- Scale estimation.
- Native linear/aligned/rotated dimension reconstruction.
- Confidence scoring and mathematical validation.
- Automated synthetic/regression tests.

## Architecture
- `TeyPdfCad.Core` — CAD-neutral semantic engine.
- `TeyPdfCad.AutoCAD` — temporary AutoCAD adapter for PDFIMPORT input and native DWG output.
- `TeyPdfCad.Tests` — unit/regression tests for the core.
- `TeyPdfCad.TestGenerator` — synthetic dimension/golden corpus generator.

The Core must never depend on AutoCAD assemblies. The AutoCAD adapter is replaceable later by a direct PDF primitive reader such as PDFium.
