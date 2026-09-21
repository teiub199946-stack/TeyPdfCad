# P0 Safe Source Replacement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make source suppression depend on verified native DWG entities in an on-disk probe file, then publish only a structurally sane final DWG.

**Architecture:** Core owns neutral replacement, verification, residual, and suppression-decision contracts. The DWG project writes candidate metadata and a declaration manifest, then reads the closed probe/final files to verify the declaration and count structural output. The CLI coordinates two document-wide writes: probe with all sources, gate from read-back, final with only authorized source removal.

**Tech Stack:** .NET 8, C#, ACadSharp 3.7.1, xUnit 2.9.2, GitHub Actions CI.

**Spec:** docs/superpowers/specs/2026-09-21-p0-safe-source-replacement-design.md

## Global Constraints

- Suppression is allowed only after on-disk probe-file read-back; writer counters are diagnostics only.
- Candidate metadata is exactly two flat ASCII XData strings under TEYCONVERT_CANDIDATE_V1: CandidateId, then Role.
- One entity has exactly one CandidateId and role; a candidate may own multiple entities. Never attach candidate metadata to BlockRecord.
- No spatial or bbox fallback, ExtDict fallback, Core Console path, SubEntityRef, or source XData in P0.
- HATCH: confident preserves boundary and may suppress pattern only after verification; uncertain writes no native HATCH, preserves all its sources, and emits a Medium residual.
- The document is written twice as a whole; per-page semantic failures preserve source and continue, but serialization/probe-read-back/final-sanity failures publish no DWG.
- Final sanity uses emitted-DWG-entity delta, not SourceId count. It is structural only, not a per-source final verifier.
- Immutable paint ordering must not depend on document insertion enumeration.
- FULL_PASS is P0 geometry preservation plus declared minimum checks, not WYSIWYG, recognition completeness, or broad engineering-semantic correctness.

## Review Focus

- Malformed or duplicate candidate XData must preserve all claimed source instead of being accepted by a permissive reader. Task 2.
- A multi-entity leader or level with one missing child must fail the whole candidate and preserve every eligible source. Task 2.
- A source that emits several DWG entities must contribute its true count to final sanity. Task 3.
- A final-pass bug that drops a native entity or ignores suppression must fail closed and publish no DWG. Task 4.
- Changing recognizer or source enumeration order must not change candidate identity, shared deferral, or paint order. Tasks 1 and 3.

## File structure

| File | Responsibility |
|---|---|
| src/TeyPdfCad.Core/Recognition/ReplacementVerificationContracts.cs | Immutable manifest, read-back result, suppression decision, and page status contracts. |
| src/TeyPdfCad.Core/Recognition/SourceReplacementPlanner.cs | CandidateId canonicalization, explicit source roles, deterministic shared claims, explicit HATCH classification. |
| src/TeyPdfCad.Core/Recognition/ReplacementExecutionAuditor.cs | Verification-driven residual construction; no writer-key authority. |
| src/TeyPdfCad.Core/Recognition/HatchRecognizer.cs | Explicit boundary/pattern lists and Confident/Uncertain classification. |
| src/TeyPdfCad.Dwg/CandidateMetadataCodec.cs | Strict two-string XData codec. |
| src/TeyPdfCad.Dwg/DwgReadBackVerifier.cs | Probe verification, required-property checks, and structural summaries. |
| src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs | Metadata, manifest, source-emission summary, and immutable paint keys. |
| src/TeyPdfCad.Dwg/PaintOrderEngine.cs | Ordering only by immutable identity fields. |
| src/TeyPdfCad.Cli/ConversionPipeline.cs | Probe → verify → gate → final → final-sanity sequence. |
| tests/TeyPdfCad.Tests/Recognition/SourceReplacementPlannerTests.cs | Planner, deterministic identity, HATCH, gate tests. |
| tests/TeyPdfCad.Dwg.Tests/CandidateMetadataCodecTests.cs | XData schema tests. |
| tests/TeyPdfCad.Dwg.Tests/DwgReadBackVerifierTests.cs | Triple/property/child/boundary verification tests. |
| tests/TeyPdfCad.Dwg.Tests/DwgTwoPassWriterTests.cs | Manifest, metadata roles, source count, paint-order tests. |
| tests/TeyPdfCad.Cli.Tests/ConversionPipelineTests.cs | Publish/fail-closed and page-status tests. |

