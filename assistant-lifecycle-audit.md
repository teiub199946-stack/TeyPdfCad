# Lifecycle Audit: Job Registry and Metadata Retention

Date: 2026-09-18 12:19 (Europe/Moscow)
Scope: read-only source inspection requested by the main task.

## Checked files

- `src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs`
- `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs`

No source files or tests were changed. No runtime or test execution was required for this bounded audit.

## Findings

### P2: terminal marking is vulnerable to job-id reuse during a remove/re-add race

`MarkTerminal` reads an entry and then updates it with a compare-and-swap loop keyed only by `jobId` (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:52-60`). `TryRemove` can remove that entry (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:41-49`), and `TryAdd` can subsequently add a replacement under the same key (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:19-27`). If the original completion callback continues after removal, the retry at lines 56-60 can observe the replacement entry and mark the new job terminal.

This is conditional on allowing a new job to reuse an old `jobId`; the reviewed interface does not carry an entry token or job instance into `MarkTerminal` (`src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs:3-9`). The current implementation therefore cannot distinguish completion of the old record from completion of a replacement record. No concurrent remove/re-add regression test was found in the reviewed scope.

### P2: retention is delayed by the fixed hourly sweep interval

`JobMetadataRetentionHostedService` runs one cleanup pass and then waits on a one-hour `PeriodicTimer` (`src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:30-39`, `:52`). An entry can therefore remain in memory for up to approximately one additional hour after its configured retention age has elapsed. This is bounded eventual cleanup, not an exact TTL deadline, and should be reflected in operational expectations.

### P2: registry TTL depends on every terminal path calling `MarkTerminal`

New entries start with `LastTerminalAt = DateTimeOffset.MaxValue` (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:7`, `:24-26`). `RemoveTerminalOlderThan` only removes entries whose timestamp is at or before the cutoff (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:64-76`), so an entry that reaches a terminal state without a matching `MarkTerminal` call is retained indefinitely. The interface exposes `MarkTerminal` as a separate operation (`src/TeyPdfCad.Web/Jobs/IConversionJobRegistry.cs:5-9`); the reviewed files do not enforce that lifecycle transition atomically with status completion.

## Positive checks

- Capacity admission and duplicate `jobId` checks are serialized by `_capacityGate` (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:9`, `:22-27`).
- Timestamp comparisons use `DateTimeOffset.UtcNow` consistently for marking and cutoff calculation (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:58`, `:68`).
- Conditional removal uses `TryRemove(KeyValuePair<,>)`, so a concurrent timestamp update prevents removal of the newer entry (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:70-75`).
- Invalid non-positive retention ages are rejected by both the registry and hosted-service configuration (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobRegistry.cs:64-67`; `src/TeyPdfCad.Web.Api/JobMetadataRetentionHostedService.cs:24-27`).

## Validation boundary

This report is a source-only audit of the three files listed above. It does not certify API behavior, worker completion ordering, persistence, process shutdown behavior, or AutoCAD runtime behavior. No fixes are included in this report.
