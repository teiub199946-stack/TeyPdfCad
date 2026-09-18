# Lifecycle Audit v2: Runner, Registry, Retention, and README

Date: 2026-09-18 12:27 (Europe/Moscow)
Scope: independent read-only source follow-up after the latest lifecycle changes.

## Checked files

- `src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs`
- `src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs`
- `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs`
- `src/TeyPdfCad.Web.Api/ArtifactRetentionHostedService.cs`
- `README.md`

Source and test files were not changed. Runtime and build commands were not run, as required by the audit boundary.

## Status of the previous three P2 findings

### 1. Job-id reuse race in `MarkTerminal`: mitigated, but not eliminated for same-idempotency replacements

The previous interface accepted only a string job id. It now accepts the completed `ConversionJob` object (`src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs:3-9`). `InMemoryConversionJobRegistry.MarkTerminal` compares the current entry's idempotency key before updating it (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:52-61`). This prevents an old completion from marking a replacement that has the same `jobId` but a different idempotency key.

The residual race remains conditional on reusing both the same `jobId` and the same `IdempotencyKey`: the registry still keys storage and the compare-and-swap by `jobId` (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:8`, `:55-60`). The reviewed contract has no immutable entry token or generation number. Treat the original P2 as partially closed rather than fully eliminated for that edge case.

### 2. One-hour retention sweep delay: closed at the previous default, configurable eventual cleanup remains

Both retention services now read `TEYPDFCAD_RETENTION_SWEEP_MINUTES`, validate it as positive, and use it for `PeriodicTimer` (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:29-37`; `src/TeyPdfCad.Web.Api/ArtifactRetentionHostedService.cs:23-31`). The README documents the default value as five minutes (`README.md:38-40`). The prior fixed one-hour behavior is therefore closed.

Cleanup is still eventual: an item may remain for almost one configured sweep interval after its retention age expires. The metadata service and artifact service each have their own timer (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:35-44`; `src/TeyPdfCad.Web.Api/ArtifactRetentionHostedService.cs:29-39`), so the interval is an operational bound, not an exact deletion deadline.

### 3. Missing `MarkTerminal` on worker terminal paths: closed for `ConversionJobRunner`, interface-level misuse remains possible

`ConversionJobRunner` now marks the job terminal after successful completion, cancellation, and worker failure (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:24-29`, `:31-35`, `:37-42`). This closes the previously identified omission in the main worker lifecycle. The public registry interface still exposes marking as a separate call (`src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs:5-9`), so an unrelated future caller can still bypass it; that is an API-contract risk rather than a missing call in the reviewed runner.

## New or remaining risks

### P2: metadata and artifact retention are independent sweeps

`JobMetadataRetentionHostedService` removes result/job/queue metadata in its own loop (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:35-57`), while `ArtifactRetentionHostedService` deletes files in a separate loop (`src/TeyPdfCad.Web.Api/ArtifactRetentionHostedService.cs:29-50`). They share configuration values but have no transaction or coordination boundary. A pass failure or timing difference can temporarily leave metadata without its artifact, or an artifact without metadata. The README's statement that both use the same window (`README.md:51`) should therefore be read as a target retention policy, not an atomic consistency guarantee.

### P2: cancellation/failure paths ignore queue completion success

The runner checks `TryComplete` and fails the successful path when it returns `false` (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:24-29`), but the cancellation and exception paths ignore the boolean result while still marking the registry terminal (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:31-42`). If queue state has already changed, metadata can report a terminal job while the queue retains a processing/tombstone state. This is conditional on a concurrent lifecycle transition; no runtime or concurrency test was run in this audit.

### P2: same-idempotency replacement remains the registry identity boundary

The idempotency-key comparison is a meaningful mitigation, but it assumes that two distinct logical jobs will not legitimately reuse both `jobId` and `IdempotencyKey` while an old completion is still in flight (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:52-61`). A generation token or immutable registry entry identity would remove that remaining assumption. No such token is present in the reviewed interface (`src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs:3-9`).

## Positive checks

- `ConversionJobRunner` validates result ownership and terminal status before completing the queue (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:46-65`).
- Registry updates use a compare-and-swap entry value, and conditional expiry removal cannot delete an entry whose timestamp changed concurrently (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:65-76`).
- Both retention services reject non-positive retention and sweep intervals (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:25-32`; `src/TeyPdfCad.Web.Api/ArtifactRetentionHostedService.cs:19-26`).
- README documents the retention window, sweep interval, metadata capacity, and the fact that processing jobs are retained (`README.md:38-51`).

## Validation boundary

This is a source-only follow-up audit of the six files listed above. It does not certify queue implementation details outside `ConversionJobRunner`, API endpoint ordering, persistent storage deletion semantics, process shutdown behavior, AutoCAD conversion, or release readiness. No source/test edits, runtime execution, or build execution were performed.
