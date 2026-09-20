# SPDS Template Export Acceptance

Date: 2026-09-19

## Runtime export

The real `Пример шаблонов.dwg` library was opened by AutoCAD 2022 Core
Console in an isolated runtime and exported with `TEYPDFEXPORTTEMPLATES`.

Results:

- 17 named blocks;
- 719 reconstructable entities;
- all A0 through A4 portrait and landscape blocks exported;
- each standard sheet block contains 68 editable entities;
- standard sheet composition: 53 `AcDbPolyline` and 15 `AcDbMText`;
- one native `AcDbArc` elsewhere in the library is preserved with centre,
  radius and start/end angles;
- `mcsDbObjectFormat` is fully expanded and is no longer reported as an
  unsupported proxy class;
- the only remaining unsupported class is `AcDbPoint`, which represents
  non-printing construction points and is intentionally omitted.

## A3 conversion selection evidence

The exported manifest was supplied to the direct CLI conversion of
`Тест А3.pdf`.

The PDF was converted successfully in Model Space-only mode, but the template
was intentionally not selected:

- `templateSelected: false`;
- `templateReason: title-block-not-confirmed`;
- `textCount: 0`;
- source geometry remained editable;
- no viewport was created.

This is correct fail-closed behavior. The current title-block detector
requires both linework and text evidence. The next text-recognition work must
recover vector-glyph title-block text before this PDF can safely replace its
source frame with the standard A3 template.
