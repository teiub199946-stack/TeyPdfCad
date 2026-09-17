# TEST-003 Error Classification Design

## Goal

Separate genuine Semantic Core defects from expected PDF/PDFIMPORT noise propagation and ordinary floating-point error without changing `SemanticReconstructionEngine`, expected labels, production thresholds, AutoCAD bridge code, or case difficulty.

## Branch and isolation

- Work branch: `feature/test-003-error-classification`.
- Base: TEST-002 / PR #7 head `74020703d233477bc959840184f7385839cf3bfb`.
- Do not touch `feature/autocad-bridge` or PR #6.
- Do not modify production recognizer files or recognition thresholds.
- Do not merge this PR automatically.

## Diagnostic coordinate model

For each test case capture a deterministic `CaseGeometryTrace` containing:

1. Expected DWG/world coordinates.
2. Clean paper-space coordinates before noise.
3. Injected noise specification and observed deltas.
4. Coordinates actually supplied to Semantic Core.
5. Core result in paper-space.
6. Core result converted back to DWG/world-space.
7. Drawing scale.
8. Absolute world-space errors.
9. Normalized paper-space errors.
10. Primitive/source provenance IDs when available.

The diagnostic adapter must derive these values from the same synthetic scene that feeds Core. Expected semantic answers must never be passed into Core.

## Error tolerance model

Replace the single blind world-space point tolerance with a physically derived tolerance.

For one coordinate component, injected paper-space coordinate uncertainty is bounded by the actual observed paper-space delta for that point plus any deliberate synthetic primitive perturbation (endpoint mismatch / micro-break where relevant). The geometry classifier uses a fixed paper-space numeric floor and converts it to world units using drawing scale:

`allowedWorldError = (allowedPaperError + numericPaperEpsilon) * DrawingScale + numericWorldEpsilon`

where:

- `allowedPaperError` is derived from the recorded injected displacement for the exact expected point, not from the result error and not tuned against pass/fail outcomes;
- `numericPaperEpsilon` is a small constant representing floating-point / geometric reconstruction rounding only;
- `numericWorldEpsilon` is a fixed tiny absolute floor.

The classifier also computes `normalizedPaperError = worldError / DrawingScale`.

A result inside the injected displacement envelope is `ExpectedNoisePropagation`. A result only outside that envelope by at most the numeric epsilon is `NumericTolerance`. Anything beyond both is a real geometry defect candidate.

## Failure taxonomy

Every case receives a primary diagnostic category from at least:

- `ExpectedNoisePropagation`
- `NumericTolerance`
- `WrongGeometry`
- `WrongValue`
- `WrongScale`
- `WrongDimensionType`
- `WrongDimensionCount`
- `UnexpectedDetection`
- `MissedDetection`
- `ExpectedAbstention`
- `WrongAbstention`
- `ChainMismatch`

Classification is diagnostic and does not rewrite expected labels.

## Geometry dimensions

`WrongGeometry` is decomposed into explicit components when evidence exists:

- definition / extension-line points;
- dimension-line location;
- text anchor;
- rotation/orientation;
- broken-dimension-line handling;
- source/provenance IDs.

The existing recognizer does not currently expose every semantic detail directly. Missing fields are reported as `Unavailable`, never synthesized from expected values.

## Metrics

Semantic correctness is independent from point precision. The report must contain:

- Detection Precision
- Detection Recall
- F1
- FP
- FN
- Correct Value %
- Correct Scale %
- Correct Type %
- Correct Count %
- Correct Geometry %
- Abstention accuracy

Geometry metrics exclude cases where detection itself failed; each denominator is explicit in JSON/Markdown.

## Case cohorts

Statistics are grouped independently by:

- clean
- noisy
- broken dimension line
- chains
- outside text
- ambiguous
- negative
- non-canonical scale
- multi-scale

A case may belong to multiple cohorts.

## Error distributions

Produce P50, P90, P95, P99 and max for both:

- normalized paper-space geometry error;
- DWG/world-space geometry error.

Distributions are computed from measured geometry deltas and reported separately for all comparable detected geometry and for real-defect-only geometry.

## Worst defects

`worst_cases.json` contains the 20 worst real Core defects after excluding `ExpectedNoisePropagation` and `NumericTolerance`. Each row includes:

- seed and case ID;
- expected summary;
- actual summary;
- scale;
- injected noise;
- category;
- world/paper error magnitudes when applicable;
- short explanation.

Ranking prioritizes semantic failures first, then normalized geometry error severity, without changing classification thresholds.

## Reports

For 100, 1,000 and 10,000 deterministic runs write:

- `report.json`
- `report.md`
- `worst_cases.json`

`report.md` must explicitly answer:

1. How many former TEST-002 `WrongPoints` are real Core geometry defects.
2. How many are explained by noise/scale/numeric tolerance.
3. The 3–5 most frequent real Core defect classes.
4. Whether there is a systematic scale error.
5. Whether there is a systematic point bias.
6. How Detection Precision/Recall compare with TEST-002 after honest geometry classification.

## Regression protection

Add tests proving that:

- increasing drawing scale increases world allowance only by the documented conversion from fixed paper uncertainty;
- zero-noise cases do not receive a large tolerance;
- large actual error cannot become `ExpectedNoisePropagation` simply because the case scale is large;
- numeric epsilon is bounded and separate from injected noise;
- expected labels remain immutable;
- deterministic runs produce deterministic classification output excluding runtime timing fields.

## Definition of done

- Core CI green on final head.
- Test-generator CI green on final head.
- Deterministic real-Core runs at 100 / 1,000 / 10,000 complete and upload all three artifacts.
- Honest split between noise-induced discrepancies and Core defects is present.
- 20 worst real defects are produced for CORE-002 handoff.
- Separate PR opened; no merge performed.
