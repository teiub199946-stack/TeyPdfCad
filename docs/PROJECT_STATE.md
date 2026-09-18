# TeyPdfCad — verified project state

Last updated: 2026-09-18

This file records verified engineering facts and near-term priorities. It is intended to be the durable project-memory checkpoint for work across chats and branches. Claims belong here only after direct evidence or CI evidence exists.

## Product invariant

TeyPdfCad reconstructs editable native CAD semantics from vector PDF rather than merely reproducing exploded geometry. The first semantic target is native AutoCAD `Dimension` with a real `Measurement`, not LINE/TEXT plus a fake displayed value.

Current temporary pipeline:

`Vector PDF -> AutoCAD PDFIMPORT -> PrimitiveScene -> SemanticReconstructionEngine -> native AutoCAD Dimension -> DWG`

Semantic Core must remain independent of the AutoCAD host so PDFIMPORT can later be replaced by a direct PDF primitive reader.

## Verified host target

- AutoCAD 2022, product version observed in the real test environment: `S.51.0.0 AutoCAD 2022`.
- AutoCAD API family: 24.1.
- Host target: .NET Framework 4.8.
- `NETLOAD` succeeds in real AutoCAD 2022.
- `TEYPDFPING` succeeds and confirms command registration.

## Verified real E2E proof — 2026-09-17

Control file: `TeyPdfCad_TrueType_E2E_Test.pdf`.

Real `TEYPDFANALYZE` output in AutoCAD 2022:

- selected AutoCAD objects: 16
- generated line primitives: 13
- generated text primitives: 3
- reconstructed semantic dimensions: 3
- dimension chains: 0
- detected scale: approximately `7200.0015`
- average confidence: `93.57%`

Recognized dimensions:

- Aligned, source text `5200`, value `5200`, confidence `85.9%`, provenance sources `4`
- Rotated, source text `7200`, value `7200`, confidence `97.4%`, provenance sources `4`
- Rotated, source text `5200`, value `5200`, confidence `97.4%`, provenance sources `4`

Real `TEYPDFRECONSTRUCT` result:

- created 3 native AutoCAD dimensions
- scale approximately `7200.0015`
- validator-reported maximum native measurement relative error: `0.0001%`
- source PDFIMPORT primitives intentionally preserved for review

Independent AutoCAD Properties check on the reconstructed `5200` object:

- object type shown by Russian AutoCAD UI: `Параллельный размер` (`Aligned Dimension`)
- dimension style: `ISO-25`
- actual dimension measurement: `5199.9972`
- target semantic value: `5200`
- absolute difference: approximately `0.0028` drawing units
- text-string override field is empty; the displayed value is not being faked by a text override
- associative property is currently `No`

Therefore the first product invariant is directly demonstrated on real AutoCAD 2022:

`Vector PDF -> PDFIMPORT -> PrimitiveScene -> Semantic Core -> native Dimension -> real Measurement ~= semantic value`

### Associativity interpretation

`Associative=No` is not yet proven to be a defect in the writer. The first TrueType control PDF was intentionally minimal and primarily contains dimension graphics; it does not provide a robust independent measured-object geometry target for every extension point. AutoCAD associativity requires a dimension point to be coupled to actual geometry. Setting `DIMASSOC=2` alone is not sufficient proof that an API-created dimension is correctly associated.

The next associativity acceptance fixture must therefore include explicit measured geometry (for example a rectangle/line) plus dimensions whose extension origins land on that geometry. PASS requires that editing the measured object updates the reconstructed dimension automatically.

### Managed AutoCAD 2022 associativity API spike

A separate throwaway branch `feature/autocad-2022-dimassoc-spike` probed the exact AutoCAD 2022 managed assemblies used by this project.

Evidence:

- initial compile-time probe referenced `DimAssoc`, `OsnapPointRef`, and `DimAssocPointType` directly;
- CI run `35259318178` compiled the production bridge but the test project failed with `CS0246` for all three types;
- a follow-up reflection probe on commit `34aa13c6f83ceb153dab6914513144209538470f` verified that the AutoCAD 2022 managed DatabaseServices assembly does not export those native association types;
- the same probe verifies that `Database.DimAssoc` exists as the DIMASSOC system setting, while `Dimension` exposes no public instance member containing `Assoc`;
- AutoCAD 2022 Adapter CI run `35259524604` passed the reflection probe, build, tests and package verification.

Engineering conclusion: the current pure C# managed writer can create correct native dimensions, but full native geometry association is not available through the public managed types probed here. Do not fake this by merely setting `DIMASSOC=2`. If full associative reconstruction becomes an MVP requirement, evaluate a narrow ObjectARX/C++ bridge or another verified native API path as a separate architectural task. Keep this concern outside Semantic Core.

## Units / INSUNITS diagnostics — implemented, CI-verified, and real-run verified

