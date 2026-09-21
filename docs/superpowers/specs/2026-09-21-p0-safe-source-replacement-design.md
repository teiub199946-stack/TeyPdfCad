# P0 Safe Source Replacement — Design Specification

## Goal

Permit source PDF geometry to be suppressed only after a temporary written DWG has been read back and has proved that every expected native entity for a replacement candidate exists with its CandidateId, role, type, geometry fingerprint, and required properties.

## Scope

P0 closes the unsafe source-replacement path; it does not improve recognition quality.

Included:

- persistent CandidateId XData on native entities;
- writer expectation manifest and independent read-back verifier;
- two-pass document writing: probe without suppression, then final with verified suppression;
- explicit claims from every suppressible recognizer and HATCH boundary/pattern classification;
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

With recognition disabled, the candidate and claim sets are empty. With source replacement disabled, every source is preserved. The two-pass driver still runs: SuppressionGate returns empty authorization and final equals probe. No downstream stage may branch on either gate.

## Two-pass document write

The unit is the complete DWG document, never one DWG per page. The writer serializes to a Stream supplied by its caller and never owns or closes that Stream. The CLI owns the probe and final FileStreams, closes each one before read-back, and owns atomic publication.

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

sealed record NativeWriteManifest(
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

DwgReadBackVerifier reopens the on-disk probe file after the CLI has closed its FileStream; it is independent from the writer. It compares the triple CandidateId, Role, EntityKind, then fingerprint and required properties. Fingerprints verify entities already identified by XData; they are not spatial matching.

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

sealed record NativeReadBackVerification(
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

SuppressionGate is the only component allowed to grant suppression. It receives the existing, modified Core SourceReplacementPlan and NativeReadBackVerification.

A source is suppressed only if it has one full valid suppressible claim, its candidate is verified, and it is neither HatchBoundary nor EvidenceOnly. There is no spatial fallback. Every source has a non-empty page-unique SourceId. An empty or duplicate SourceId produces SourceIdentityViolation at Critical severity, preserves all involved sources, and makes every related candidate ineligible for suppression. P0 authorization is keyed only by valid SourceId, so invalid-ID entities can never enter SuppressSourceIds; per-entity ordinal identity is not introduced in P0 and all invalid-ID entities remain emitted by the writer.

## Recognizer source-claim contract

Every suppressible semantic candidate exposes explicit claims created by its own recognizer. The planner must never infer a role from VectorEntity runtime type and must never use legacy ProvenanceIds to authorize suppression; legacy provenance remains diagnostic only.

~~~
enum SourceUsageRole
{
    Unknown = 0,
    DimensionLine, ExtensionLine, ArrowGeometry,
    LeaderShaft, LeaderArrow, LeaderLanding,
    AxisGeometry, LevelMarker, Text,
    HatchPattern, HatchBoundary, EvidenceOnly
}

sealed record RecognizerSourceClaim(
    string SourceId,
    SourceUsageRole Role,
    SourceClaimState State,
    bool IsPartial);
~~~

Dimension recognizers emit DimensionLine, ExtensionLine, ArrowGeometry, and Text claims where those source primitives were used. Leader recognizers emit LeaderShaft, LeaderArrow, LeaderLanding when present, and Text. Axis and Level recognizers emit AxisGeometry/LevelMarker and Text. HATCH continues to use explicit boundary/pattern lists.

The candidate's declared whole-source set is independent from its claim list and is used for deterministic identity and completeness validation only; it never authorizes suppression. The normalized declared SourceId set must equal the distinct union of RecognizerSourceClaim.SourceId. Any mismatch is DeferredUnresolvedClaims at High severity, preserves the complete union, and makes the candidate ineligible. This allows legacy provenance/source-set data to detect omissions while forbidding it from granting suppression.

Each semantic type has both a minimum required-role set and a closed allowed-role set. An unknown semantic type or any role outside the allowed set is fail-closed as DeferredUnresolvedClaims High.

Minimum required roles are:
- DIMENSION: DimensionLine, ExtensionLine, Text; ArrowGeometry is additionally claimed whenever arrow source geometry was used.
- LEADER: LeaderShaft, LeaderArrow, Text; LeaderLanding is required only when landing geometry was used.
- AXIS: AxisGeometry.
- LEVEL: LevelMarker and Text.
- ARC_DIMENSION: DimensionLine and Text.
- confident HATCH: HatchBoundary and HatchPattern.

Unknown/default role, EvidenceOnly, Unresolved, IsPartial=true, or a claim SourceId containing a partial marker while presented as a whole-source claim always defers the whole candidate. Exact duplicate claims with identical SourceId/Role/State/IsPartial are normalized away deterministically before validation. One SourceId carrying more than one distinct role inside the same candidate is a ClaimRoleConflict and also defers the whole candidate because P0 has no SubEntityRef to disambiguate parts.

CandidateId uses the normalized declared source set, not the claim set. Exact duplicate candidate descriptors with the same CandidateId are deterministically deduplicated. The same CandidateId with different semantic type, source set, claims, or HATCH classification is CandidateIdentityViolation Critical; every involved source is preserved and no candidate with that identity is emitted.

Warning provenance is fail-closed EvidenceOnly data. An orphan warning preserves only its referenced source. If warning provenance overlaps a candidate source, that candidate is DeferredUnresolvedClaims High. Warning provenance referencing a missing page SourceId is SourceMissingFromPage Critical.

A candidate with a missing explicit claim, an EvidenceOnly role, or an Unresolved claim is DeferredUnresolvedClaims at High severity: all its sources are preserved and P0 emits no native candidate for it. This prevents a source-preserved/native-created visual duplicate. SubEntityRef remains excluded; every P0 claim refers to a whole SourceId.

The shared-claim policy remains one-pass over the full graph:

- the overlap graph is built once from every valid claim before eligibility is evaluated;
- deferred candidates remain claimants for every other SourceId they claim;
- any SourceId with two or more valid CandidateIds defers all involved candidates;
- there is no recomputation, claimant removal, or promotion after deferral;
- therefore chain/cycle conflicts propagate deterministically across the full claim graph;
- every source used by a deferred candidate is preserved;
- each deferred case reports DeferredShared at High severity.

A failed verification produces CandidateNotVerified at Critical severity while preserving all sources claimed by that candidate. GeometryLost is not emitted for a preserved source. Any residual produces PASS_WITH_RESIDUALS, not FULL_PASS.

Residual severity is fixed: DeferredShared and DeferredUnresolvedClaims are High; DeferredUncertainHatch is Medium; CandidateNotVerified, SourceIdentityViolation, CandidateIdentityViolation, and SourceSuppressionViolation are Critical.

FULL_PASS means every P0 probe-verification and final-structural-sanity check passed, and no residual, CandidateNotVerified, SourceIdentityViolation, or SourceSuppressionViolation was recorded. It guarantees no detected loss of source geometry and the declared minimum emitted-entity checks. It does not guarantee recognition completeness, visual WYSIWYG fidelity, or engineering-semantic correctness beyond those declared checks.

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
4. Two-pass executor: verified candidate suppresses allowed sources; unverified candidate emits CandidateNotVerified and preserves all sources; probe failure emits no final DWG; a requested source-emission multiset absent from the probe inventory fails before final publication; final fingerprint mismatch emits SourceSuppressionViolation and publishes no final DWG; no spatial fallback.
5. Source identity and explicit claims: Duplicate_source_id_is_never_collapsed; Empty_source_id_is_not_suppressible; Duplicate_source_id_for_verified_candidate_still_preserves_sources; Dimension_MissingExtensionLine_IsDeferred_NoEligibleSources; SameSource_TwoRoles_SameCandidate_Violation; CandidateSourceSet_MustEqualUnionOfClaims_MismatchDefers; Claim_UnsetRole_Rejected_NotSilentlyDefaulted; PartialFlag_AlwaysDefers_RegardlessOfSourceShape; TwoCandidates_SameCandidateId_WithDifferentClaims_BothDeferred; DuplicateCandidate_ExactDuplicate_IsDeduplicatedDeterministically; WarningEvidence_OverlappingCandidate_BlocksEligibility; OrphanEvidenceOnlyClaim_SourcePreserved_NoCandidateImpact; role inference from VectorEntity is forbidden. Shared chains and cycles yield identical deferred candidates despite recognizer enumeration order.
6. HATCH: confident creates native HATCH, preserves BoundarySourceIds, suppresses PatternSourceIds only after verified boundary binding, and leaves matching boundary fingerprint; uncertain emits no native HATCH and preserves all sources.
7. Paint order: input enumeration changes do not change immutable ordering.
8. Regression: ACadSharp round-trip and committed user AutoCAD 2022 save/reopen fixture.

## Acceptance

P0 is complete only when CI proves source suppression comes from on-disk read-back verification, final structural sanity uses a verified output-fingerprint multiset and source-emission inventory rather than SourceId count, no writer counter grants suppression, and every required test passes. It is not a claim of visual WYSIWYG acceptance or recognition completeness.
