# P0 Safe Source Replacement — Design Specification

## Goal

Permit source PDF geometry to be suppressed only after a temporary written DWG has been read back and has proved that every expected native entity for a replacement candidate exists with its CandidateId, role, type, geometry fingerprint, and required properties.

## Scope

P0 closes the unsafe source-replacement path; it does not improve recognition quality.

Included:

- persistent CandidateId XData on native entities;
- writer expectation manifest and independent read-back verifier;
- two-pass document writing: probe without suppression, then final with verified suppression;
- explicit claims and HATCH boundary/pattern classification;
- basic emitted-entity sanity checks needed to prevent false FULL_PASS;
- immutable deterministic paint-order key;
- ACadSharp and user-saved AutoCAD 2022 regression evidence.

Excluded:

- spatial or bbox fallback;
- Core Console, copy/paste, mirror, and array persistence paths;
- ExtDict fallback, unless a later regression invalidates confirmed XData;
- SubEntityRef;
- broader recognizer accuracy and visual acceptance work;
- broad review-overlay paint-order policy.

## One pipeline, two gates

The only mode gates are EnableSemanticRecognition and EnableSourceReplacement.

With recognition disabled, the candidate and claim sets are empty. With source replacement disabled, every source is preserved. No downstream stage may branch on either gate.

## Two-pass document write

The unit is the complete DWG document, never one DWG per page.

1. Probe pass: write all source entities and all eligible native candidates to a temporary probe DWG; close the file.
2. Verify: reopen the probe file from disk through DwgReadBackVerifier and evaluate its manifest.
3. Final pass: write the same native candidates and suppress only SourceIds authorized by SuppressionGate; close the final DWG.
4. Final structural sanity: reopen the final file and build an output-fingerprint multiset for ModelSpace top-level entities plus nested INSERT attributes. An output fingerprint contains entity kind, canonical geometry, relevant visual/style properties, and CandidateId/Role when valid candidate metadata exists. The final native metadata-entity count must equal the verified probe count. Its fingerprint multiset must equal the on-disk probe-file fingerprint multiset minus the verified probe source-emission fingerprint multisets for authorized suppressed SourceIds. SourceEmissionSummary records, per SourceId, a multiset of the source DWG entities it emitted; it is not assumed to be one entity per SourceId. Before subtraction, the verifier requires every requested source-emission multiset to be a sub-multiset of the actual probe-file inventory.
5. Publish: atomically move the final DWG only after final structural sanity succeeds.

This is output-integrity verification, not spatial matching and not a source-suppression authorization path. P0 does not add source XData, so it cannot distinguish a swap of two geometrically and stylistically identical source emissions; that exact per-SourceId proof is deferred. It does detect wrong-source removal whenever the emitted fingerprints differ, a lost/replaced native entity, and an aggregate suppression mismatch. A final structural mismatch emits SourceSuppressionViolation at Critical severity and fails the complete document without publishing a final DWG.

If a candidate fails verification, it has no suppression authorization and every source it claims is written in the final DWG. A probe serialization or probe read-back failure fails the complete document and produces no final DWG.

## Persistent metadata

Every native entity generated for a semantic candidate has exactly one XData group under AppId TEYCONVERT_CANDIDATE_V1. The group contains exactly two flat ASCII strings, in this order:

1. CandidateId
2. Role

CandidateId is deterministic from schema version, page number, semantic type, SourceIds sorted by ordinal, and normalized geometry fingerprint. Recognizer version is reported separately, not included in CandidateId. Empty IDs and page-local collisions are rejected before writing.

One candidate may own multiple entities. One entity has one CandidateId and one role only. INSERT carries its CandidateId on the instance, never BlockRecord. A level AttributeEntity carries the same CandidateId as its parent INSERT and role attribute.

## Writer manifest

The writer returns an expectation, not evidence.

~~~
sealed record ExpectedNativeEntity(
    string CandidateId,
    string Role,
    string EntityKind,
    string GeometryFingerprint,
    IReadOnlyDictionary<string, string> RequiredProperties);

sealed record ExpectedCandidate(
    string CandidateId,
    string SemanticType,
    IReadOnlyList<ExpectedNativeEntity> Entities);

sealed record DwgWriteManifest(
    IReadOnlyDictionary<string, ExpectedCandidate> Candidates);
~~~

A writer-created-key counter may remain as diagnostics, but no branch may use it to decide source suppression.

Expected roles:

| Semantic type | Required native roles |
|---|---|
| DIMENSION / ARC_DIMENSION | primary: Dimension |
| LEADER | primary: Leader; annotation: Text |
| AXIS | primary: Insert |
| LEVEL | primary: Insert; attribute: AttributeEntity |
| HATCH | primary: Hatch |

## Read-back verifier

