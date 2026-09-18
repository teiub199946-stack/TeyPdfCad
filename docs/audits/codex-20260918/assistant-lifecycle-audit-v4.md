# Lifecycle Audit v4: Retention Fault Isolation and Idempotency Race

Date: 2026-09-18 12:38 (Europe/Moscow)
Scope: independent read-only follow-up of the latest fault-isolation change.

## Checked files

- `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionResultRegistry.cs`
- `src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs`
- `src/TeyPdfCad.Web.Api/Program.cs`
- `src/TeyPdfCad.Web/Jobs/ConversionJob.cs`

Source, tests, and runtime state were not changed or executed. This report is based on source inspection only.

## Retention fault isolation

### Artifact failure: later metadata stages continue

`RunSweepAsync` isolates artifact deletion in its own `try/catch`. A non-cancellation exception is logged at `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:68-80`, and execution proceeds to the result stage beginning at line 82. Cancellation is deliberately rethrown at lines 73-75 so shutdown is not converted into a successful sweep.

### Result failure: jobs and queue cleanup continue

Result cleanup is isolated at `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:82-89`. If `_results.RemoveOlderThan` throws, the exception is logged and the job cleanup block at lines 91-98 still runs, followed by the queue cleanup block at lines 100-107.

### Job failure: queue cleanup continues

Job cleanup is isolated at `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:91-98`. A job-registry exception is logged and the queue cleanup at lines 100-107 still executes.

### Queue failure: no later stage exists

Queue cleanup is the final stage in the sweep (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:100-110`), so its failure cannot skip another cleanup stage. The counts that completed are retained for the summary log; a queue exception leaves `queue` at zero and is logged.

## Cancellation behavior

- Cancellation during artifact deletion is rethrown by `RunSweepAsync` (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:70-75`) and caught by the outer loop, which breaks cleanly (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:44-50`). Results/jobs/queue cleanup does not continue after shutdown cancellation, which is the intended stop behavior.
- Cancellation while waiting for the next sweep is passed to `PeriodicTimer.WaitForNextTickAsync` (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:57`). That await is outside the inner sweep catch, so the hosted task completes through normal cancellation propagation rather than swallowing the cancellation.
- Results/jobs/queue cleanup methods are synchronous and do not receive a token (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:82-107`). A cancellation request arriving during one of those in-memory loops is observed only after the current stage returns. This is a bounded shutdown-latency risk for large registries, not a cancellation correctness failure in the reviewed control flow.

## Same `jobId` + same `IdempotencyKey` race assessment

### Conclusion: not a real behavioral bug under current deduplication semantics

The idempotency key is derived from the normalized PDF hash, pipeline version, and canonical settings (`src/TeyPdfCad.Web/Jobs/ConversionJob.cs:12`, `:56-71`). The queue rejects a second enqueue with the same key while the existing queue record is present (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:26-32`). API submission checks the queue by idempotency key first and returns the existing job as deduplicated (`src/TeyPdfCad.Web.Api/Program.cs:98-103` for JSON and `:188-193` for uploads), before applying the separate job-id conflict check.

Registry retention also does not remove a job before it has been marked terminal: new entries start at `DateTimeOffset.MaxValue` and only a terminal timestamp can satisfy expiry (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:19-26`, `:52-76`). Therefore an old worker completion cannot normally remain in flight after the old registry record becomes eligible for retention. A second request with the same key represents the same logical conversion and is intentionally deduplicated; it is not a competing replacement.

The `MarkTerminal` idempotency-key comparison remains useful defensive protection (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:52-61`), but the artificial test scenario that removes a registry entry and immediately re-adds a replacement does not reproduce a production behavioral error when the queue's same-key deduplication invariant holds. No new finding is raised for same `jobId` + same key under the current semantics.

## Remaining risks

### P2: non-cancellation stage failures can produce partial retention passes

Fault isolation intentionally allows later stages to run, so a failed artifact/result/job stage can leave that stage's data until a later sweep (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:77-107`). This is the desired availability tradeoff, but it means one sweep is not an all-or-nothing retention transaction.

### P2: synchronous metadata cleanup can delay shutdown

The in-memory result, job, and queue cleanup calls have no cancellation token (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:82-107`). They should remain bounded for the configured metadata capacity, but shutdown latency grows with the amount of work in a sweep.

## Validation boundary

This is a source-only audit of retention fault isolation, cancellation propagation, and deduplication identity semantics. It does not certify runtime host cancellation, concurrent API timing, filesystem fault injection, queue implementation under load, AutoCAD conversion, or release readiness. No source/test edits, build, or runtime execution were performed.
