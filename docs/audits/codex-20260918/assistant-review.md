# Backend Foundation Review

## Findings

### P1 - Idempotency key collision risk

- **File/area:** `src/TeyPdfCad.Web/Jobs/ConversionJob.cs:39-55`; `src/TeyPdfCad.Web/Jobs/ConversionSettings.cs:7-17`
- **Reason:** The idempotency digest is built by concatenating raw pipeline and settings values with `\n` and `=` separators. Settings values are unrestricted strings and are not escaped or length-prefixed. A value containing newline and field-like text can produce the same canonical string as a different settings object. The queue can then incorrectly deduplicate two different conversions.

### P1 - Queue and worker do not enforce one lifecycle

- **File/area:** `src/TeyPdfCad.Web/Jobs/InMemoryConversionWorker.cs:14-20`; `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:47-61`
- **Reason:** The worker only invokes the handler and returns its result. It does not move the queue from `Processing` to `Completed`, `Failed`, or `Cancelled`; that requires a separate `TryComplete` call. A worker can return `Failed` or `Cancelled` while the queue still reports `Processing`. There is also no queue operation to cancel a job while it is still `Queued`.

### P2 - Invalid worker results are accepted

- **File/area:** `src/TeyPdfCad.Web/Jobs/ConversionResult.cs:3-17`; `src/TeyPdfCad.Web/Jobs/InMemoryConversionWorker.cs:14-20`
- **Reason:** The public result record allows inconsistent status and metadata combinations, and the worker returns handler output without validating the job id, terminal status, or required fields. Examples include `Completed` without an artifact key, a result for another job, or a final `Queued` result.

### P2 - SHA-256 helper depends on stream position

- **File/area:** `src/TeyPdfCad.Web/Jobs/ConversionJob.cs:32-36`
- **Reason:** `ComputePdfSha256(Stream)` hashes from the current stream position and consumes the stream. Reusing a stream after a prior read or hash produces a digest for only the remaining bytes, so the same PDF can receive a different idempotency identity.

### P2 - Idempotency lookup is missing from the queue abstraction

- **File/area:** `src/TeyPdfCad.Web/Jobs/IConversionJobQueue.cs:3-13`; `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:64-70`
- **Reason:** The concrete in-memory queue has `TryGetByIdempotencyKey`, but the interface does not. Code using `IConversionJobQueue` receives only `false` from a duplicate enqueue and cannot retrieve the canonical existing job id without depending on the implementation.

## Checks

- Reviewed only `src/TeyPdfCad.Web` and `tests/TeyPdfCad.Web.Tests`.
- Inspected `ConversionJob`, `ConversionSettings`, `ConversionResult`, `IConversionWorker`, `InMemoryConversionWorker`, `IConversionJobQueue`, `InMemoryConversionJobQueue`, and the current web tests.
- `dotnet build src\\TeyPdfCad.Web\\TeyPdfCad.Web.csproj --no-restore --verbosity minimal` passed with 0 warnings and 0 errors.
- The existing tests cover basic idempotency stability, pipeline-version changes, one SHA calculation, worker success, queue deduplication, and one queue lifecycle path.
- No production code was changed during this review.
- `docs/PROJECT_STATE.md`, Core, and AutoCAD were not touched.

## Remaining risks

- Full `dotnet test` execution was not completed. Restore was blocked by inaccessible `C:\Users\Admin\AppData\Roaming\NuGet\NuGet.Config`, and usable test assets were not present for the web test project.
- There is no automated coverage for delimiter/newline key collisions, concurrent duplicate enqueue, full bounded-queue backpressure, producer cancellation while blocked, queued cancellation, handler exceptions, invalid handler results, stream position behavior, or integrated queue-worker terminal transitions.
- Queue shutdown/completion semantics are not represented in the current abstraction; a blocked producer or consumer may require external cancellation and coordination.

## Resolution after review

- P1 canonical-key collision risk: fixed with length-prefixed canonical fields.
- P1 lifecycle gap: fixed with `ConversionJobRunner`, which validates and completes worker results.
- P2 invalid worker results: rejected by `ConversionJobRunner` before terminal completion.
- P2 stream-position dependency: seekable streams are hashed from position zero and restored afterward.
- P2 abstraction lookup gap: `IConversionJobQueue` now exposes `TryGetByIdempotencyKey`.
- Queued cancellation was added and cancelled entries are skipped during dequeue.
- `TeyPdfCad.Web` builds with 0 warnings and 0 errors. xUnit execution remains blocked by unavailable NuGet packages in this environment.