---

### Task 1: Establish Core contracts, deterministic IDs, and explicit claims

**Files:**
- Create: src/TeyPdfCad.Core/Recognition/ReplacementVerificationContracts.cs
- Modify: src/TeyPdfCad.Core/Recognition/SourceReplacementPlanner.cs
- Modify: src/TeyPdfCad.Core/Recognition/ReplacementExecutionAuditor.cs
- Modify: src/TeyPdfCad.Core/Recognition/HatchRecognizer.cs
- Create: tests/TeyPdfCad.Tests/Recognition/SourceReplacementPlannerTests.cs

**Interfaces:**

Produces the neutral contracts consumed by DWG and CLI:

~~~
public sealed record ExpectedNativeEntity(
    string CandidateId, string Role, string EntityKind,
    string GeometryFingerprint,
    IReadOnlyDictionary<string, string> RequiredProperties);

public sealed record ExpectedCandidate(
    string CandidateId, string SemanticType,
    IReadOnlyList<ExpectedNativeEntity> Entities);

public sealed record DwgWriteManifest(
    IReadOnlyDictionary<string, ExpectedCandidate> Candidates);

public sealed record CandidateVerification(
    string CandidateId, bool IsVerified,
    IReadOnlyList<string> MissingRoles,
    IReadOnlyList<string> DuplicateRoles,
    IReadOnlyList<string> InvalidEntities);

public sealed record DwgReadBackVerification(
    IReadOnlyDictionary<string, CandidateVerification> Candidates);

public sealed record SuppressionDecision(
    IReadOnlySet<string> SuppressSourceIds,
    IReadOnlySet<string> PreserveSourceIds,
    IReadOnlyList<ReplacementResidual> Residuals);

public sealed class SuppressionGate
{
    public SuppressionDecision Evaluate(
        SourceReplacementPlan plan,
        DwgReadBackVerification verification);
}
~~~

- [ ] **Step 1: Write failing planner and gate tests**

Add tests:

~~~
[Fact]
public void Canonical_candidate_id_is_stable_when_provenance_order_changes()
{
    var first = SourceReplacementPlanner.CreateCandidateId(
        29, "LEADER", ["s-2", "s-1"], "normalized-geometry");
    var second = SourceReplacementPlanner.CreateCandidateId(
        29, "LEADER", ["s-1", "s-2"], "normalized-geometry");

    Assert.Equal(first, second);
    Assert.StartsWith("v1:29:LEADER:", first);
}

[Fact]
public void Gate_preserves_every_claim_of_an_unverified_multi_source_candidate()
{
    var plan = PlanWithEligibleSources("candidate-1", ["s-1", "s-2"]);
    var result = new DwgReadBackVerification(new Dictionary<string, CandidateVerification>
    {
        ["candidate-1"] = new("candidate-1", false, ["annotation"], [], ["missing Text"])
    });

    var decision = new SuppressionGate().Evaluate(plan, result);

    Assert.Empty(decision.SuppressSourceIds);
    Assert.Equal(["s-1", "s-2"], decision.PreserveSourceIds.Order());
    Assert.Contains(decision.Residuals, x => x.Kind == ReplacementResidualKind.GeometryLost);
}
~~~

Also add chain and cycle cases with shuffled recognizer order, checking identical sorted deferred candidates and preservation of every claimed source. Add uncertain-HATCH test: no native candidate is eligible, all HATCH sources preserved, one DeferredUncertainHatch Medium residual.

- [ ] **Step 2: Run the focused Core tests and verify they fail**

Run: dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter "FullyQualifiedName~SourceReplacementPlannerTests"

Expected: FAIL because the contracts, canonical ID, explicit hatch roles, and SuppressionGate do not exist.

- [ ] **Step 3: Implement contracts and planner changes**

Add the contract file above. Rename provisional SourceReplacementPlan.SuppressedSourceIds to EligibleSourceIds, updating all callers in this task. Only SuppressionGate may produce actual suppression. A source may be authorized only if it has one valid suppressible claim, that candidate is verified, and its role is neither HatchBoundary nor EvidenceOnly.

For every unverified candidate, preserve all sources claimed by it and append a Critical GeometryLost residual per affected source. No branch may inspect createdCandidateKeys.

