# P0 Safe Source Replacement — Design Specification

## Goal

Permit source PDF geometry to be suppressed only after the written DWG has been read back and has proved that every expected native entity for the replacement candidate exists with its persistent CandidateId and role.

## Scope

P0 closes the unsafe replacement path already present in the semantic pipeline. It does not broaden semantic recognition quality.

P0 includes:

- persistent CandidateId metadata written on native entities;
- a writer-produced expectation manifest and a separate read-back verifier;
- source suppression driven by verified entities, never by writer counters;
- explicit candidate source roles;
- explicit HATCH boundary/pattern claims and conservative uncertain handling;
- deterministic paint-order identity;
- regression tests for ACadSharp and AutoCAD 2022 evidence.

P0 does not include:

- spatial matching or any bounding-box fallback;
- Core Console, copy/paste, mirror, or array persistence testing;
- semantic correctness validators such as dimension measurement checks;
- wider recognizer accuracy improvements;
- final visual acceptance comparison of Aidyn.pdf page 29.

## Pipeline

```
Vector extraction → recognition → candidate claims → provisional replacement plan
      → DWG writer + expected entity manifest → DWG read-back verifier
      → verified replacement plan → final source suppression → report
```

The pipeline remains one pipeline. The only mode gates are:

- `EnableSemanticRecognition`: if false, recognition returns no candidates or claims.
- `EnableSourceReplacement`: if false, the final plan preserves every source.

No lower layer may branch on the mode.

## Persistent metadata contract

Every native entity generated for a semantic candidate receives exactly one XData group under AppId `TEYCONVERT_CANDIDATE_V1`.

The group contains exactly two flat ASCII strings, in this order:

1. `CandidateId`
2. `Role`

CandidateId is a deterministic, ASCII-safe identifier. Its canonical input is:

- schema version;
- page number;
- semantic type;
- SourceIds sorted by ordinal;
- normalized geometry fingerprint.

The recognizer version is reported separately and is not part of CandidateId. CandidateId creation must reject empty input and collisions within a page.

One candidate may own multiple entities. One entity may own one CandidateId and one role only. Writing a second CandidateId to the same entity is a validation failure before DWG serialization.

Candidate metadata belongs only to entity instances. In particular, axis and level CandidateId belong to INSERT, never BlockRecord. A level AttributeEntity uses the same CandidateId as its INSERT and role `attribute`.

## Expected entity manifest

The writer must return `DwgWriteManifest`, not `createdCandidateKeys`.

```csharp
sealed record ExpectedNativeEntity(
    string CandidateId,
    string Role,
    string EntityKind);

sealed record ExpectedCandidate(
    string CandidateId,
    string SemanticType,
    IReadOnlyList<ExpectedNativeEntity> Entities);

sealed record DwgWriteManifest(
    IReadOnlyDictionary<string, ExpectedCandidate> Candidates);
```

Expected roles:

| Semantic type | Expected native entity roles |
|---|---|
| DIMENSION / ARC_DIMENSION | `primary` |
| LEADER | `primary`, `annotation` |
| AXIS | `primary` on INSERT |
| LEVEL | `primary` on INSERT, `attribute` on AttributeEntity |
| HATCH | `primary` on HATCH |

The writer may add no source suppression evidence itself. Its manifest is an expectation only.

## Read-back verifier

The verifier reads the serialized DWG byte stream independently from the writer and builds actual `(CandidateId, Role, EntityKind)` bindings.

A candidate is verified only if:

- every expected entity role appears exactly once;
- it is on the expected DWG entity type;
- each entity has exactly one XData group with exactly two strings;
- both strings are non-empty;
- no candidate metadata is present on BlockRecord;
- an expected level attribute remains a child of its expected INSERT;
- no unexpected duplicate role exists for that CandidateId.

If any condition fails, the candidate is unverified. An unverified candidate cannot suppress any source geometry.

```csharp
sealed record CandidateVerification(
    string CandidateId,
    bool IsVerified,
    IReadOnlyList<string> MissingRoles,
    IReadOnlyList<string> DuplicateRoles,
    IReadOnlyList<string> InvalidEntities);

sealed record DwgReadBackVerification(
    IReadOnlyDictionary<string, CandidateVerification> Candidates);
```

The final replacement executor receives only verified CandidateIds. There is no spatial fallback.

## Claims and source suppression

Claims are explicit output from each recognizer. A claim contains CandidateId, SourceId, source role, and full/partial provenance.

A source is suppressed only when:

- it is claimed by exactly one candidate;
- that candidate is verified by read-back;
- its claim is full, valid, and suppressible;
- it has no `HatchBoundary` or `EvidenceOnly` role;
- its candidate is not deferred.

The existing single-pass shared-claim policy remains:

- build the complete source-to-candidate claim graph;
- for every SourceId claimed by two or more valid candidates, defer all involved candidates;
- do not recompute or promote candidates after deferral;
- defer preserves all sources for every deferred candidate;
- emit `DeferredShared` residuals at High severity.

Any residual means `PASS_WITH_RESIDUALS`, never `FULL_PASS`.

A candidate that fails read-back produces a `GeometryLost` residual at Critical severity. The page is written with its source preserved.

## HATCH

The previous convention “first ProvenanceId is the boundary” is removed.

```csharp
sealed record HatchClaim(
    string CandidateId,
    IReadOnlyList<string> BoundarySourceIds,
    IReadOnlyList<string> PatternSourceIds,
    FillRule FillRule,
    HatchClassification Classification);

enum HatchClassification { Confident, Uncertain }
```

- `Confident`: write native HATCH; preserve BoundarySourceIds; PatternSourceIds can be suppressed only after HATCH read-back is verified.
- `Uncertain`: do not write native HATCH; preserve all its sources; emit `DeferredUncertainHatch` at Medium severity.

## Deterministic paint order

Paint order must use immutable ordering identity, not document insertion enumeration. The identity is derived from page number, source/candidate key, entity role, priority, and stable entity ordinal inside the candidate.

The ordering remains bottom-to-top: base geometry, solid fill, hatch pattern, preserved boundary, dimensions/annotations/text, review overlay. Equal keys are ordered lexically by immutable identity.

## Failure policy

- Semantic / verification failure on one page: write the page with source preserved for unsafe candidates, continue the document, report the page as `PASS_WITH_RESIDUALS`; document status is the worst page status.
- DWG serialization or document read-back failure: fail fast and emit no output DWG.

## Required tests

1. Candidate metadata codec: valid flat schema; empty/one/three fields; unknown app; duplicate candidate metadata; case-sensitive CandidateId.
2. Read-back verifier: expected primary, Leader + annotation, Level INSERT + Attribute, wrong entity kind, missing role, duplicate role, metadata on BlockRecord.
3. Planner/executor: a verified candidate suppresses only allowed sources; any unverified candidate preserves every source; no spatial fallback.
4. Shared claims: chain and cycle produce the same deferred set independent of recognizer ordering.
5. HATCH: explicit confident boundary/pattern roles; uncertain HATCH emits no native HATCH and preserves all source.
6. Paint-order: identical candidate/source set yields identical immutable order after input enumeration changes.
7. Regression: ACadSharp round-trip and committed user AutoCAD 2022 save/reopen fixture.

## Acceptance

P0 is complete only when CI demonstrates that source suppression depends on read-back verification rather than writer counters, and all required tests pass. It is not a claim of semantic accuracy or visual WYSIWYG acceptance.
