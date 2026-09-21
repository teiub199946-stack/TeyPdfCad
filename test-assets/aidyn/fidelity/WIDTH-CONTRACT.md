# Text width contract — proposal + numeric evidence

Branch: `codex/text-fidelity-fix`, from `987006a`.
Scope: source word width / text splitting (headers + bottom-right table) on Aidyn.pdf.
Did NOT modify: writer, ConversionPipeline, geometry, titles, output/CURRENT.

## Question from coordinator answered

> Can we pass source text width without shifting glyphs or creating false word breaks?

**Yes, for horizontal and rotated text, using `TextEntity.WidthFactor` with a
calibration factor.** ACadSharp 3.7.1 `TextEntity` exposes:

- `WidthFactor : Double` — uniform horizontal scale of the text; glyphs keep their
  order and relative spacing (it never re-flows or re-orders characters).
- `HorizontalAlignment : {Left, Center, Right, Aligned, Middle, Fit}`
- `AlignmentPoint : XYZ`

`WidthFactor` is a *transform*, so it cannot shift individual glyphs or split words;
the only risk is a wrong overall width (uniform stretch/compress), not a false break.

**Caveat (must be stated, not hidden):** `WidthFactor` maps advance -> width for a
*given* target font's natural width. The writer currently uses `arial.ttf` (Width=1).
So exact reproduction requires `WidthFactor = sourceAdvance / (Height * k)`, where
`k` = Arial's natural advance-per-unit-height for the specific string. Without
calibrating `k` (per font, or per string), `WidthFactor` only *approximates* width,
and per-word scaling can slightly break *inter-word* spacing uniformity on a line.

## Concrete contract (reader side, implemented)

`VectorText` gained one additive, defaulted field:

    double AdvanceWidthPoints = 0d

It is the baseline *length* of the word:

    hypot(EndBaseLine - StartBaseLine)

This is rotation-aware: horizontal words report X-advance; rotated words report the
true baseline length (which was previously `0` and would collapse).

Reader change (`PdfPigVectorDocumentReader`): computes `AdvanceWidthPoints` from the
baseline delta, in PDF points, after the existing single-glyph fallback.

## Numeric before/after (real page 29)

| word | rotation | height(pts) | advance width(pts) |
|---|---|---|---|
| `(Опалубочный` | 0 | 11.3 | 67.5 |
| `№04/05-2026-КР` (header) | 0 | 18.2 | 132 |
| `13х100=1300` (table) | ±π/2 | 7.09 | 43.1  (was 0) |
| `27х200=5400` (table) | ±π/2 | 7.1 | 48.1  (was 0) |
| `Согласовано` (side label) | ±π/2 | 8.51 | 54.9  (was 0) |

Full per-word dump: `page29-text-metrics.tsv` (177 rows).

## Proposed writer mapping (coordinator to implement)

In `AcadSharpDwgWriter` text loop, after building `TextEntity`:

    if (sourceText.AdvanceWidthPoints > 0 && sourceText.HeightPoints > 0)
    {
        // k = Arial advance per unit height; calibrate once (measure or fixed table)
        text.WidthFactor = sourceText.AdvanceWidthPoints / (sourceText.HeightPoints * k);
    }

`k` is the only open parameter. Options for `k`:
- fixed constant per font (Arial ~0.5 for latin, differs for Cyrillic);
- measure per document by fitting common words.

I recommend agreeing on `k` before wiring, since a wrong `k` uniformly mis-scales all
text and is worse than the current default (WidthFactor=1).

## Tests (green)

Pdf 12/12 (new: rotation-aware advance width), Core 74/74, Dwg 23/23, Cli 10/10.

## Honest status

- Reader now emits faithful, rotation-aware advance width for every word.
- **No width-faithful DWG is produced yet**: the writer still ignores the new field
  (it only writes `Height`). Producing a DWG now would look identical to HEAD and I
  will not claim a visual improvement without the writer mapping.
- Next (after coordinator agrees on `k` and wires the writer): table cell/row model.
