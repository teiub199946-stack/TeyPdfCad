# API Race Audit Follow-up

Date: 2026-09-18

## Scope

Read-only inspection of the latest API and Jobs state:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api`
- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs`

Focus: enqueue-to-`jobsById` ordering, client `JobId` overwrite, completed/result visibility, result endpoint behavior, cancellation, and concurrent POST/GET behavior. No source files were changed.

## Closed or improved

- **Enqueue-to-registry race materially closed:** a singleton `SemaphoreSlim` serializes POST submission (`Program.cs:17`, `46-47`, `80-83`), and `jobsById` is reserved before enqueue (`Program.cs:58-61`). A concurrent GET now finds the job instead of returning the previous immediate `404` window.
- **JobId overwrite closed:** an existing `JobId` is rejected with `409 job_id_conflict` (`Program.cs:55-56`), and duplicate idempotency requests use `TryAdd` for the canonical job (`Program.cs:49-52`).
- **Queued cancellation exposed:** `POST /jobs/{jobId}/cancel` calls `TryCancel` (`Program.cs:123-138`), so the previous absence of an API cancellation path is addressed.
- **Submission rollback exists:** failed enqueue removes the pre-registration (`Program.cs:63`, `74-77`).

## Remaining findings

### P1 - Default worker still publishes an artifact key without creating the artifact

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\InMemoryConversionWorker.cs:10-11`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\ConversionJobWorkerHostedService.cs:28-29`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:116-120`
- **Reason:** The default in-memory worker returns `jobs/{jobId}/result.dwg`, but does not write it through `IConversionArtifactStore`. The result endpoint consequently returns `404 artifact_not_found` unless another component creates the file.

### P2 - Completed status remains visible before result registry publication

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\ConversionJobRunner.cs:19-23`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\ConversionJobWorkerHostedService.cs:28-29`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:108-114`
- **Reason:** Queue status is completed before `_results.Set(result)`. A concurrent status GET can report `completed` while the result endpoint returns `409 result_pending`. A process failure in that interval loses the in-memory result metadata.

### P2 - Successful POST still returns a hardcoded queued snapshot

- **Absolute path/lines:** `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:61-72`
- **Reason:** The hosted worker may process the job before the response is built, but the response always uses `ConversionJobStatus.Queued`. A subsequent GET can immediately show `processing` or `completed`.

### P2 - Reservation rollback still permits a transient synthetic queued status

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:58-61`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:91-94`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:146-155`
- **Reason:** The registry reservation is visible before `EnqueueAsync` commits. `GetStatusOrQueued` intentionally converts a missing queue status into `Queued`. A concurrent GET during a blocked or ultimately cancelled enqueue can therefore report queued for a job that is later removed by rollback. The prior 404 race is reduced, but status is not transactional with enqueue.

## Residual risks

- Queue, job registry, result registry, and artifact state remain process-local or filesystem-local; restart loses in-memory lookup and result metadata.
- No runtime concurrent HTTP stress test was run; this is source inspection only.
- Idempotency is serialized at the API POST boundary, but external callers could still mutate the injected queue/registry independently of that gate.
- Result retry and artifact readiness remain implicit; clients must interpret `result_pending` and `artifact_not_found` themselves.