Add:

~~~
public enum HatchClassification { Confident, Uncertain }

public sealed record HatchClaim(
    string CandidateId,
    IReadOnlyList<string> BoundarySourceIds,
    IReadOnlyList<string> PatternSourceIds,
    HatchClassification Classification);
~~~

Refactor HatchRecognizer and SourceReplacementPlanner.AddHatchClaims so lists are explicit; delete the provenance[0] convention. Uncertain produces no native candidate and a Medium DeferredUncertainHatch residual.

Implement CreateCandidateId(page, semanticType, sourceIds, normalizedGeometryFingerprint): sort ordinally; concatenate version/page/type/sources/fingerprint with unambiguous separators; hash SHA-256; return v1:page:type:first-16-lowercase-hex. Reject blank IDs and page-local collisions. Report recognizer version separately; it is not hash input.

Replace ReplacementExecutionAuditor.Build(createdCandidateKeys, ...) with verification-driven residual conversion.

- [ ] **Step 4: Run focused and full Core tests**

Run:

~~~bash
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj --filter "FullyQualifiedName~SourceReplacementPlannerTests"
dotnet test tests/TeyPdfCad.Tests/TeyPdfCad.Tests.csproj
~~~

Expected: PASS. Existing provisional-plan tests must assert EligibleSourceIds; executed suppression assertions belong in gate tests.

- [ ] **Step 5: Commit**

~~~bash
git add src/TeyPdfCad.Core/Recognition tests/TeyPdfCad.Tests/Recognition
git commit -m "feat: add verification-driven replacement contracts"
~~~

### Task 2: Build strict metadata codec and on-disk read-back verifier

**Files:**
- Create: src/TeyPdfCad.Dwg/CandidateMetadataCodec.cs
- Create: src/TeyPdfCad.Dwg/DwgReadBackVerifier.cs
- Create: tests/TeyPdfCad.Dwg.Tests/CandidateMetadataCodecTests.cs
- Create: tests/TeyPdfCad.Dwg.Tests/DwgReadBackVerifierTests.cs
- Modify: tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj

**Interfaces:**

~~~
public readonly record struct CandidateEntityMetadata(
    string CandidateId, string Role);

public static class CandidateMetadataCodec
{
    public const string AppId = "TEYCONVERT_CANDIDATE_V1";
    public static void Write(Entity entity, CandidateEntityMetadata metadata);
    public static bool TryRead(Entity entity, out CandidateEntityMetadata metadata);
}

public sealed record DwgStructuralSummary(
    int CountedEntityCount,
    int CandidateMetadataEntityCount);

public sealed class DwgReadBackVerifier
{
    public DwgReadBackVerification Verify(string dwgPath, DwgWriteManifest manifest);
    public DwgStructuralSummary ReadStructuralSummary(string dwgPath);
}
~~~

- [ ] **Step 1: Write failing codec and verifier tests**

Codec tests must cover exact two-string Line round trip; write rejection for blank candidate/role; TryRead false for unknown app, one string, three strings, non-string record, empty field; ordinal/case-sensitive CandidateId.

Verifier fixtures must cover missing Leader annotation, wrong entity type with right role, duplicate primary role, a Level Attribute not owned by expected Insert, and each required-property failure: zero Dimension measurement, one Leader vertex, empty/zero-height Text, zero-scale Axis, empty Level value, Hatch with zero area or wrong boundary fingerprint.

- [ ] **Step 2: Run focused tests and verify they fail**

Run: dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "FullyQualifiedName~CandidateMetadataCodecTests|FullyQualifiedName~DwgReadBackVerifierTests"

Expected: FAIL because codec and verifier types do not exist.

- [ ] **Step 3: Implement codec, verifier, and counters**

CandidateMetadataCodec.Write rejects null entity, blank fields, and entity already carrying candidate metadata; it writes exactly two ExtendedDataString records. TryRead accepts exactly two nonblank strings, no control or other record.

DwgReadBackVerifier.Verify calls DwgReader.Read(path), enumerates ModelSpace top-level entities and Insert.Attributes, compares exact triple CandidateId/Role/EntityKind before fingerprint and properties, and requires exactly one entity per expected role/type. It rejects duplicates, unexpected metadata for a CandidateId, invalid metadata, and BlockRecord metadata.

