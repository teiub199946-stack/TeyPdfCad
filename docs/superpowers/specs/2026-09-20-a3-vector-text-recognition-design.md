# A3 Vector Text Recognition Design

## Status

Proposed architecture for review.

## Goal

Confirm and reconstruct the title block of the real A3 vector PDF even when
AutoCAD PDFIMPORT returns every visible character as line geometry and returns
zero `DBText` or `MText` entities.

The first deliverable is safe A3 template selection and replacement. Complete
OCR of arbitrary Cyrillic engineering text is not required for this stage.

## Verified input evidence

The runtime fixture
`tests/fixtures/real/autocad2022_a3_vector_titleblock.json` was captured from
the real `Тест А3.pdf` with AutoCAD 2022 PDFIMPORT scale `1`.

Verified properties:

- physical page: approximately 420 x 297 mm;
- imported page extents: approximately 41999.67 x 29700.45 drawing units;
- drawing scale: approximately 100 drawing units/mm;
- selected AutoCAD objects: 199;
- generated line primitives: 2,005;
- generated text primitives: 0;
- provenance object groups: 173;
- compact glyph-like groups: 82;
- repeated normalized shapes: 14.

The SPDS template library independently provides all ten A0-A4 orientation
blocks. Each standard sheet block contains 53 polylines and 15 editable MText
entities.

## Product constraints

- Never infer a character solely because a title-block cell is expected to
  contain that character.
- Never lower dimension-recognition thresholds to compensate for missing text.
- Never delete unknown glyph geometry.
- Do not mutate the source `PrimitiveScene`.
- Do not add raster OCR in this stage.
- Use the sheet format, page transform, provenance and SPDS template as
  evidence, not as permission to fabricate values.
- Low-confidence or unknown content remains editable on `TEY_REVIEW_TEXT`.

## Considered approaches

### Full glyph OCR first

Build a complete Cyrillic and numeric stroke-font recognizer before template
replacement.

This would eventually produce the richest result but requires a large labeled
corpus. It would delay safe frame replacement and would encourage guessing from
one A3 fixture.

### Raster OCR

Render the title block and send it through OCR.

This loses vector provenance, introduces raster dependencies and does not solve
safe deletion of source geometry. It remains a future fallback for scanned
pages.

### Template-guided vector evidence with gradual decoding

Segment source line geometry into glyph candidates, use their location and
shape as title-block evidence, spatially match static labels against the
selected SPDS template, and decode only glyphs present in an explicitly
verified font catalog.

This is the selected approach.

## Architecture

The pipeline becomes:

```text
PDFIMPORT lines
  -> VectorGlyphSegmenter
  -> VectorGlyphEvidence
  -> provisional sheet-template lookup
  -> TemplateTextEvidenceMatcher
  -> TitleBlockDetector
  -> TemplateSheetSelector
  -> optional VectorGlyphClassifier
  -> TextRunAssembler
  -> Model Space writer
```

### VectorGlyphSegmenter

The segmenter groups line primitives into candidate glyphs without assigning
characters.

Primary grouping uses existing PDFIMPORT provenance handles. A provenance
group is split only when disconnected components are separated by more than a
scale-aware endpoint tolerance.

Each candidate records:

- bounds;
- stroke count;
- normalized strokes;
- layer;
- provenance IDs;
- drawing-units-per-mm context;
- segmentation confidence.

Candidate size limits are expressed in millimetres after applying
`DrawingUnitsPerMm`, not in raw drawing units.

The real A3 evidence indicates common glyph heights of approximately
1.75-2.5 mm.

### VectorGlyphEvidence

Undecoded candidates are valid evidence. They are not represented as fabricated
`TextPrimitive` values.

The evidence contract contains:

- bounds;
- baseline estimate;
- provenance IDs;
- confidence;
- optional decoded value;
- optional catalog ID;
- optional classification confidence.

### Provisional template lookup

Standard sheet format and orientation identify a provisional template before
title-block confirmation. This does not authorize replacement.

For example, a detected A3 landscape page provides provisional
`A3-landscape`.

### TemplateTextEvidenceMatcher

The matcher converts template MText coordinates into page coordinates and
compares them with glyph clusters.

It may confirm only the presence and spatial structure of static template
labels. It must not claim that a glyph cluster has a particular string unless
that cluster was independently decoded.