AutoCAD drawing coordinates are drawing units; they are not intrinsically millimeters. Autodesk defines `INSUNITS=4` as millimeters, `INSUNITS=6` as meters, and `INSUNITS=0` as undefined/unitless context.

A new adapter diagnostic reports the current drawing units in both `TEYPDFANALYZE` and `TEYPDFRECONSTRUCT` without silently changing them:

- `Millimeters`: explicitly reports `INSUNITS=4` and `1 drawing unit = 1 mm`.
- `Undefined`: explicitly reports the engineering-unit interpretation as unverified and does not claim millimeters.
- other units such as `Meters`: explicitly reports that current drawing units are not millimeters and must not be interpreted as mm without conversion.

TDD evidence:

- RED run: AutoCAD 2022 CI run `35258145634`; 3 new unit-diagnostic tests failed because `DrawingUnitDiagnostics` did not yet exist while the previous 7 tests passed.
- GREEN run: AutoCAD 2022 CI run `35258421175`; build, all tests, net48 output verification, package staging and artifact upload passed.

Real AutoCAD 2022 units gate — verified 2026-09-17 using the latest units-enabled DLL and the same TrueType control PDF:

- `TEYPDFANALYZE` again returned `selected=16, lines=13, texts=3, dimensions=3, chains=0, scales=[7200.0015], avg confidence=93.57%`;
- the adapter printed exactly: `TeyPdfCad units: INSUNITS=4 (Millimeters). Engineering interpretation verified: 1 drawing unit = 1 mm.`

Therefore, in this verified control drawing, the reconstructed native measurements are explicitly millimeter-context values: `5200` means approximately `5200 mm` and `7200` means approximately `7200 mm`. This conclusion is tied to the observed `INSUNITS=4`; it must not be generalized to drawings whose unit context is different or undefined.

No automatic unit conversion and no Semantic Core change were introduced by this step.

## Verified real vector-glyph / SHX-style failure case

A separate real PDF imported into AutoCAD produced:

- selected AutoCAD objects: 35
- generated line primitives: 39
- text primitives: 0
- semantic dimensions: 0

Visible dimension labels were imported as vector glyph geometry rather than DBText/MText. AutoCAD `PDFSHXTEXT` failed to recover the selected labels; after threshold experiments the best-font result reached 0% for the tested selection.

This PDF must be preserved as a required future regression fixture. Do not weaken Semantic Core thresholds to force it through. The responsible future layer is a `VectorTextRecognizer` / vector-glyph text reader upstream of Semantic Core.

## Test infrastructure state

- TEST-001: deterministic synthetic generator and release/report infrastructure.
- TEST-002 / PR #7: integration of generated cases with the real Semantic Core; approximately 3309 historical `WrongPoints` in the 10,000 run must not automatically be called Core defects because paper-space noise multiplied by drawing scale can exceed a fixed 0.05 DWG-unit tolerance.
- TEST-003 belongs to the helper developer: classify noise propagation / numeric tolerance separately from real Core semantic defects. Do not modify Semantic Core merely to improve metrics.

## Branch isolation rules

- Do not modify or merge `feature/autocad-bridge` / PR #6 as part of current AutoCAD 2022 E2E work.
- Current real-E2E work branch: `feature/autocad-2022-e2e-real`.
- PR #13 is the isolated AutoCAD 2022 real-PDF diagnostics/E2E branch and remains review-gated.
- Helper TEST-002/TEST-003 work must remain isolated from the AutoCAD host branch.
- `feature/autocad-2022-dimassoc-spike` is a throwaway API-probe branch; do not merge it into product code.

## Near-term priorities

1. Final-review PR #13 now that the real AutoCAD 2022 units gate is verified; keep merge gated on fresh CI/review evidence.
2. Keep native dimension associativity outside the current C# MVP unless a verified native bridge is explicitly approved. The next associativity fixture with explicit measured geometry remains useful for future bridge validation.
3. Review and integrate TEST-003 findings before changing Semantic Core thresholds or algorithms.
4. Add the real vector-glyph PDF as a regression fixture and implement `VectorTextRecognizer` as an upstream module, not as ad-hoc changes inside Semantic Core.
5. Later: spatial scale partitioning for sheets containing multiple drawing scales.

## Non-negotiable validation rules

- A text override alone is never proof of correct reconstruction.
- Native `Dimension.Measurement` must agree with the semantic value within the defined validation tolerance or the transaction must roll back.
- Do not lower thresholds or relabel expected results merely to make metrics green.
- Preserve provenance/source IDs needed for safe future cleanup of exploded PDFIMPORT primitives.
- Never silently assign engineering units from the appearance of a dimension label; unit semantics must be explicit or separately inferred and validated.

## Web/API lifecycle — verified 2026-09-18

The Web/API path now publishes a terminal `ConversionResult` before changing the queue status to a terminal state. This removes the observable window where `GET /jobs/{jobId}` could report `completed` while `/result` still returned `result_pending`. If queue completion fails, the pre-published result is removed. The hosted worker no longer publishes a second copy; `ConversionJobRunner` owns the ordering and lifecycle transition.

