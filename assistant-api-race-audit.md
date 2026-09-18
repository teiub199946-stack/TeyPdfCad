# API Race Audit

Date: 2026-09-18

## Scope

Read-only inspection of:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api`
- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs`

Focus areas: `jobsById` ordering relative to enqueue, status visibility before result registry publication, result endpoint behavior, idempotent POST concurrency, cancellation, queue/runner lifecycle, and artifact lookup. No source files were changed.

## Findings

### P1 - Completed jobs can have no artifact available

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\InMemoryConversionWorker.cs:10-11`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\ConversionJobWorkerHostedService.cs:28-29`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:84-88`
- **Reason:** The default in-memory worker returns an artifact key but does not write an artifact. The hosted service stores only the result metadata. The result endpoint then opens the missing file and returns `404 artifact_not_found`. A separate artifact producer is required for a successful download.

### P1 - Registry insertion occurs after enqueue and can race with GET

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:44-50`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:53-62`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:73-74`
- **Reason:** The queue accepts a job before `jobsById` is populated. The hosted worker can dequeue or complete that job in the gap. A concurrent status or result GET can therefore return `404 job_not_found` even though the job was accepted and is already in the queue or completed.

### P1 - Client-provided JobId can overwrite an unrelated job

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:33-37`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:50`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:96-100`
- **Reason:** The request accepts an optional `JobId`, and registry assignment uses `jobsById[job.JobId] = job`. Different idempotency inputs with the same JobId create separate queue entries but leave only the last one addressable through status/result GET.

### P2 - Status can be Completed before result registry publication

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\ConversionJobRunner.cs:19-23`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\ConversionJobWorkerHostedService.cs:28-29`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:76-82`
- **Reason:** The runner completes the queue status before the hosted service calls `_results.Set`. During that interval status GET reports `completed`, while result GET reports `409 result_pending`. Because both queue and registry are in memory, a process failure in this interval loses the result.

### P2 - POST response status is hardcoded to queued

- **Absolute path/lines:** `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:44-51`
- **Reason:** The worker may process the job before the POST response is built, but the response always reports `queued`. A following GET can immediately report `processing` or `completed`.

### P2 - Accepted jobs cannot be cancelled through the API and enqueue can block

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\InMemoryConversionJobQueue.cs:17-20`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\InMemoryConversionJobQueue.cs:79-83`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:21-25`
- **Reason:** The queue has `TryCancel`, but the API exposes no cancellation endpoint. The request cancellation token only covers enqueue. With `BoundedChannelFullMode.Wait`, a full queue can block POST until request cancellation.

## Residual risks

- No runtime stress test was run for concurrent POST/GET, the enqueue-to-registry window, or a full bounded queue.
- Queue, job registry, result registry, and artifact location are process-local or filesystem-local; process restart loses in-memory state and can make previously accepted jobs unqueryable.
- Idempotency deduplication is atomic inside the queue, but API-level job registry publication is not one transaction with enqueue.
- No API-level cancellation or retry contract exists for `result_pending`, `artifact_not_found`, or queue backpressure.
- No source changes were made during this audit.
