# GUI Acceptance Boundary Audit

Date: 2026-09-18
Repository: `TeyPdfCad-restored`
Scope: bounded read-only audit of the documented GUI evidence boundary. No source or test files were changed. AutoCAD/Core Console was not started by this audit.

## Result

The checkout has a coherent manual GUI acceptance procedure, and the command implementations contain the expected read-only fixture and native-measurement validation safeguards. The evidence available in this checkout does **not** establish a current production conversion gate. The historical GUI result in `docs/PROJECT_STATE.md` is a recorded prior result; the current bridge/runtime gate remains unconfirmed and is currently fail-closed.

## Evidence that is actually present

### Health command

- `src/TeyPdfCad.AutoCAD/PluginEntryPoint.cs:25-38` registers `TEYPDFHEALTH`, prints `TeyPdfCad health: ok.`, and optionally writes an `ok` sentinel file.
- `tools/autocad-plugin-smoke.ps1:52-60` requires Core Console exit code `0` and the sentinel. The script treats either missing condition as failure; a printed command message alone is not enough.
- `tools/prepare-autocad-test.ps1:58-67` describes the manual sequence and explicitly keeps production readiness gated on Core Console exit `0`, bridge status `ok`, and independent DWG inspection.

This proves the intended health evidence contract and its fail-closed checks. It does not prove that the installed AutoCAD profile currently starts.

### Fixture capture (`TEYPDFDUMP` / `TEYPDFDUMPALL`)

- `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs:42-80` implements selected-object `TEYPDFDUMP`: it reads the adapter scene, serializes a versioned JSON fixture, reports the full path and counts, and does not commit a transaction.
- `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs:82-95` implements `TEYPDFDUMPALL` over ModelSpace and delegates to the same fixture writer.
- `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs:231-257` writes `PrimitiveScene_<timestamp>.json` under `%TEMP%\\TeyPdfCad` and reports `selected`, `lines`, `texts`, and `INSUNITS` without changing the drawing.
- `docs/AUTOCAD_MVP_TEST.md:97-102` defines the fixture as read-only evidence and requires preserving it before threshold or recognizer changes.
- `tests/TeyPdfCad.AutoCAD.Tests/PrimitiveSceneFixtureFormatterTests.cs:30-61` verifies schema, units, geometry, text, and provenance fields; `:11-28` verifies byte stability under insertion-order changes.

Two physical fixture files were found outside the repository at `C:\Users\Admin\AppData\Local\Temp\TeyPdfCad`:

| File | Physical contents | Interpretation |
|---|---|---|
| `PrimitiveScene_20260918_130633357.json` (2,509 bytes) | schema `TeyPdfCad.PrimitiveScene.v1`, `selectedCount=16`, `insunits=4`, `lines=13`, `texts=3`, first text `5200` | consistent with the TrueType control fixture described in `docs/PROJECT_STATE.md:21-43` |
| `PrimitiveScene_20260917_192953547.json` (4,708 bytes) | schema `TeyPdfCad.PrimitiveScene.v1`, `selectedCount=35`, `insunits=4`, `lines=39`, `texts=0` | consistent with the vector-glyph failure case described in `docs/PROJECT_STATE.md:114-121` |

These JSON files are physical adapter outputs and support the primitive-scene/fixture boundary. They do not contain a native AutoCAD `Dimension` object or a `Dimension.Measurement` result. Their presence alone is not reconstruction or production evidence.

### Native dimension and `Dimension.Measurement`

- `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs:150-209` shows the acceptance order: analyze, reject invalid/multiple scale groups, write native dimensions, validate them, and commit only after validation succeeds.
- `src/TeyPdfCad.AutoCAD/ReconstructionCommands.cs:214-219` reports the number of native dimensions and maximum native measurement error after commit.
- `src/TeyPdfCad.AutoCAD/NativeDimensionValidator.cs:24-35` compares each native `Dimension.Measurement` with the semantic expected value and rejects non-finite or over-tolerance values.
- `docs/AUTOCAD_MVP_TEST.md:104-126` requires opening AutoCAD Properties and checking the actual `Measurement`; it explicitly rejects a text override as proof.
- `docs/PROJECT_STATE.md:45-64` records a prior manual GUI result: three native dimensions, maximum relative error `0.0001%`, and an independent Properties value `5199.9972` for semantic `5200`. This is historical project-state evidence, not a newly reproduced run in this checkout.

No DWG containing the reconstructed dimensions, AutoCAD Properties screenshot, or command transcript is present in the repository or under `artifacts/autocad-test` at audit time. Therefore the historical `PROJECT_STATE` numbers cannot be independently re-opened from a saved artifact here.

## Boundary: GUI evidence versus runtime/production gate

The following are separate claims and must not be merged:

1. **GUI/manual acceptance evidence:** a user-operated AutoCAD session can show `TEYPDFHEALTH`, capture a fixture, run reconstruction, and inspect native `Dimension.Measurement`. The command/source contracts and the procedure are documented above. `docs/PROJECT_STATE.md:21-64` records that this happened previously for the TrueType control PDF.
2. **Core Console bridge evidence:** the bridge must start the configured Core Console, run its script, return exit code `0`, write a status of `ok`, and produce a valid independently inspected DWG. This is required by `README.md:45-63`, `tools/README-autocad-batch.md:23-27`, and `tools/prepare-autocad-test.ps1:66-67`.
3. **Current production readiness:** the current checkout has not crossed the second gate. The physical `C:\Users\Admin\Documents\ChatGPT\TeyConvert\acad.err:3` records `Problem with setting up current profile.` The readiness log records repeated `GET /ready` responses with HTTP `503` (`C:\Users\Admin\AppData\Local\Temp\teypdfcad-readiness.stdout.log:9-20`). No current successful Core Console status or independently inspected output DWG was found.

Consequently, this audit confirms documented/manual GUI acceptance boundaries and fixture artifacts, while production/runtime acceptance remains **OPEN / NOT PASS**. It is incorrect to report the current bridge as a successful AutoCAD conversion based only on the historical GUI text or the fixture JSON.

## Open gaps

- Re-run `TEYPDFHEALTH` through Core Console only after the AutoCAD profile startup problem is fixed; retain exit code, sentinel, and full run directory.
- Re-run `TEYPDFDUMPALL` and `TEYPDFRECONSTRUCTALL` on the control DWG/PDF and preserve the command transcript, fixture JSON, output DWG, and an independent Properties check for `Dimension.Measurement`.
- Attach or archive the DWG/screenshot evidence referenced by `docs/PROJECT_STATE.md:45-58`; the current checkout has the claim but not the inspectable artifact.
- Keep the vector-glyph fixture (`texts=0`) as a regression input; it is not a valid TrueType reconstruction proof (`docs/AUTOCAD_MVP_TEST.md:53-74`).
- Do not mark the bridge production-ready until the explicit Core Console/status/output criteria in `tools/prepare-autocad-test.ps1:66-67` are satisfied.

## Audit conclusion

**GUI acceptance boundary: documented and source-consistent; historical manual result recorded.**

**Current Core Console/production gate: not demonstrated and currently fail-closed by profile startup/readiness evidence.**

