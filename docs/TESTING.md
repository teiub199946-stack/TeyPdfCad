# TeyPdfCad synthetic regression testing

`TeyPdfCad.TestGenerator` is the deterministic golden-test harness for PDF -> DWG
dimension reconstruction. It is intentionally separate from `TeyPdfCad.Core` and
does not reference AutoCAD assemblies.

## What it generates

The corpus includes:

- linear, rotated and aligned dimensions;
- fixed values from 10 through 30000 plus deterministic random values;
- angles from 0 through 179 degrees, including almost horizontal/vertical cases;
- scales 1:1 through 1:500;
- centered/above/below/offset/outside text placement;
- ClosedFilled, ClosedBlank, Open, ArchitecturalTick, Oblique and Dot arrows;
- adversarial short/long, broken-line, flipped-text and dense/intersecting targets;
- dimension chains with 2, 3, 5, 10 and 20 dimensions, both equal and mixed segment lengths;
- negative lookalikes that must be rejected;
- ambiguous borderline evidence targets where the correct action is to abstain;
- controlled coordinate noise at 0, 0.001, 0.01, 0.05, 0.1 and 0.5.

After the mandatory bootstrap cases, the sustained corpus mix is approximately
25% negative lookalikes, 10% dimension chains, 5% ambiguous targets and 60%
positive single dimensions. This makes false-positive and chain regressions
statistically visible instead of representing them with only a handful of fixtures.

Chain orientation varies independently across horizontal, vertical and inclined
cases. Chains alternate equal-length and mixed-length segments, so spacing between
centered dimension texts varies naturally with segment geometry.

Each `DimensionCase` stores two geometries:

1. expected semantic geometry (`P1`, `P2`, `DimensionLinePoint`, `TextPosition`);
2. `ObservedGeometry`, where controlled PDFIMPORT-like jitter can be applied.

This keeps the golden answer clean while preserving a reproducible noisy input target.

## Determinism

The generator uses an in-repository SplitMix64 implementation rather than
`System.Random`. Therefore the sequence is controlled by this codebase instead of
runtime implementation details.

The same seed and count must serialize to byte-equivalent `cases.json`.

The required determinism check is:

```bash
dotnet run --project tests/TeyPdfCad.TestGenerator -- verify
```

`verify` generates 100, 1000 and 10000 case corpora, generates seed `12345`
twice, compares the serialized payloads and writes a SHA-256 fingerprint.

## Commands

Generate a corpus:

```bash
dotnet run --project tests/TeyPdfCad.TestGenerator -- \
  generate --count 10000 --seed 12345 --output artifacts/generated/run-001
```

Add `--split` to also write one JSON file per case. Large generated outputs belong
under `artifacts/` and are ignored by Git.

Run the harness self-check:

```bash
dotnet run --project tests/TeyPdfCad.TestGenerator -- \
  self-check --count 10000 --seed 12345 --output artifacts/generated/self-check
```

`self-check` uses `ExpectedEchoPipeline`. It proves the generator/runner/reporting
plumbing only. It is deliberately not presented as Semantic Core accuracy.

Run the real Semantic Core benchmark:

```bash
dotnet run --project tests/TeyPdfCad.TestGenerator -- \
  core-run --count 10000 --seed 12345 --output artifacts/semantic-core/10000
```

`core-run` performs the full CAD-neutral test path:

```text
DimensionCase drawing coordinates
  -> paper/PDFIMPORT-like PrimitiveScene
  -> SemanticReconstructionEngine
  -> detected paper-space semantics
  -> drawing coordinates
  -> RegressionRunner
```

The adapter does not feed expected type, expected value, expected confidence or expected
result into the Core recognizer. Without `--baseline`, `core-run` always writes the
honest report and exits successfully even when the report's release gate is `FAIL`.
This is intentional for first-baseline measurement and GitHub Actions artifact capture.
With `--baseline`, the normal regression gate is enforced.

Compare externally supplied actual results:

```bash
dotnet run --project tests/TeyPdfCad.TestGenerator -- \
  compare \
  --cases artifacts/generated/run-001/cases.json \
  --actual artifacts/generated/run-001/actual.json \
  --output artifacts/generated/run-001/result \
  --baseline baselines/dimension-v1.json
```

The `actual.json` input is a JSON array of `ActualDimensionResult` objects keyed
by `caseId`.

## Regression outcomes

The runner classifies every case as one of:

- Correct;
- FalsePositive;
- FalseNegative;
- WrongValue;
- WrongPoints;
- WrongType;
- WrongScale;
- WrongConfidence;
- WrongCount;
- AmbiguousMismatch;
- MissingActual.

Definition points are compared with endpoint order tolerance, so a recognizer is
not failed merely for swapping P1/P2.

Exploded non-axis PDF primitives can be compatible with both native AutoCAD
`AlignedDimension` and `RotatedDimension` when the original native class has been
lost. `SemanticCoreTestPipeline` derives this condition from the generated
`PrimitiveScene` and reports it as `SemanticTypeAmbiguity`; it does not inspect the
expected case label to manufacture a passing type.

