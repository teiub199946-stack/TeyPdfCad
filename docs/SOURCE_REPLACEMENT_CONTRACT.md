# Source Replacement Contract

Status: required contract for the final semantic PDF -> DWG pipeline.

## Safety principle

TeyPdfCad never removes source geometry only to increase the native-object count.
A source object may be suppressed only when an explicit replacement plan records
which native candidate covers it.

## Shared source policy

Replacement is candidate-granular until SubEntityRef exists.

If two or more native candidates share one SourceId:

- all involved native candidates are deferred;
- every SourceId used by those candidates is preserved;
- no involved source object is suppressed;
- the audit records `DeferredShared` with severity `High`.

This intentionally sacrifices native conversion rate to avoid duplicated or lost
geometry. Part-level deferral is deferred to the SubEntityRef iteration.

## SourceCoverageMap

Every suppressed SourceId must have at least one candidate key in
`SourceCoverageMap`.

After DWG writing, `ReplacementExecutionReport` compares this map with the
candidate keys that were actually created. A suppressed source without an
actually-created covering candidate is `GeometryLost` and forces `PARTIAL`.

This is structural/provenance coverage evidence, not pixel-level proof that the
native object renders identically to the suppressed PDF geometry. Visual
equivalence remains a separate AutoCAD render gate.

## Residual severity

Residual severity is deterministic:

- `Critical`: suppressed/missing source cannot be proven to have a native replacement.
- `High`: deferred shared candidate, partial provenance, protected-role overlap,
  or unresolved evidence overlapping primary geometry.
- `Medium`: unresolved source-specific evidence that does not overlap proven
  primary geometry.
- `Low`: reserved for explicitly classified decorative/fidelity-only residuals;
  it is not inferred automatically.

Severity never relaxes FULL_PASS. Any residual prevents FULL_PASS.

## Page audit statuses

- `FULL_PASS`: no residuals, no conflicts, no semantic/hatch warnings, no page
  diagnostics, read-back succeeded, and GeometryLost is zero.
- `PASS_WITH_RESIDUALS`: all source geometry is preserved, GeometryLost is zero,
  but one or more semantic residuals/conflicts/warnings remain.
- `PARTIAL`: geometry loss is detected, page extraction is incomplete, or
  another page-level correctness invariant fails.

## Multi-page failure policy

The converter is fail-soft for semantic/page-level PARTIAL results:

- process every page that can be processed safely;
- preserve source geometry on unresolved pages;
- still emit the DWG when structural writing/read-back succeeds;
- set the overall conversion outcome to `Partial` if any page is not FULL_PASS.

The converter is fail-fast only for structural failures that make the output
untrustworthy as a file, such as invalid input/output paths, DWG serialization
failure, or DWG read-back failure.

No percentage threshold is used.

## Paint order contract

Paint order is explicit and deterministic. The pure ordering key is:

`(paintPriority, insertionIndex, sourceIdOrdinal)`

The current bottom-to-top priorities are:

1. BackgroundMask
2. SolidFill
3. PatternHatch
4. BaseGeometry
5. PreservedBoundary
6. Axis
7. Dimension
8. Annotation
9. Text
10. ReviewOverlay

The DWG writer serializes an explicit `SortEntitiesTable`; insertion order is
not treated as visual proof. Real AutoCAD render acceptance remains a separate
gate.

## Deferred work

Before part-level replacement can be enabled:

- introduce SubEntityRef with explicit geometry identity;
- assign semantic source roles explicitly inside recognizers;
- support part-level shared ownership and part-level deferral;
- keep the existing whole-object conservative fallback until those contracts
  have dedicated regression and AutoCAD render tests.
