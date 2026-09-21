# Text fidelity fix — proven causes & results

Branch: `codex/text-fidelity-fix` (created from `30ee5a1`).
Scope: text height/position fidelity in the PDF -> DWG independent path.

## Proven root cause (primary, affects Aidyn)

`PdfPigVectorDocumentReader` set `HeightPoints = word.Letters.Max(l => l.PointSize)`.
PdfPig documents `Letter.PointSize` as "the size of the font in points" — the
**nominal font size declared by the page**, not the rendered glyph height.

Measured on `input/Aidyn.pdf` (real):

| metric | value |
|---|---|
| `GlyphRectangle.Height / PointSize` median (page 1) | 0.466 |
| median (page 29) | 0.683 |
| median (page 41) | 0.490 |

So text was emitted ~1.5-2x taller than the visible glyph. That directly causes
the reported header overlap and dense white spots (over-height TEXT boxes collide;
see coordinator's white-fill audit for the masking side, which I did not touch).

Fix: derive height from `Letter.GlyphRectangle.Height` (the visible glyph box,
ascender + descender), taking the max over the word's letters, with a fallback to
`PointSize` only when no glyph box is available.

## Before / after (real Aidyn.pdf)

| sample word | before (HeightPoints, =PointSize) | after (glyph box) |
|---|---|---|
| `630` (page 29) | 10.37 pt | 7.1 pt (-> 2.5 mm) |
| `№04/05-2026-КР` header | ~14.5 pt | 6.43 mm |
| full-doc median TEXT height | > nominal, inflated | **2.5 mm** |
| full-doc max TEXT height | > 10 mm | **6.44 mm** |

Read-back of the produced DWG (`output/text-fidelity-review/aidyn-text-height-fix.dwg`)
with ACadSharp: 9943 TEXT entities, median height 2.5 mm, max 6.44 mm.

## What I verified is NOT a bug (avoid double-rotation)

- Aidyn MediaBox is `(0,0)-(595.32,841.92)` — zero offset, rotation 0, so no frame
  mismatch on the user's actual input.
- PdfPig's `Letter.StartBaseLine` / `EndBaseLine` / `GlyphRectangle` are **already
  normalized** — they subtract `MediaBox.Left/Bottom` and apply page `/Rotate`.
  Probe: PDF text at (172,800) with `MediaBox [100 100 ...]` -> PdfPig reports
  baseline (72,700). So the reader must NOT re-apply page rotation/offset to text.

## Latent finding (out of my scope — for coordinator)

For a PDF with a **non-zero MediaBox offset**, PdfPig normalizes letter baselines
(and reports `Page.MediaBox.Bounds.Left/Bottom = 0`), but the geometry interpreter
`PdfGraphicsOperationInterpreter` consumes raw content-stream coordinates and
applies `-mediaBounds.{Left,Bottom}` where those are already 0. Net effect: text is
normalized, geometry is not -> geometry/text desync on offset-origin pages.

This does **not** affect Aidyn (zero origin). It lives in the geometry interpreter
(`PdfGraphicsOperationInterpreter` / how the reader passes media bounds), which the
coordinator owns. Flagging it here; I did not modify it.

## Changes

- `src/TeyPdfCad.Pdf/PdfPigVectorDocumentReader.cs`: glyph-box height + fallback.
- `tests/TeyPdfCad.Pdf.Tests/PdfPigVectorDocumentReaderTests.cs`:
  - updated height assertion (12 pt Helvetica -> ~8.66 pt glyph box);
  - new `Reader_derives_text_height_from_visible_glyph_box_not_nominal_font_size`;
  - new `Reader_keeps_text_and_geometry_in_the_same_origin_when_media_box_is_offset`
    (asserts text-only normalization; documents the geometry desync as a separate concern).

## Tests (all green)

- TeyPdfCad.Pdf.Tests: 11/11
- TeyPdfCad.Tests (Core): 74/74
- TeyPdfCad.Cli.Tests: 10/10
- TeyPdfCad.Dwg.Tests: 23/23

## Contract question for coordinator (width/spacing)

`VectorText` has no width/advance field. Each `Word` becomes one AutoCAD `TEXT`;
AutoCAD then renders width from the font metrics, which won't match the source
PDF's exact glyph advance/kerning (minor horizontal drift, not the overlap bug).
If vertical spacing is next, suggest adding an optional per-word advance/width to
the `VectorText` contract — but that touches the shared writer, so I did not change it.

## Limitations

- Standalone punctuation words (dash `—`, hyphen `-`, `=`) get tiny real glyph-box
  heights (~0.15-0.28 mm), making them nearly invisible. 90 such entities in the
  full doc. No heuristic added (per coordinator); documented.
- Height is now glyph-faithful but still per-word (no cross-word baseline/line
  grouping yet — that is the coordinator's table/line-grouping track).

## Artifacts (written only to output/text-fidelity-review)

- `aidyn-text-height-fix.dwg` + `aidyn-text-height-fix-report.json` (full 51-page run)
- this `REPORT.md`

## SHA

See the commit on branch `codex/text-fidelity-fix`.