DwgReadBackVerifier closes and reopens the on-disk probe file, independently from the writer. It compares the triple CandidateId, Role, EntityKind, then fingerprint and required properties. Fingerprints verify entities already identified by XData; they are not spatial matching.

A candidate is verified only when:

- every expected role/type appears exactly once;
- every matched entity has exactly one valid two-string XData group;
- CandidateId and Role are non-empty;
- every fingerprint and required property passes its declared tolerance;
- a level attribute is still a child of its expected INSERT;
- BlockRecord has no candidate metadata;
- no duplicate or unexpected entity metadata exists for that CandidateId.

~~~
sealed record CandidateVerification(
    string CandidateId,
    bool IsVerified,
    IReadOnlyList<string> MissingRoles,
    IReadOnlyList<string> DuplicateRoles,
    IReadOnlyList<string> InvalidEntities);

sealed record DwgReadBackVerification(
    IReadOnlyDictionary<string, CandidateVerification> Candidates);
~~~

The required emitted-entity checks are:

| Type | Required checks |
|---|---|
| Dimension | endpoints define nonzero expected measurement within tolerance |
| Leader | at least two vertices |
| Leader text | non-empty value and positive height |
| Axis Insert | expected BlockRecord name, positive scale and length |
| Level | expected INSERT and non-empty Attribute value |
| Hatch | nonzero boundary area and Hatch path geometry matching expected boundary fingerprint |

## Suppression gate and claims

SuppressionGate is the only component allowed to grant suppression. It receives the provisional replacement plan and DwgReadBackVerification.

A source is suppressed only if it has one full valid suppressible claim, its candidate is verified, and it is neither HatchBoundary nor EvidenceOnly. There is no spatial fallback.

The shared-claim policy remains one-pass over the full graph:

- any SourceId with two or more valid CandidateIds defers all involved candidates;
- there is no recomputation or promotion after deferral;
- every source used by a deferred candidate is preserved;
- each deferred case reports DeferredShared at High severity.

A failed verification produces GeometryLost at Critical severity. Any residual produces PASS_WITH_RESIDUALS, not FULL_PASS.

FULL_PASS means every P0 probe-verification and final-structural-sanity check passed, and no residual, GeometryLost, or SourceSuppressionViolation was recorded. It guarantees no detected loss of source geometry and the declared minimum emitted-entity checks. It does not guarantee recognition completeness, visual WYSIWYG fidelity, or engineering-semantic correctness beyond those declared checks.

## HATCH

The first-ProvenanceId-as-boundary rule is removed.

~~~
sealed record HatchClaim(
    string CandidateId,
    IReadOnlyList<string> BoundarySourceIds,
    IReadOnlyList<string> PatternSourceIds,
    FillRule FillRule,
    HatchClassification Classification);

enum HatchClassification { Confident, Uncertain }
~~~

Confident writes native HATCH, preserves BoundarySourceIds, and can suppress PatternSourceIds only after verified HATCH path/boundary binding. Uncertain writes no native HATCH, preserves all HATCH sources, and emits DeferredUncertainHatch at Medium severity.

## Paint order

P0 uses immutable identity, not document insertion enumeration. The bottom-to-top sort key is paint priority, then page number, source/candidate key, role, and stable ordinal inside a candidate. Its priority policy is base geometry, solid fill, hatch pattern, preserved boundary, dimension/annotation/text, review overlay.

## Failure policy

A semantic or verification failure on a page preserves its unsafe sources and continues document processing. Overall document status is the worst page status. A serialization, probe read-back, or final structural-sanity failure aborts the complete document and produces no final DWG.

## Required tests

1. Metadata codec: valid flat schema; empty, one, three fields; unknown app; duplicate metadata; case-sensitive CandidateId.
2. Verifier: missing role; duplicate role; wrong entity type; wrong fingerprint; unexpected metadata; BlockRecord metadata; Level parent/child preservation.
3. Emitted-entity sanity: dimension geometry-derived measurement; leader vertices; text height/value; level Attribute value; axis scale/length; HATCH area/boundary binding.
4. Two-pass executor: verified candidate suppresses allowed sources; unverified candidate preserves all sources; probe failure emits no final DWG; final native-count or expected emitted-entity-delta mismatch emits SourceSuppressionViolation and publishes no final DWG; no spatial fallback.
5. Shared claims: chain and cycle yield identical deferred candidates despite recognizer enumeration order.
6. HATCH: confident explicit roles; uncertain emits no native HATCH and preserves all sources.
7. Paint order: input enumeration changes do not change immutable ordering.
8. Regression: ACadSharp round-trip and committed user AutoCAD 2022 save/reopen fixture.

## Acceptance

P0 is complete only when CI proves source suppression comes from on-disk read-back verification, final structural sanity uses a verified output-fingerprint multiset and source-emission inventory rather than SourceId count, no writer counter grants suppression, and every required test passes. It is not a claim of visual WYSIWYG acceptance or recognition completeness.
