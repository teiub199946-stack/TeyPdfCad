# Lifecycle Audit v3: Retention Coordinator and Runner Completion

Date: 2026-09-18 12:34 (Europe/Moscow)
Scope: short independent read-only follow-up after the latest lifecycle change.

## Checked files

- `src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs`
- `src/TeyPdfCad.Web.Api/Program.cs`
- `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs`
- `src/TeyPdfCad.Web.Api/ArtifactRetentionHostedService.cs` (absence verified; no separate service file remains)

`README.md` and unrelated queue/storage implementation were not re-audited for this bounded follow-up. Source, tests, and runtime state were not changed or executed.

## Required checks

### Artifact and metadata retention now use one coordinator: PASS

`Program.cs` registers `JobMetadataRetentionHostedService` as the sole retention hosted service alongside the worker (`src/TeyPdfCad.Web.Api/Program.cs:52-54`). The previous separate artifact service is no longer registered, and no `ArtifactRetentionHostedService.cs` file is present in the API project.

The registered coordinator receives `IConversionArtifactRetention` in its constructor (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:6-22`). Each sweep deletes expired artifacts first, then removes result, job, and queue metadata in the same loop (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:39-52`). This closes the previous risk that two independent hosted timers would drift without sharing a coordinator.

### `MarkTerminal` after unsuccessful `TryComplete`: PASS

The success path throws before `MarkTerminal` when queue completion returns `false` (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:24-28`). In the cancellation path, `MarkTerminal` is inside the successful `TryComplete` branch (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:31-35`). The worker-failure path has the same guard (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:37-42`). No reviewed runner path calls `MarkTerminal` after an unsuccessful queue transition.

## Closed previous risks

- Independent artifact and metadata retention timers are closed in the reviewed registration/implementation: one hosted service owns both operations (`src/TeyPdfCad.Web.Api/Program.cs:52-54`; `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:39-52`).
- Cancellation/failure paths marking the registry after `TryComplete == false` are closed in `ConversionJobRunner` (`src/TeyPdfCad.Web/Jobs/ConversionJobRunner.cs:31-42`).

## Remaining risks

### P2: coordinator pass is ordered but not transactionally all-or-nothing

Artifact deletion is awaited before metadata deletion (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:46-50`). If `DeleteOlderThanAsync` throws a non-cancellation exception, control jumps to the catch at lines 58-60 and the metadata cleanup calls in that pass are skipped. The single coordinator removes timer drift, but it does not guarantee that artifact and metadata cleanup complete together; a later sweep must retry the skipped work.

### P2: terminal metadata still depends on the caller's successful queue transition

The runner now guards `MarkTerminal`, but a job that reaches a terminal state through a different caller must still use the same contract. The registry API keeps `MarkTerminal(ConversionJob)` as a separate operation (`src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs:3-9`), and the API cancel endpoint calls it independently (`src/TeyPdfCad.Web.Api/Program.cs:287-290`). This is no longer a runner bug, but it remains an interface-level lifecycle obligation for future callers.

### P2: retention remains eventual rather than an exact TTL deadline

The coordinator still uses a configurable `PeriodicTimer` (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:33-42`, `:63`). An expired item can remain until the next sweep interval, and an exception can defer it for additional intervals. This is expected operational behavior, not an exact deletion timestamp.

## Validation boundary

This report is a source-only check of the requested coordinator registration and runner completion guards. It does not certify the queue implementation, artifact deletion internals, API runtime ordering, process shutdown, AutoCAD conversion, or release readiness. No source/test edits, build, or runtime execution were performed.
