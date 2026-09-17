# TeyPdfCad — verified project state

Last updated: 2026-09-17

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

The remaining `Associative=No` status is a separate engineering gap and must not be confused with native-object reconstruction success.

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

## Near-term priorities

1. Units / `INSUNITS` diagnostics and engineering-unit semantics. Do not silently assume that a drawing unit means millimeters; AutoCAD coordinates themselves are unitless without drawing-unit context.
2. Native dimension associativity. Current reconstructed object is native but AutoCAD Properties reports `Associative=No`.
3. Review and integrate TEST-003 findings before changing Semantic Core thresholds or algorithms.
4. Add the real vector-glyph PDF as a regression fixture and implement `VectorTextRecognizer` as an upstream module, not as ad-hoc changes inside Semantic Core.
5. Later: spatial scale partitioning for sheets containing multiple drawing scales.

## Non-negotiable validation rules

- A text override alone is never proof of correct reconstruction.
- Native `Dimension.Measurement` must agree with the semantic value within the defined validation tolerance or the transaction must roll back.
- Do not lower thresholds or relabel expected results merely to make metrics green.
- Preserve provenance/source IDs needed for safe future cleanup of exploded PDFIMPORT primitives.
