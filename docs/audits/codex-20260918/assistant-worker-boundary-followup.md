# Worker Boundary Follow-up Review

Date: 2026-09-18

## Scope

Read-only review of the current `AutoCadConversionWorker`, `IAutoCadHostBridge`, `UnavailableAutoCadHostBridge`, and `TEYPDFCAD_WORKER_MODE` selection. The review also checked upload input artifact handling and the result endpoint boundary. No source files were changed.

## Findings

### P1 - `autocad` mode is fail-closed and cannot process a job

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:13-26`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\UnavailableAutoCadHostBridge.cs:3-15`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadConversionWorker.cs:18-49`
- **Reason:** Selecting `TEYPDFCAD_WORKER_MODE=autocad` registers `AutoCadConversionWorker`, but its bridge is always `UnavailableAutoCadHostBridge`. Every conversion reaches the bridge and throws `InvalidOperationException`, which the worker maps to `autocad_worker_error`. The hosted loop survives, but no AutoCAD conversion can succeed until a real host bridge is registered.

### P1 - Default `inmemory` mode returns an output key without creating a DWG artifact

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\InMemoryConversionWorker.cs:3-21`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:205-213`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\ConversionArtifactKeys.cs:3-7`
- **Reason:** The default worker returns `jobs/{jobId}/result.dwg` but never calls `IConversionArtifactStore.StoreAsync`. The result endpoint then attempts `OpenReadAsync` and normally returns `404 artifact_not_found`. The mode is useful as a lifecycle fake, but it is not an artifact-producing conversion implementation.

### P2 - Upload can leave orphaned `input.pdf` after enqueue failure or cancellation

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:152-170`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\IConversionArtifactStore.cs:3-12`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\ConversionArtifactKeys.cs:3-7`
- **Reason:** Upload writes `jobs/{jobId}/input.pdf` before `queue.EnqueueAsync`. If enqueue returns false or throws after the write, the code removes the registry reservation but has no delete operation for the input artifact. Repeated cancelled/full-queue uploads can leave files that no longer have a job lookup.

### P2 - AutoCAD host bridge contract is not a runtime host boundary yet

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\IAutoCadHostBridge.cs:3-8`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadConversionWorker.cs:26-40`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\ReconstructionCommands.cs:81-148`
- **Reason:** The interface accepts a PDF stream and returns a DWG stream, but no implementation connects it to AutoCAD `PDFIMPORT`, an active document, transactions, native dimension validation, or DWG save. Existing AutoCAD functionality is exposed as interactive commands requiring `MdiActiveDocument` and user selection, so it cannot be inferred as an implementation of this bridge.

## Worker mode behavior

- Missing `TEYPDFCAD_WORKER_MODE` defaults to `inmemory` (`Program.cs:13`, `19-21`).
- `autocad` selects the placeholder bridge (`Program.cs:14-18`).
- Any other value fails application startup (`Program.cs:23-26`).
- `AutoCadConversionWorker` reads `jobs/{jobId}/input.pdf`, invokes the bridge, stores output at `jobs/{jobId}/result.dwg`, and returns `Completed` only after `StoreAsync` succeeds (`AutoCadConversionWorker.cs:26-40`).

## Runtime-tested boundary

The following parts cannot be called runtime-tested from this source-only review:

- AutoCAD 2022 process launch, `PDFIMPORT`, `NETLOAD`, `TEYPDFPING`, `TEYPDFDUMP`, or `TEYPDFRECONSTRUCT` execution.
- A real `IAutoCadHostBridge` implementation and PDF-to-DWG conversion.
- Native `Dimension.Measurement` validation through the web worker.
- End-to-end upload -> `input.pdf` -> AutoCAD -> `result.dwg` -> result endpoint.
- Artifact cleanup after cancellation or enqueue failure.

Only source compatibility and DI mode selection were inspected. No builds, HTTP smoke tests, or AutoCAD runtime tests were run.
