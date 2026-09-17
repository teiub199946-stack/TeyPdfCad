# TEST-003 Error Classification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic diagnostics layer that separates injected PDF/PDFIMPORT noise and numeric tolerance from genuine Semantic Core defects, without changing Semantic Core.

**Architecture:** Extend the TEST-002 test adapter with a traceable geometry snapshot and add a standalone `ErrorClassifier`/`DiagnosticReportBuilder` in the test-generator project. Classification operates only on expected case data, the actual synthetic scene supplied to Core, and Core output; Core itself remains untouched. Existing TEST-002 detection semantics are preserved while TEST-003 adds independent geometry and semantic quality metrics.

**Tech Stack:** .NET 8, C#, xUnit, existing `TeyPdfCad.TestGenerator`, existing `SemanticCoreTestPipeline`, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-09-17-test-003-error-classification-design.md`

## Global Constraints

- Do not modify `SemanticReconstructionEngine` or production recognizer thresholds.
- Do not modify expected labels or remove/downgrade hard cases.
- Do not touch `feature/autocad-bridge` or PR #6.
- Do not tune tolerance from observed failures.
- No automatic merge.

---

### Task 1: Diagnostic geometry trace and tolerance contract

**Files:**
- Create: `tests/TeyPdfCad.TestGenerator/Diagnostics/DiagnosticModels.cs`
- Create: `tests/TeyPdfCad.TestGenerator/Diagnostics/GeometryTolerance.cs`
- Test: `tests/TeyPdfCad.Tests/ErrorClassificationTests.cs`

**Interfaces:**
- `GeometryTolerance.Calculate(PointDiagnosticInput input) -> GeometryToleranceResult`
- `GeometryToleranceResult` exposes allowed paper/world error, actual paper/world error and cause classification.
- `DiagnosticCategory` contains all TEST-003 categories.

- [ ] Write failing tests proving zero-noise tolerance stays bounded, scale conversion is linear, noise allowance derives from injected displacement, and a large error is never excused only because scale is large.
- [ ] Run `dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --configuration Release` and capture the expected RED failure.
- [ ] Implement minimal diagnostic models and tolerance formula from the spec.
- [ ] Re-run the targeted tests and confirm GREEN.
- [ ] Commit as `test: add scale-aware geometry tolerance contract`.

### Task 2: Capture the exact scene supplied to Semantic Core

**Files:**
- Modify: `tests/TeyPdfCad.TestGenerator/Pipelines/DimensionCasePrimitiveSceneBuilder.cs`
- Modify: `tests/TeyPdfCad.TestGenerator/Pipelines/SemanticCoreTestPipeline.cs`
- Create: `tests/TeyPdfCad.TestGenerator/Diagnostics/SceneTraceBuilder.cs`
- Test: `tests/TeyPdfCad.Tests/SceneTraceTests.cs`

**Interfaces:**
- `SceneTraceBuilder.Build(DimensionCase testCase, PrimitiveScene scene) -> CaseGeometryTrace`
- `SemanticCoreTestPipeline.RunDetailedAsync(...) -> DiagnosticActualResult` or equivalent test-only trace surface while existing `RunAsync` remains compatible.

- [ ] Write tests showing clean paper coordinates, observed/noisy paper coordinates, world coordinates, drawing scale and provenance are recorded from the same `PrimitiveScene` used by Core.
- [ ] Run tests RED.
- [ ] Implement trace capture without modifying Core types.
- [ ] Run tests GREEN and full existing pipeline tests.
- [ ] Commit as `feat: capture Semantic Core geometry trace`.

### Task 3: Diagnostic classification taxonomy

**Files:**
- Create: `tests/TeyPdfCad.TestGenerator/Diagnostics/ErrorClassifier.cs`
- Test: `tests/TeyPdfCad.Tests/ErrorClassificationTests.cs`

**Interfaces:**
- `ErrorClassifier.Classify(DimensionCase expected, ActualDimensionResult actual, CaseGeometryTrace trace) -> CaseDiagnostic`
- `CaseDiagnostic` contains primary category, geometry subchecks, point errors, explanation and semantic correctness flags.

- [ ] Add failing tests for UnexpectedDetection, MissedDetection, ExpectedAbstention, WrongAbstention, WrongValue, WrongScale, WrongDimensionType, WrongDimensionCount, ChainMismatch, ExpectedNoisePropagation, NumericTolerance and real WrongGeometry.
- [ ] Run RED.
- [ ] Implement deterministic precedence rules so detection/abstention errors classify before downstream semantic/geometry checks.
- [ ] Add geometry subchecks for definition points, dimension-line location, text anchor/orientation/broken line/provenance with `Unavailable` where Core does not expose data.
- [ ] Run GREEN.
- [ ] Commit as `feat: classify TEST-003 Core diagnostics`.