Implement required-property evaluators:
- dimension.expectedMeasurement from dimension geometry within tolerance;
- leader.minimumVertices;
- text.minimumHeight and text.nonEmpty;
- axis.blockName, axis.minimumScale, axis.minimumLength;
- level.attributeTag and level.nonEmptyValue plus parent ownership;
- hatch.minimumArea and hatch.boundaryFingerprint.

ReadStructuralSummary counts ModelSpace top-level entities plus nested Insert attributes, and separately valid metadata-bearing entities. It never counts BlockRecord definition entities.

- [ ] **Step 4: Run focused and persistence regressions**

Run:

~~~bash
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "FullyQualifiedName~CandidateMetadataCodecTests|FullyQualifiedName~DwgReadBackVerifierTests"
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "FullyQualifiedName~PersistentCandidateId"
~~~

Expected: PASS, including the committed user-saved AutoCAD 2022 fixture.

- [ ] **Step 5: Commit**

~~~bash
git add src/TeyPdfCad.Dwg tests/TeyPdfCad.Dwg.Tests
git commit -m "feat: verify candidate metadata from DWG read-back"
~~~

### Task 3: Refactor writer output into manifest, source summary, and immutable paint order

**Files:**
- Modify: src/TeyPdfCad.Dwg/AcadSharpDwgWriter.cs
- Modify: src/TeyPdfCad.Dwg/PaintOrderEngine.cs
- Modify: tests/TeyPdfCad.Dwg.Tests/DwgDocumentWriterTests.cs
- Modify: tests/TeyPdfCad.Dwg.Tests/PaintOrderEngineTests.cs
- Create: tests/TeyPdfCad.Dwg.Tests/DwgTwoPassWriterTests.cs

**Interfaces:**

~~~
public sealed record SourceEmissionSummary(
    IReadOnlyDictionary<string, int> EmittedEntityCountBySourceId)
{
    public int GetExpectedSuppressionDelta(IEnumerable<string> sourceIds);
}

public sealed record DwgWriteResult(
    byte[] Bytes,
    DwgWriteManifest Manifest,
    SourceEmissionSummary SourceEmissionSummary,
    int DiagnosticCreatedCandidateKeyCount);
~~~

- [ ] **Step 1: Write failing writer tests**

Use a fixture with Dimension, Leader+Text, Axis Insert, Level Insert+Attribute, and Hatch. Assert manifest roles/types, exact metadata on all natives, same CandidateId but differing Leader/Level roles, and no BlockRecord metadata.

Add source count test where a filled source creates a boundary plus Hatch; its SourceEmissionSummary value must be two or more. Add authorized-suppression test: a source is emitted in probe mode and omitted only when its ID is passed in final authorization.

Shuffle input entity order and expect unchanged immutable paint keys.

- [ ] **Step 2: Run focused writer tests and verify they fail**

Run: dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "FullyQualifiedName~DwgTwoPassWriterTests|FullyQualifiedName~PaintOrderEngineTests"

Expected: FAIL because writer returns bytes only, native entities lack metadata, and paint order uses insertion index.

- [ ] **Step 3: Implement writer result and deterministic emission**

Change writer API:

~~~
public DwgWriteResult Write(
    VectorPdfDocument document,
    DwgDocumentPlan plan,
    IReadOnlyDictionary<int, HatchRecognitionResult> hatches,
    IReadOnlyDictionary<int, SemanticReconstructionResult> semantics,
    TemplateLibrary? templates,
    IReadOnlyDictionary<int, TemplateSelection>? selections,
    IReadOnlyDictionary<int, SourceReplacementPlan> replacementPlans,
    IReadOnlySet<string>? authorizedSuppressedSourceIds = null);
~~~

Null authorization means probe and suppresses nothing. Non-null authorization affects source output only; native selection is identical.

Refactor WriteDimension, WriteArcDimension, WriteLeader, WriteAxis, WriteLevel, and WritePatternHatch to pass every created native through AddExpectedNative(entity, candidateId, role, entityKind, fingerprint, properties, document, manifest). That helper writes metadata, records ExpectedNativeEntity, and rejects duplicate CandidateId/Role.

Properties come from semantic candidate input before serialization. Level labels Insert primary and owned AttributeEntity attribute; never label definition. Uncertain hatch does not invoke native hatch writer.