Evidence:

- `TeyPdfCad.Web` build: 0 warnings, 0 errors.
- `TeyPdfCad.Web.Api` build: 0 warnings, 0 errors.
- `TeyPdfCad.Web.Tests`: 22 passed.
- In-memory HTTP smoke: `/ready` returned 200, job submission returned 202, the job reached `completed`, `/result` returned 200, and the downloaded artifact began with the valid DWG header `AC1032`.

This is in-memory lifecycle/artifact evidence only. It does not certify AutoCAD conversion. The AutoCAD plugin still requires AutoCAD 2022 host assemblies and a working Core Console profile for runtime acceptance.

## AutoCAD Core Console gate — rechecked 2026-09-18

The real bridge was invoked with the installed AutoCAD 2022 Core Console, the built `TeyPdfCad.AutoCAD.dll`, the `autocad-live-minimal.pdf` fixture, and the discovered `СПДС 2023` profile. Core Console started but exited with code `1`; the bridge returned exit code `22` before PDFIMPORT. The preserved log reports that `C:\Program Files\Autodesk\AutoCAD 2022\acad2022.cfg` is unavailable or cannot be processed, followed by `Невозможно обработать файл конфигурации!`. A direct filesystem check found no `acad2022.cfg` under the installed AutoCAD 2022 directory. No output DWG was produced.

This is an installation/runtime prerequisite failure. Do not treat the header-only in-memory smoke artifact as a DWG conversion result, and do not claim the bridge production-ready until AutoCAD is repaired or its configuration is restored and a real Core Console run produces an independently inspectable DWG.

The Web API readiness contract now checks the same prerequisite before accepting AutoCAD jobs. With the current installed paths and missing configuration file, `/ready` returns HTTP 503 with `autocad_configuration_not_found`. This is fail-closed readiness evidence; it does not replace the later Core Console and saved-DWG acceptance gate.

## Code-only completion gate — 2026-09-18

The bounded code work is complete for this checkpoint:

- Core now has an explicit, fail-closed `VectorTextRecognizer` upstream module. It accepts only registered normalized stroke templates, merges provenance, does not mutate `PrimitiveScene`, and returns rejections for unknown groups. The default template set is deliberately limited to conservative seven-segment control digits; the real SHX fixture remains unrecognized until a verified template set is supplied.
- Bridge protocol contracts are centralized: protocol v1, stable exit codes, DWG header/status validation, strict CLI argument validation, same input/output rejection, settings propagation through the Core Console environment, and bounded cleanup after cancellation.
- A3/title-block detection now includes line segments crossing the candidate region even when both endpoints are outside the region.
- AutoCAD Web readiness fails closed when `acad2022.cfg` is missing beside Core Console.

Verified sequential Release evidence:

- `TeyPdfCad.Tests`: 40 passed.
- `TeyPdfCad.AutoCAD.Bridge.Tests`: 27 passed.
- `TeyPdfCad.Web.Tests`: 24 passed.
- Web/API, Bridge, Core, and AutoCAD plugin builds: 0 warnings, 0 errors. The plugin build used the installed AutoCAD 2022 API directory as an explicit local reference source.

The next gate is manual AutoCAD acceptance by the operator: load the Release plugin, run health/PDFIMPORT/dump/reconstruct/sheet audit, save and reopen a DWG, then return the command transcript, fixture JSON, and resulting DWG. No real AutoCAD conversion success is claimed until those artifacts exist and pass independent inspection.

The first manual PDFIMPORT attempt exposed a defective control fixture rather than a plugin failure: `autocad-live-minimal.pdf` contained literal `` `n`` sequences instead of line breaks. The fixture was corrected in place and independently parsed with Poppler `pdfinfo` as a one-page PDF 1.4 document. The next AutoCAD step is to retry PDFIMPORT with the corrected file before evaluating any reconstruction behavior.

The first real A3 PDF dump then exposed a scale contract: the imported page extents were approximately `2829.645..44829.645` by `2065.104..31801.104`, while the physical page is 420 x 297 mm. This is 100 drawing units per millimetre. `SheetPageBounds`, title-block region calculations, the AutoCAD reader, and `TEYPDFSHEETCONFIG` now carry an explicit `DrawingUnitsPerMm` value; the rebuilt Release plugin must be loaded before reconstruction is tested. For this fixture use width `420`, height `297`, scale `100`, MinX `2829.645`, and MinY `2065.104`.

The first scaled reconstruction still returned no sheet candidate because PDFIMPORT produced `texts=0`: the title-block labels are SHX/vector linework. The reader now has an explicit geometry-only title-block fallback when a configured sheet has linework but no text primitives. It preserves empty text fields and source provenance, so it enables layout geometry without inventing labels. Core tests are now 44/44 and the Release AutoCAD plugin rebuild is clean.