## Metrics and reports

Every run writes `report.json` and `report.md`.

The reports include:

- total/pass/fail;
- expected dimensions and expected negatives;
- TP/TN/FP/FN;
- wrong geometry/value/type/scale/confidence/count;
- semantic type ambiguity count;
- precision, recall and F1;
- total runtime, cases/sec, median, p95 and p99;
- breakdowns by angle, scale, dimension type, arrow type, text position,
  noise level and dimension length;
- top failures and worst categories;
- baseline deltas and release-gate status.

## First real Semantic Core baseline — TEST-002

Seed `12345`, 10,000 deterministic synthetic cases, real
`SemanticReconstructionEngine`:

| Metric | Result |
|---|---:|
| Precision | 94.979% |
| Recall | 85.931% |
| F1 | 90.229% |
| False positive | 318 |
| False negative | 985 |
| Wrong measurement | 0 |
| Wrong geometry/count | 3592 |
| Wrong type | 658 |
| Semantic type ambiguity | 2595 |
| Full semantic pass rate | 39.470% |
| False-positive rate on expected negatives | 12.725% |

The release gate is `FAIL` because the false-positive rate exceeds the configured
0.5% maximum. The metric is intentionally not softened or rebased away.

The main confirmed Core weaknesses from this baseline are tracked separately:

- false-positive negative-evidence rejection (`LinesTextNoArrows`, block/table lookalikes);
- recall degradation under larger PDFIMPORT noise, high drawing scales and outside text;
- no explicit Core abstention/ambiguous channel for borderline evidence;
- unstable detected counts in longer dimension chains.

### Geometry/noise caveat

`WrongPoints` is intentionally retained in the honest report, but it must not be
interpreted automatically as a pure recognizer defect. TEST-001 stores a clean DWG
golden geometry plus injected PDFIMPORT-like paper-space coordinate jitter. When
paper-space jitter is mapped back through drawing scale, the resulting drawing-space
coordinate delta can legitimately exceed the fixed `0.05` point tolerance, especially
at large scales. TEST-002 therefore does not hide these failures, but Core bug triage
must separate actual reconstruction error from propagated input noise before changing
the production recognizer or the tolerance policy.

## Baseline

Create a baseline from an accepted report:

```bash
dotnet run --project tests/TeyPdfCad.TestGenerator -- \
  baseline --report artifacts/generated/run-001/result/report.json \
  --output baselines/dimension-v1.json
```

A later run can pass that file through `--baseline`. Precision, recall, F1, pass
rate, false-positive rate and wrong-measurement rate are compared. A degradation
beyond the configured tolerance is reported as `REGRESSION`.

Baseline changes should be deliberate and reviewable. Do not update a baseline
just to make a failing gate green. The TEST-002 result above is an observed baseline,
not automatically an accepted release baseline.

## Release gate

Thresholds live in `tests/TeyPdfCad.TestGenerator/testconfig.json`.

The initial gate fails when:

- false-positive rate exceeds its configured maximum;
- wrong-measurement rate exceeds its configured maximum;
- actual results are missing;
- a baseline regression is detected and `failOnBaselineRegression` is true.

## Connecting Semantic Core

`SemanticCoreTestPipeline` is the Core-backed implementation of
`ISemanticTestPipeline`. `DimensionCasePrimitiveSceneBuilder` owns test-case to
`PrimitiveScene` conversion. This keeps synthetic/test concerns outside
`TeyPdfCad.Core`.

The current TEST-002 adapter converts clean drawing coordinates to paper coordinates,
applies controlled PDFIMPORT-like evidence/noise, invokes `SemanticReconstructionEngine`,
and maps detected paper-space points back with the detected drawing scale.

Do not add test-generator dependencies to `TeyPdfCad.Core` and do not change the
production recognizer simply to improve the TEST-002 score.

## Future AutoCAD round trip

`IAutoCadTestPipeline` is the prepared AutoCAD boundary. A future implementation
can perform:

```text
DimensionCase
  -> native AutoCAD DIMENSION
  -> DWG
  -> plot PDF
  -> PDFIMPORT
  -> PrimitiveScene
  -> Semantic Core
  -> ActualDimensionResult
  -> RegressionRunner
```

AutoCAD API references belong in the adapter implementation, never in Core and
never in the mathematical case generator.

## CI / local acceptance

From repository root:

```bash
dotnet restore TeyPdfCad.sln
dotnet build TeyPdfCad.sln --configuration Release --no-restore
dotnet test TeyPdfCad.sln --configuration Release --no-build
dotnet run --project tests/TeyPdfCad.TestGenerator \
  --configuration Release --no-build -- verify --output artifacts/generated/ci
dotnet run --project tests/TeyPdfCad.TestGenerator \
  --configuration Release --no-build -- core-run --count 10000 --seed 12345 \
  --output artifacts/semantic-core/10000
```

GitHub Actions runs the deterministic harness plus real Semantic Core baselines at
100, 1,000 and 10,000 cases and uploads `report.json` / `report.md` as the
`semantic-core-regression` artifact.