Record every emitted DWG entity under its SourceId, including fills/boundaries. GetExpectedSuppressionDelta sums counts once per distinct ID and rejects unknown IDs.

Replace PaintOrderItem.InsertionIndex with:

~~~
public sealed record PaintOrderKey(
    int PageNumber, string SourceOrCandidateKey,
    string Role, PaintPriority Priority, int StableOrdinal);
~~~

Order by Priority, PageNumber, SourceOrCandidateKey ordinal, Role ordinal, StableOrdinal. Remove all dependence on document insertion/enumeration index.

- [ ] **Step 4: Run writer and full DWG suite**

Run:

~~~bash
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj --filter "FullyQualifiedName~DwgTwoPassWriterTests|FullyQualifiedName~DwgDocumentWriterTests|FullyQualifiedName~PaintOrderEngineTests"
dotnet test tests/TeyPdfCad.Dwg.Tests/TeyPdfCad.Dwg.Tests.csproj
~~~

Expected: PASS. Confirm source entity delta test passes before relying on final sanity.

- [ ] **Step 5: Commit**

~~~bash
git add src/TeyPdfCad.Dwg tests/TeyPdfCad.Dwg.Tests
git commit -m "feat: emit native manifest and source summaries"
~~~

### Task 4: Coordinate document-wide probe/final writing and fail closed

**Files:**
- Modify: src/TeyPdfCad.Cli/ConversionPipeline.cs
- Modify: tests/TeyPdfCad.Cli.Tests/ConversionPipelineTests.cs

**Interfaces:**

~~~
private static void EnsureFinalStructuralSanity(
    DwgStructuralSummary probe,
    DwgStructuralSummary final,
    SourceEmissionSummary sourceEmission,
    IReadOnlySet<string> authorizedSuppressedSourceIds);
~~~

- [ ] **Step 1: Write failing end-to-end tests**

Add tests named:
- Pipeline_keeps_sources_when_probe_verification_rejects_a_native_candidate
- Pipeline_publishes_final_only_when_structural_delta_matches_probe
- Pipeline_does_not_publish_dwg_when_final_native_count_changes

For final-count failure, inject an internal IDwgDocumentWriter seam whose final pass omits one native. Assert InvalidArgumentsOrIo, no output DWG, and report residual SourceSuppressionViolation.

Add two-page fixture: one page contains uncertain HATCH or failed candidate, the other ordinary source geometry. Assert output exists, unsafe page PASS_WITH_RESIDUALS, other page independently reported, whole document Partial.

- [ ] **Step 2: Run focused CLI tests and verify they fail**

Run: dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj --filter "FullyQualifiedName~Pipeline_keeps_sources|FullyQualifiedName~Pipeline_publishes_final|FullyQualifiedName~Pipeline_does_not_publish"

Expected: FAIL because current pipeline writes once, trusts createdCandidateKeysByPage, and publishes before final sanity.

- [ ] **Step 3: Implement two-pass sequence**

In ConvertAsync:
1. Build provisional replacement plans. Gate semantic recognition and replacement only at this boundary; disabled semantic means empty claims, disabled replacement means empty final authorization.
2. Write probe without authorization to unique temporary path in output directory; close it.
3. Verify probe manifest from disk; run SuppressionGate per page; union actual authorization.
4. Read probe structural summary.
5. Write final to a second unique temporary path with exact authorization.
6. Read final summary and require:

~~~
final.CandidateMetadataEntityCount == probe.CandidateMetadataEntityCount
final.CountedEntityCount ==
    probe.CountedEntityCount -
    probe.SourceEmissionSummary.GetExpectedSuppressionDelta(authorized)
~~~

7. On mismatch create Critical SourceSuppressionViolation, remove only known temp paths, write failure report, and do not publish an output DWG.
8. On success atomically move final temp to requested output and remove probe temp.

Keep writer key count as diagnostic only. Reports and GeometryLost use gate/verifier data only.

- [ ] **Step 4: Define status and run CLI regression**

Implement:
- FULL_PASS: all probe verifier and final structural checks pass; no residual, GeometryLost, or SourceSuppressionViolation.
- PASS_WITH_RESIDUALS: published output, but a page preserved source due to defer/uncertainty/verification failure.
- PARTIAL: existing source-PDF diagnostics make page partial.
- serialization, probe read-back, or final sanity failure: InvalidArgumentsOrIo and no final DWG.

