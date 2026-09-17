# TEST-002 Core Regression Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect TEST-001 to the real `SemanticReconstructionEngine` and produce the first honest 100 / 1,000 / 10,000-case Core accuracy baseline.

**Architecture:** Keep `TeyPdfCad.Core` unchanged. Add a test-side scene builder that converts `DimensionCase` DWG-space expectations into paper/PDFIMPORT-like `PrimitiveScene` geometry, and a `SemanticCoreTestPipeline` that runs `SemanticReconstructionEngine` and maps its paper-space output back to real drawing coordinates for `RegressionRunner`. Add a CLI command and CI workflow that run the real Core path and upload reports.

**Tech Stack:** .NET 8, xUnit, GitHub Actions, existing `TeyPdfCad.Core` and `TeyPdfCad.TestGenerator`.

**Spec:** User-provided TEST-002 requirements in the project conversation.

## Global Constraints

- Work only on `feature/core-regression-integration`.
- Do not modify `feature/autocad-bridge` or PR #6.
- Do not change the production recognizer to improve TEST-002 numbers.
- Positive cases must emit dimension line, extension lines, numeric text, arrow/tick evidence, broken line when requested, and controlled noise.
- Negative cases must emit the requested `NegativePattern`, not a valid dimension with a negative label.
- Chains must emit every segment.
- DWG-space coordinates must be divided by drawing scale before entering Core; Core candidate geometry must be multiplied by reconstructed scale before comparison.
- Aligned/Rotated ambiguity that cannot be observed from primitives must be recorded separately rather than artificially treated as recognizer failure.
- Preserve honest failures and create issues for recognizer defects instead of weakening expected values or thresholds.

---

### Task 1: Contract tests for the real Core pipeline

**Files:**
- Create: `tests/TeyPdfCad.Tests/CoreRegressionPipelineTests.cs`

**Interfaces:**
- Consumes: `DimensionCaseGenerator`, `ISemanticTestPipeline`.
- Produces: behavioral contract for `SemanticCoreTestPipeline.RunAsync(DimensionCase)`.

- [ ] Add a test proving a clean 5200 @ 1:100 case is recognized by real Core with value, count and scale mapped back correctly.
- [ ] Add a test proving paper-space scene construction divides real geometry/text height by drawing scale.
- [ ] Add a test proving `TextNearOrdinaryLine` produces no recognized dimension.
- [ ] Run tests and confirm RED because `SemanticCoreTestPipeline` / scene builder do not exist.

### Task 2: DimensionCase -> PrimitiveScene builder and Core adapter

**Files:**
- Create: `tests/TeyPdfCad.TestGenerator/Pipelines/DimensionCasePrimitiveSceneBuilder.cs`
- Create: `tests/TeyPdfCad.TestGenerator/Pipelines/SemanticCoreTestPipeline.cs`
- Modify: `tests/TeyPdfCad.TestGenerator/TeyPdfCad.TestGenerator.csproj`

**Interfaces:**
- `DimensionCasePrimitiveSceneBuilder.Build(DimensionCase) -> PrimitiveScene`
- `SemanticCoreTestPipeline.RunAsync(DimensionCase, CancellationToken) -> ActualDimensionResult`

- [ ] Add the Core project reference to the test-generator project.
- [ ] Build positive primitives in paper coordinates, including extension lines, numeric text, arrow/tick evidence and broken dimension lines.
- [ ] Apply observed/noisy geometry in paper coordinates without altering expected values.
- [ ] Build every negative pattern as a dedicated false-lookalike scene.
- [ ] Build every chain segment as its own dimension evidence.
- [ ] Run `SemanticReconstructionEngine.Analyze` without expected-result shortcuts.
- [ ] Map candidate points to drawing coordinates using the candidate reconstructed scale; map confidence to existing confidence classes.
- [ ] For multi-candidate cases, report detected count and select a deterministic representative; chains compare count while retaining aggregate diagnostics.
- [ ] Record aligned/rotated primitive ambiguity in diagnostics and normalize only the objectively indistinguishable type comparison path.
- [ ] Run tests and confirm GREEN.

### Task 3: CLI real-Core regression command

**Files:**
- Modify: `tests/TeyPdfCad.TestGenerator/Program.cs`
- Modify: `docs/TESTING.md`

**Interfaces:**
- Produces command: `core-check --count N --seed N --output DIR [--baseline FILE]`.

- [ ] Add CLI coverage for the new command surface.
- [ ] Generate corpus, run `SemanticCoreTestPipeline`, write `cases.json`, `report.json`, `report.md`.
- [ ] Print Precision / Recall / F1 / FP / FN / Wrong Measurement.
- [ ] Preserve release-gate status; do not substitute echo-pipeline results.

### Task 4: CI artifacts and honest baseline

**Files:**
- Create: `.github/workflows/core-regression.yml`

**Interfaces:**
- Runs real Core regression at 100 / 1,000 / 10,000 cases.
- Uploads all three `report.json` / `report.md` artifacts.

- [ ] Restore/build/test the solution.
- [ ] Run real Core command for 100, 1,000 and 10,000 cases.
- [ ] Upload reports with `if: always()` so low accuracy still yields evidence.
- [ ] Fail CI on execution/build errors, but allow the initial metric gate result to be captured as baseline evidence rather than hidden.

### Task 5: Verification, weaknesses and PR

**Files:**
- No recognizer modifications.
- Create GitHub issues only for concrete recognizer defects confirmed by report categories.

- [ ] Run full build and tests.
- [ ] Run 100 / 1,000 / 10,000 real Core regressions.
- [ ] Read the 10,000-case report and record Precision, Recall, F1, FP, FN, Wrong Measurement and worst angle/scale/noise/length buckets.
- [ ] Open separate issues for confirmed Core defects, without modifying the recognizer.
- [ ] Create PR from `feature/core-regression-integration` to `main` and do not merge it.
