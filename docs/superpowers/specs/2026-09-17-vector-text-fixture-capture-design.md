# Vector-text fixture capture design

Date: 2026-09-17

## Goal

Capture the exact primitive geometry that AutoCAD 2022 `PDFIMPORT` supplies for a real PDF whose visible dimension labels arrive as vector glyph geometry (`texts=0`). The captured fixture will become the first real input for `VectorTextRecognizer` work.

This stage deliberately does **not** recognize glyphs yet. It removes guesswork by recording the actual lines/text/provenance that are passed to Semantic Core.

## Product context

Verified real failure case:

- 35 selected AutoCAD objects;
- 39 line primitives;
- 0 text primitives;
- 0 semantic dimensions;
- visible labels include `203`, `212`, `168`;
- AutoCAD `PDFSHXTEXT` failed to recover the labels.

The responsible future pipeline is:

`PDFIMPORT geometry -> real primitive fixture -> VectorTextRecognizer -> TextPrimitive -> existing SemanticReconstructionEngine`

No dimension-recognition thresholds are changed in this stage.

## Command

Add an AutoCAD diagnostic command:

`TEYPDFDUMP`

Flow:

1. User selects the same objects produced by `PDFIMPORT`.
2. `AutoCadPrimitiveReader` builds the exact `PrimitiveScene` used by the product.
3. A deterministic formatter serializes the scene into a versioned JSON fixture.
4. The command writes the fixture under `%TEMP%/TeyPdfCad/` and prints the full path plus primitive counts.
5. The user can upload that JSON fixture for regression/recognizer development.

The command is read-only with respect to the drawing.

## Fixture schema v1

Top-level fields:

- `schema`: `TeyPdfCad.PrimitiveScene.v1`
- `selectedCount`
- `insunits`
- `lines`
- `texts`

Each line stores:

- start `[x,y]`
- end `[x,y]`
- layer
- provenance/source IDs

Each text stores:

- value
- position `[x,y]`
- height
- rotation
- layer
- provenance/source IDs

Numeric output uses invariant culture and round-trip precision. Strings are JSON-escaped. Collections are sorted deterministically so selection/insertion ordering does not make otherwise identical fixtures differ.

## Safety / non-goals

- Do not modify `SemanticReconstructionEngine`.
- Do not alter production recognition thresholds.
- Do not infer or label glyphs in this task.
- Do not delete or transform source PDFIMPORT geometry.
- Do not add OCR.
- Do not depend on a particular SHX font.

## TDD acceptance

Formatter tests must prove:

1. Same geometry inserted in different list order produces byte-identical fixture text.
2. Line geometry, text geometry, layer and provenance are preserved.
3. JSON string escaping is valid for quotes/backslashes/control characters.
4. Empty text scenes (`texts=0`) serialize honestly.

Adapter CI must still build/test under AutoCAD 2022 / .NET Framework 4.8 and stage the package.

## Next stage after real capture

Only after the real `203/212/168` fixture is obtained do we design the first `VectorTextRecognizer` matcher. Recognition thresholds/templates will be derived from actual captured geometry plus independent synthetic variants, not from the expected labels alone.