Run:

~~~bash
dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj
dotnet test TeyPdfCad.sln
~~~

Expected: PASS. Update the existing pattern-HATCH test so it expects source removal only after actual authorization.

- [ ] **Step 5: Commit**

~~~bash
git add src/TeyPdfCad.Cli tests/TeyPdfCad.Cli.Tests
git commit -m "feat: gate source suppression on probe read-back"
~~~

### Task 5: Reconcile reports and regression evidence

**Files:**
- Modify: src/TeyPdfCad.Cli/ConversionPipeline.cs
- Modify: tests/TeyPdfCad.Cli.Tests/ConversionPipelineTests.cs
- Modify: tests/TeyPdfCad.Dwg.Tests/PersistentCandidateIdAutoCadRoundTripTests.cs
- Modify: docs/superpowers/specs/2026-09-21-p0-safe-source-replacement-design.md

**Interfaces:**
- Consumes completed two-pass result from Task 4.
- Produces report fields whose status and residuals match the published DWG decision.

- [ ] **Step 1: Write failing report-contract tests**

Parse JSON and assert uncertain hatch is visible:

~~~
Assert.Equal("PASS_WITH_RESIDUALS", page.GetProperty("semanticAuditStatus").GetString());
Assert.Contains(page.GetProperty("residuals").EnumerateArray(),
    item => item.GetProperty("kind").GetString() == "DeferredUncertainHatch"
         && item.GetProperty("severity").GetString() == "Medium");
~~~

Add failed-probe test asserting no FULL_PASS report. Add final-sanity mismatch test asserting Critical SourceSuppressionViolation, null dwgPath, and absent output.

- [ ] **Step 2: Run focused report tests and verify they fail**

Run: dotnet test tests/TeyPdfCad.Cli.Tests/TeyPdfCad.Cli.Tests.csproj --filter "FullyQualifiedName~Pipeline_.*report|FullyQualifiedName~Pipeline_.*sanity"

Expected: FAIL until report fields use verifier/gate outcomes.

- [ ] **Step 3: Implement report reconciliation**

Emit per-page eligible, authorized-suppressed, preserved counts; candidate verification failures; residual kind/severity; document status from Task 4; and FULL_PASS limitations. Keep AutoCAD 2022 fixture scope unchanged: it confirms only tested metadata persistence, not Core Console or transform/copy paths. Update spec only for actual identifier naming; do not expand P0.

- [ ] **Step 4: Run full verification and inspect CI**

Run:

~~~bash
dotnet test TeyPdfCad.sln
git status --short
~~~

Expected: all tests pass and only intended P0 files are changed. Push branch and require green GitHub Actions before completion claim.

- [ ] **Step 5: Commit**

~~~bash
git add src/TeyPdfCad.Cli tests/TeyPdfCad.Cli.Tests tests/TeyPdfCad.Dwg.Tests docs/superpowers/specs
git commit -m "test: cover P0 report and failure policy"
~~~

## Self-review

### Spec coverage

- Probe verification, manifest, triple comparison, sanity properties, no writer-count authority: Tasks 1–4.
- Exact XData schema, multi-entity roles, Insert vs BlockRecord, AutoCAD fixture: Tasks 2, 3, 5.
- Shared claims, deterministic identity, no spatial fallback, explicit HATCH uncertainty: Task 1.
- Whole-document two-pass, actual emitted delta, atomic publish, fail-closed: Task 4.
- Immutable paint order: Task 3.
- Per-page status and truthful reports: Tasks 4–5.
- Exclusions remain stated in Global Constraints and unchanged by all tasks.

### Placeholder scan

No TODO/TBD or generic test/handling steps remain. Each test and implementation step names assertions, interfaces, and safety behavior.

### Type consistency

DwgWriteManifest, DwgReadBackVerification, SuppressionGate, CandidateMetadataCodec, DwgStructuralSummary, SourceEmissionSummary, and DwgWriteResult are introduced before consumption. Structural count scope is consistently ModelSpace top-level entities plus nested Insert attributes.

### Review Focus coverage

All five Review Focus inputs are covered by concrete tests in Tasks 1–4.
