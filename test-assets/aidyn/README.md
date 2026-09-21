# Aidyn Golden / Acceptance Assets

This directory preserves the external evidence required to reproduce and continue the TeyPdfCad PDF -> DWG work.

## Canonical input
- `input/Aidyn.pdf` — 51-page canonical regression PDF.
- `input/SOURCE.txt` — original source path and fixed SHA-256.

## Reference library
- `reference/TemplateLibrary.json` — exported SPDS/CAD reference library used for style/template work.

## Current accepted/baseline outputs
- `current/` contains the latest page-29 and full-51-page DWG/report baselines plus the page preview/PDF.

## Diagnostics
- `diagnostics/` preserves page-29 candidate/source/geometry experiments for dimension, leader, fill, text-angle, scale-consensus and level-attribute issues.
- The huge AutoCAD runtime copies under `page29-integrated-text-dimensions/audit-*` and `userdata/` are intentionally excluded; only the useful resulting DWG is preserved.

## Fidelity evidence
- `fidelity/REPORT.md`, `WIDTH-CONTRACT.md`, and `page29-text-metrics.tsv` document proven text-height and width findings.
- `fidelity/draw-order/` preserves the final draw-order spike, DWGs, reports and AutoCAD render PNGs.
- `fidelity/text-fit/` preserves the text-fit comparison fixtures and read-back evidence.

## Title / visual regression
- `title-tests/pages/` preserves pages 26, 29, 41 and 51 as compact PDF/DWG/PNG/report fixtures.
- `title-tests/previews/` preserves representative page previews.

`MANIFEST.sha256` is generated from every preserved file for reproducibility.