### Task 4: Independent semantic metrics and error distributions

**Files:**
- Create: `tests/TeyPdfCad.TestGenerator/Diagnostics/DiagnosticReportBuilder.cs`
- Modify: `tests/TeyPdfCad.TestGenerator/Models/Models.cs`
- Test: `tests/TeyPdfCad.Tests/DiagnosticReportTests.cs`

**Interfaces:**
- `DiagnosticReportBuilder.Build(TestCorpus, IReadOnlyList<CaseDiagnostic>) -> DiagnosticRegressionReport`
- Report exposes Detection Precision/Recall/F1, FP/FN, Correct Value/Scale/Type/Count/Geometry %, Abstention accuracy, cohorts, percentiles and bias diagnostics.

- [ ] Write failing metric tests with fixed tiny corpora and hand-computed denominators.
- [ ] Run RED.
- [ ] Implement metrics, cohorts (`clean`, `noisy`, `broken dimension line`, `chains`, `outside text`, `ambiguous`, `negative`, `non-canonical scale`, `multi-scale`) and P50/P90/P95/P99/max for paper/world errors.
- [ ] Add systematic scale-error and signed point-bias diagnostics.
- [ ] Run GREEN.
- [ ] Commit as `feat: add independent semantic and geometry metrics`.

### Task 5: Worst-case extraction and TEST-003 reports

**Files:**
- Create: `tests/TeyPdfCad.TestGenerator/Diagnostics/WorstCaseSelector.cs`
- Modify: `tests/TeyPdfCad.TestGenerator/Reporting/ArtifactWriter.cs`
- Modify: `tests/TeyPdfCad.TestGenerator/Program.cs`
- Test: `tests/TeyPdfCad.Tests/DiagnosticReportTests.cs`

**Interfaces:**
- CLI: `diagnose-core --count N --seed N --output DIR`
- Writes `report.json`, `report.md`, `worst_cases.json`, and corpus/trace artifacts as needed.

- [ ] Write tests ensuring `ExpectedNoisePropagation` and `NumericTolerance` are excluded from worst real Core defects.
- [ ] Write markdown contract tests for the six mandatory TEST-003 conclusions.
- [ ] Run RED.
- [ ] Implement top-20 deterministic ranking and report serialization.
- [ ] Run GREEN.
- [ ] Commit as `feat: emit TEST-003 diagnostic reports`.

### Task 6: Determinism and regression hardening

**Files:**
- Test: `tests/TeyPdfCad.Tests/DiagnosticDeterminismTests.cs`
- Modify: diagnostic models/builders only if tests expose instability.

**Interfaces:**
- Same seed/count produces byte-stable diagnostic content after excluding runtime performance timing fields.

- [ ] Write a deterministic 100-case classification comparison test.
- [ ] Add tests that expected labels and corpus case counts remain unchanged by diagnostics.
- [ ] Add tolerance anti-softening boundary tests at 1:1 and 1:500.
- [ ] Run tests RED/GREEN as appropriate.
- [ ] Commit as `test: harden TEST-003 calibration invariants`.

### Task 7: GitHub Actions 100/1,000/10,000 and artifact publication

**Files:**
- Modify: `.github/workflows/test-generator.yml`
- Modify: `docs/TESTING.md`

**Interfaces:**
- CI executes `diagnose-core` for 100, 1,000 and 10,000 cases with seed 12345.
- Artifact includes each run's `report.json`, `report.md`, and `worst_cases.json`.

- [ ] Add workflow steps and always-upload diagnostics.
- [ ] Document tolerance derivation, taxonomy and semantic-vs-geometry metrics.
- [ ] Push and inspect actual 100/1k/10k outputs.
- [ ] Confirm deterministic rerun fingerprints/diagnostic content.
- [ ] Commit as `ci: run TEST-003 error classification benchmarks`.

### Task 8: Final analysis, review and PR

**Files:**
- No production files.
- PR body summarises final results and CORE-002 handoff.

**Interfaces:**
- New PR from `feature/test-003-error-classification` to `main`, left unmerged.

- [ ] Run full Core CI and test-generator CI on final head.
- [ ] Parse final 10,000 report and identify 20 worst real defects plus top 10 for user summary.
- [ ] Verify no changes under AutoCAD bridge or production recognizer paths.
- [ ] Request code review / inspect diff for scope violations.
- [ ] Open separate PR and report PR number, head SHA, metrics and top 10 defects.
