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

Compare real Semantic Core output:

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

## Metrics and reports

Every run writes `report.json` and `report.md`.

The reports include:

- total/pass/fail;
- expected dimensions and expected negatives;
- TP/TN/FP/FN;
- wrong geometry/value/type/scale/confidence/count;
- precision, recall and F1;
- total runtime, cases/sec, median, p95 and p99;
- breakdowns by angle, scale, dimension type, arrow type, text position,
  noise level and dimension length;
- top failures and worst categories;
- baseline deltas and release-gate status.

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
just to make a failing gate green.

## Release gate

Thresholds live in `tests/TeyPdfCad.TestGenerator/testconfig.json`.

The initial gate fails when:

- false-positive rate exceeds its configured maximum;
- wrong-measurement rate exceeds its configured maximum;
- actual results are missing;
- a baseline regression is detected and `failOnBaselineRegression` is true.

## Connecting Semantic Core

Implement `ISemanticTestPipeline`.

The adapter receives one `DimensionCase` and must return one
`ActualDimensionResult`. Keep conversion from test data to `PrimitiveScene` in
the adapter or test integration layer. Do not add test-generator dependencies to
`TeyPdfCad.Core`.

When the recognizer exposes a stable API, add a Core-backed implementation next
to the test harness and feed its output into `RegressionRunner`.

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
```

The GitHub Actions workflow runs the same acceptance path for
`feature/test-generator` and pull requests.
