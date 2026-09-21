# TeyPdfCad architecture

## Non-negotiable rules
1. `TeyPdfCad.Core` must not reference AutoCAD assemblies.
2. AutoCAD `PDFIMPORT` is a temporary primitive source for V0.1, not the final product architecture.
3. All semantic reconstruction operates on `PrimitiveScene`.
4. Native DWG objects are created only in an adapter layer.
5. Any uncertain reconstruction must preserve source primitives instead of guessing.
6. Every reconstructed dimension must pass geometric/value validation.
7. Every algorithm change must be covered by regression tests.

## V0.1 pipeline
PDF -> AutoCAD PDFIMPORT -> PrimitiveScene -> ScaleEstimator -> Dimension recognizers -> Confidence -> Validator -> AutoCAD native DIMENSION -> DWG.

## First semantic targets
- Rotated/linear dimension
- Aligned dimension
- Dimension chains
- Numeric text matching
- Scale estimation

## Deferred intentionally
Raster/OCR, blocks, tables, MLeader, semantic hatches, web UI, billing, other file formats, direct PDFium reader.