Matching evidence includes:

- expected text-region bounds;
- observed glyph-run bounds;
- baseline agreement;
- height agreement;
- occupancy ratio;
- source provenance.

At least two independent static-label regions plus title-block grid evidence
are required to confirm a template-guided title block.

### VectorGlyphClassifier

Classification uses a versioned explicit catalog of normalized stroke
templates.

Initial catalog sources:

1. existing synthetic seven-segment digits;
2. the verified real `203 / 212 / 168` fixture, after each glyph mapping is
   independently labeled and reviewed;
3. later user-approved Cyrillic samples.

A match is accepted only when:

- the best score meets the configured threshold;
- the margin over the second-best match meets a separate ambiguity threshold;
- aspect ratio and stroke-count constraints pass.

Unknown or ambiguous candidates remain undecoded.

### TextRunAssembler

Decoded glyphs are assembled using baseline, height, rotation and gap evidence.
Separate title-block cells remain separate runs even when their baselines are
close.

The assembler produces `TextPrimitive` only for fully decoded runs. A partially
decoded run remains vector evidence.

### Title-block confirmation

`TitleBlockDetector` accepts either:

- existing real text plus grid lines; or
- template-guided vector-text evidence plus grid lines.

It does not require decoded field values merely to confirm that the standard
title block exists.

### Replacement and preservation rules

After high-confidence confirmation:

- matched frame and grid geometry may be replaced by the standard template;
- glyph geometry matched to static template labels may be replaced only when
  the spatial match is high confidence;
- decoded variable fields become editable text or attributes;
- undecoded variable glyphs remain editable source geometry on
  `TEY_REVIEW_TEXT`;
- source IDs not explicitly listed in the replacement contract remain in the
  DWG.

This prevents duplication of known static labels while preserving unknown
project values.

## Confidence model

Separate confidence values are maintained for:

- glyph segmentation;
- template-region spatial matching;
- glyph classification;
- text-run assembly;
- final title-block confirmation.

The final title-block score must not hide a low classification score. Each
decision keeps its own evidence and reason.

## Diagnostics

The conversion report adds:

- glyph candidate count;
- decoded glyph count;
- undecoded glyph count;
- decoded text-run count;
- matched static-label region count;
- title-block evidence confidence;
- template replacement source count;
- preserved review-text source count;
- rejection reason counts.

No-template and ambiguous-template outcomes remain explicit.

## Testing

### Unit tests

- provenance grouping is deterministic;
- disconnected strokes sharing a handle are split safely;
- scale-aware size filtering behaves identically at different drawing scales;
- normalized signatures are translation and scale invariant;
- ambiguous templates fail closed;
- unknown glyphs produce evidence but no `TextPrimitive`;
- text runs do not cross title-block cell boundaries;
- static-label matching does not decode text implicitly;
- replacement source IDs exclude unknown variable glyphs.

### Fixture tests

The A3 fixture must produce:

- zero fabricated decoded text with an empty catalog;
- a stable non-zero glyph candidate count;
- stable repeated-shape clusters;
- title-block evidence only when the A3 template manifest is supplied;
- preserved unknown glyph provenance.

The existing `203 / 212 / 168` fixture remains fail-closed until its explicit
catalog is supplied.

### Runtime acceptance

For `Тест А3.pdf`:

1. PDFIMPORT runs at scale `1`;
2. the A3 landscape template is selected by spatial and grid evidence;
3. the standard frame and grid are inserted in Model Space;
4. no user layout or viewport is created;
5. unknown variable glyphs remain editable on `TEY_REVIEW_TEXT`;
6. no source object outside the replacement contract is erased;
7. the saved DWG reopens successfully;
8. the report explains every replaced and preserved source group.

## Non-goals

- universal OCR;
- automatic labeling of all Cyrillic glyphs from one drawing;
- native AutoCAD `TABLE`;
- semantic interpretation of every title-block field;
- removal of all title-block source glyphs;
- support for raster-only PDFs.

## Delivery sequence

1. implement scale-aware glyph segmentation and evidence contracts;
2. integrate template-guided static-label matching;
3. confirm A3 template selection while preserving unknown glyphs;
4. add an explicit digit catalog from independently labeled fixtures;
5. assemble decoded runs and map only verified fields;
6. run AutoCAD save/reopen acceptance.
