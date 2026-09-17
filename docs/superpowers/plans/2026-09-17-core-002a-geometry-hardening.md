# CORE-002A Geometry Reconstruction Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Identify and correct proven production Semantic Core geometry-reconstruction defects exposed by TEST-003 while preserving clean-case accuracy, detection metrics, expected labels, corpus, tolerance, release thresholds, and AutoCAD integration.

**Architecture:** Use deterministic TEST-003 seed `12345` cases as the source of repros. First prove whether each mismatch is in production Core or in diagnostic adapter/trace. Only after a production defect is demonstrated, add a focused RED test against `TeyPdfCad.Core`, then make the smallest Core change and verify the full safety gates through `diagnose-core` 100 → 1,000 → 10,000.

**Tech Stack:** .NET 8, C#, xUnit, TeyPdfCad.Core, TeyPdfCad.TestGenerator, GitHub Actions.

**Spec:** User CORE-002A requirements in the 2026-09-17 project conversation.

## Global Constraints

- Base: `feature/test-003-error-classification` at `8b76a50b54fb1788ba1658ef0cefc19090b17ddc`.
- Branch: `feature/core-002a-geometry-hardening`.
- Draft PR stacked on PR #12; do not merge.
- Do not touch PR #6 / `feature/autocad-bridge`, PR #13/#14, AutoCAD adapter, expected labels, TEST-003 tolerance, generator corpus, or release thresholds.
- TDD required for production changes.
- Safety gates after every production block: clean geometry must remain 100%; Precision >= 94.9795%; Recall >= 85.9306%; FP <= 318; FN <= 985; Correct Value must not regress.
- Do not increase tolerance to improve results.
- Aligned/Rotated visual ambiguity is not a geometry defect.
- If TEST-003 adapter/trace is responsible for a significant portion of `WrongGeometry`, stop production fixes for that class, preserve the evidence, and report a separate minimal reproduction before any diagnostic correction.

---

### Task 1: Deterministic root-cause reproduction matrix

**Files:**
- Create: `tests/TeyPdfCad.Tests/Core002AGeometryReproTests.cs`
- Create: `docs/CORE-002A_ROOT_CAUSE.md`

**Interfaces:**
- Consumes: `DimensionCaseGenerator`, `DimensionCasePrimitiveSceneBuilder`, `SemanticCoreTestPipeline.RunDetailedAsync`, `ErrorClassifier`.
- Produces: named repro tests and a root-cause table for endpoint mismatch, micro-break, coordinate jitter, angular skew, outside-text, the required combinations, and scales 1/1, 1/50, 1/100, 1/500.

- [ ] **Step 1:** Freeze TEST-003 case IDs/inputs for each required cohort using seed `12345` and assert case labels/corpus are not modified.
- [ ] **Step 2:** Add assertions exposing `DefinitionPoint1`, `DefinitionPoint2`, `DimensionLinePoint`, selected dimension-line provenance, selected extension-line provenance, and scale.
- [ ] **Step 3:** Run targeted tests and preserve genuine production RED failures separately from trace/adapter mismatches.
- [ ] **Step 4:** Quantify how many of 3,454 `WrongGeometry` cases are explained by each root cause.
- [ ] **Step 5:** Commit the repro suite/root-cause map without changing production Core.

### Task 2: Block A — endpoint reconstruction

**Files:**
- Modify only if production defect is proven: `src/TeyPdfCad.Core/Recognition/DimensionGeometryAnalysis.cs`
- Test: `tests/TeyPdfCad.Tests/Core002AEndpointReconstructionTests.cs`

**Interfaces:**
- Consumes: selected extension-line primitives plus selected dimension line.
- Produces: stable definition points with provenance unchanged.

- [ ] **Step 1:** Add the smallest endpoint RED test from Task 1.
- [ ] **Step 2:** Verify RED on unchanged production Core.
- [ ] **Step 3:** Implement only the proven endpoint reconstruction fix; no threshold changes.
- [ ] **Step 4:** Targeted GREEN, full Core suite, then `diagnose-core` 100 → 1,000 → 10,000 if gates remain green.
- [ ] **Step 5:** Record before → after metrics and commit.

