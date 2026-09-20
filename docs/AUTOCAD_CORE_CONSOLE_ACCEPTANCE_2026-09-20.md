# AutoCAD Core Console Acceptance

Date: 2026-09-20

## Environment

- AutoCAD 2022 Core Console `S.51.0.0`
- .NET Framework 4.8 AutoCAD plug-in
- `acad2022.cfg` placed beside `accoreconsole.exe`
- fresh `/isolate` user-data root per run
- trusted base drawing: `tests/fixtures/runtime/autocad2022_base.dwg`

## Health gate

The automated smoke harness:

- opened a copied base DWG with `/i`;
- loaded the Release plug-in;
- executed `TEYPDFHEALTH`;
- observed the health sentinel;
- terminated the isolated Core Console process tree.

Result: pass.

## PDF to DWG gate

Input fixture:

`artifacts/runtime-dimension-control.pdf`

The fixture contains five vector lines and TrueType text `5200`. The bridge:

1. imported page 1 with `_F` and a `25.4` scale factor into a millimetre base
   drawing;
2. captured five line primitives and one text primitive;
3. reconstructed a native linear dimension;
4. validated its AutoCAD measurement;
5. saved `artifacts/bridge-runtime-final/result.dwg`;
6. returned exit code `0`.

## Independent read-back

ACadSharp reopened the saved DWG and reported:

- total Model Space entities: 7;
- `DimensionLinear`: 1;
- `LwPolyline`: 5;
- `MText`: 1;
- native dimension measurement: `5200.00088888889`;
- measurement absolute error: less than `0.001`;
- DWG SHA-256:
  `CFCCFC12DC64CC3F23A05DD791A4CB9EF8A51D39309D0635D567D00F44123C51`.

This proves the automatic path:

`PDF -> AutoCAD Core Console PDFIMPORT -> TeyPdfCad reconstruction -> native DIMENSION -> saved DWG -> independent read-back`.

## Clean semantic output gate

The same fixture was converted with `preserveSourceGeometry=false`. Source
objects were erased only after native-dimension validation succeeded.
Independent read-back reported:

- total Model Space entities: 1;
- `DimensionLinear`: 1;
- native measurement: `5200.000888888887`;
- source polylines: 0;
- source text: 0;
- DWG SHA-256:
  `D087FF9921633816BC94843221C3C0409BE5985773E176E36B9B67B406D0C071`.

## PDFIMPORT scale contract

PDFIMPORT scale is input-dependent and is therefore explicit:

- the generated ReportLab control fixture requires
  `TEYPDFCAD_AUTOCAD_PDFIMPORT_SCALE=25.4`;
- production default is `1`;
- the real A3 PDF at scale `1` produced extents approximately
  `41999.67 x 29700.45` drawing units, confirming the known contract of
  approximately `100 drawing units/mm`;
- applying `25.4` globally would corrupt real drawing scale and is prohibited.

It does not prove universal conversion quality for arbitrary PDFs. Real drawing
acceptance, SPDS template extraction, geometry normalization and performance
gates remain separate roadmap work.
