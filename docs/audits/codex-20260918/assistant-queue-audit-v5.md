# Queue Audit v5: Fail-Fast Enqueue and Backpressure

Date: 2026-09-18 12:50 (Europe/Moscow)
Scope: bounded read-only audit of the queue contract, fail-fast implementation, API enqueue branches, README contract, and queue tests.

## Checked files

- `src/TeyPdfCad.Web/Jobs/IConversionJobQueue.cs`
- `src/TeyPdfCad.Web/Jobs/ConversionJobEnqueueResult.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs`
- `src/TeyPdfCad.Web.Api/Program.cs`
- `README.md`
- `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs`

Source, tests, and runtime state were not changed or executed.

## Findings and checks

### Duplicate handling: PASS

The queue contract exposes a result enum with distinct `Accepted`, `Duplicate`, and `CapacityExceeded` outcomes (`src/TeyPdfCad.Web/Jobs/ConversionJobEnqueueResult.cs:3-8`), and `IConversionJobQueue.EnqueueAsync` returns that result (`src/TeyPdfCad.Web/Jobs/IConversionJobQueue.cs:3-7`). `InMemoryConversionJobQueue` reserves the idempotency key with `TryAdd`; an existing key returns `Duplicate` before touching the channel (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:26-34`). The queue test covers a duplicate with a different job id and confirms that the original job is returned (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:69-87`).

Both API enqueue paths also check the queue by idempotency key before job-id conflict handling (`src/TeyPdfCad.Web.Api/Program.cs:98-109`; `:199-207`). If a race still returns `Duplicate`, both paths remove the provisional registry entry and resolve the existing queue job (`src/TeyPdfCad.Web.Api/Program.cs:123-135`; `:227-240`).

### Capacity rollback: PASS

The bounded channel is configured with the requested capacity and `FullMode = Wait`, but enqueue uses `_pending.Writer.TryWrite(job)` rather than an awaitable `WriteAsync` (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:13-23`, `:35-42`). A full channel therefore returns immediately with `CapacityExceeded`; the rollback removes the provisional `_jobs`, `_statuses`, and `_terminalAt` entries before returning (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:39-42`).

The queue test verifies that a capacity-one queue returns `CapacityExceeded` and does not retain the rejected job (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:104-114`). The JSON API removes the registry entry and returns `429 queue_full` (`src/TeyPdfCad.Web.Api/Program.cs:115-121`). The multipart API removes the registry entry, deletes the stored input PDF with a non-cancelled cleanup token, and returns the same `429` (`src/TeyPdfCad.Web.Api/Program.cs:217-225`). `README.md:41` documents the capacity setting and `README.md:50` documents fail-fast `429 queue_full` behavior.

### Full-queue hang: no source-level hang found

Because `TryWrite` is nonblocking, a full bounded channel cannot hold the HTTP enqueue request waiting for a consumer (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:35-42`). The API branches handle the immediate result before returning (`src/TeyPdfCad.Web.Api/Program.cs:115-135`, `:217-240`). This closes the prior risk of a `WriteAsync` wait on a full queue.

### Cancellation: cooperative and bounded, with one expected acceptance race

`EnqueueAsync` throws immediately when the supplied token is already cancelled (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:26-32`). `DequeueAsync` passes its token to `ChannelReader.ReadAsync`, so an idle worker can stop without a queued item (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:45-50`). The API passes request cancellation into submission gate waits and enqueue calls (`src/TeyPdfCad.Web.Api/Program.cs:98`, `:115`; `:196`, `:217`).

There is no confirmed bug when cancellation arrives after the initial `ThrowIfCancellationRequested` but before/around `TryWrite`: the method may accept the job even though the caller later disconnects. This is the normal atomic boundary of a nonblocking enqueue; the job remains valid for the worker and the request may fail to deliver its response. No cancellation-token test exists in the reviewed queue tests.

### Channel and dequeue semantics: PASS with test coverage limited to core cases

Cancelled queued jobs remain as channel tombstones but are skipped by `DequeueAsync` after the status check (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:45-62`). `TryCancel` only transitions `Queued` to `Cancelled`, so a processing job is not cancelled out from under its worker (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:84-90`). Tests cover the cancelled-tombstone skip (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:90-102`) and terminal cleanup while preserving a processing job (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:116-133`).

The queue uses `SingleReader = false` and `SingleWriter = false`, matching the hosted API/worker model (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:18-23`). No source issue was found in the reviewed channel semantics.

## Test and validation gaps

- `Queue_ReturnsCapacityExceededWithoutWaiting` asserts the result but does not measure an elapsed-time upper bound (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:104-114`). The source's `TryWrite` is direct evidence of nonblocking behavior, but the test name is not an independent timing proof.
- No reviewed test exercises a pre-cancelled enqueue token or cancellation racing with enqueue.
- No API integration test verifies that JSON and multipart `429 queue_full` responses leave registry, queue, and multipart input artifacts consistent after the rollback.

## Validation boundary

This is a source-only audit of queue contracts, in-memory channel behavior, API enqueue branches, README claims, and the listed queue tests. It does not certify runtime latency, concurrent HTTP timing, host shutdown, filesystem failure injection, or production load behavior. No source/test edits, build, test, or runtime execution were performed.