### Task 3: Block B — dimension-line placement

**Files:**
- Modify only if production defect is proven: `src/TeyPdfCad.Core/Recognition/LinearDimensionRecognizer.cs` and/or `src/TeyPdfCad.Core/Recognition/DimensionGeometryAnalysis.cs`
- Test: `tests/TeyPdfCad.Tests/Core002ADimensionLinePlacementTests.cs`

**Interfaces:**
- Consumes: the selected dimension-line primitive(s).
- Produces: a semantic `DimensionLinePoint` whose meaningful perpendicular offset matches the supplied evidence; do not treat source-type ambiguity as geometry error.

- [ ] **Step 1:** Prove whether current midpoint reconstruction is incorrect or TEST-003 trace compares the wrong geometric invariant.
- [ ] **Step 2:** If production is wrong, add RED and make the smallest production fix. If trace is wrong, stop this block and document the minimal trace reproduction instead of changing Core.
- [ ] **Step 3:** Run targeted/full tests and 100 → 1,000 → 10,000 only after a production fix.
- [ ] **Step 4:** Record before → after metrics and commit.

### Task 4: Block C — broken/micro-break handling

**Files:**
- Modify only if production defect is proven: `src/TeyPdfCad.Core/Recognition/DimensionGeometryAnalysis.cs`
- Test: `tests/TeyPdfCad.Tests/Core002ABrokenLineTests.cs`

**Interfaces:**
- Consumes: collinear/near-collinear dimension-line fragments around text.
- Produces: deterministic merged evidence and complete provenance without changing global thresholds.

- [ ] **Step 1:** Add minimal micro-break RED repro, including jitter+break.
- [ ] **Step 2:** Verify selected fragments/provenance and locate the rejection or geometry error cause.
- [ ] **Step 3:** Implement the smallest merge/selection correction if production defect is proven.
- [ ] **Step 4:** Targeted GREEN, full tests, then diagnostic gates 100 → 1,000 → 10,000.
- [ ] **Step 5:** Record metrics and commit.

### Task 5: Block D — outside-text robustness

**Files:**
- Modify only if production defect is proven: `src/TeyPdfCad.Core/Recognition/LinearDimensionRecognizer.cs` and/or `src/TeyPdfCad.Core/Recognition/DimensionGeometryAnalysis.cs`
- Test: `tests/TeyPdfCad.Tests/Core002AOutsideTextTests.cs`

**Interfaces:**
- Consumes: text placement relative to dimension-line evidence.
- Produces: recognition/geometry selection that remains evidence-driven without weakening false-positive protection globally.

- [ ] **Step 1:** Add minimal outside-text and break+outside-text RED repros.
- [ ] **Step 2:** Identify whether failure is candidate enumeration, text-distance/projection rejection, or wrong-line selection.
- [ ] **Step 3:** Make a local evidence-based fix only if production defect is proven.
- [ ] **Step 4:** Targeted GREEN, full tests, then 100 → 1,000 → 10,000 safety gates.
- [ ] **Step 5:** Record metrics and commit.

### Task 6: Final audit / Draft PR report

**Files:**
- Update: `docs/CORE-002A_ROOT_CAUSE.md`
- Update PR body only; do not merge.

- [ ] **Step 1:** Fresh full Core tests and fresh TEST-003 10,000 diagnostic run.
- [ ] **Step 2:** Report Correct Geometry, noisy/broken/outside geometry, Precision/Recall/F1, FP/FN, WrongScale, signed dx/dy, P95/P99 paper error before → after.
- [ ] **Step 3:** Count how many of the original 3,454 `WrongGeometry` cases were eliminated and list top-10 remaining geometry defects.
- [ ] **Step 4:** Diff-audit that forbidden areas, expected labels, corpus, tolerance, and thresholds are unchanged.
- [ ] **Step 5:** Leave PR Draft and unmerged, with final commit SHA and evidence links.
