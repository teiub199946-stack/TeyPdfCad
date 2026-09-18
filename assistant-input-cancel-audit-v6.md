# Input Validation and Cancellation Audit v6

Date: 2026-09-18 13:08 (Europe/Moscow)
Scope: bounded read-only audit of upload validation/size handling, cancellation cleanup, queue-vs-worker cancellation races, rollback failure behavior, README claims, and related tests.

## Checked files

- `src/TeyPdfCad.Web.Api/Program.cs`
- `src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs`
- `src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs`
- `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs`
- `README.md`

Source and tests were not changed. Runtime and build/test commands were not run.

## Input validation and HTTP semantics

### PASS: application upload size limit and 413 paths

`TEYPDFCAD_MAX_UPLOAD_BYTES` is validated as positive and applied to multipart parsing with a one-megabyte envelope (`src/TeyPdfCad.Web.Api/Program.cs:5-10`). `ReadFormAsync` `InvalidDataException` is mapped to `413 multipart_body_too_large` (`src/TeyPdfCad.Web.Api/Program.cs:168-177`). After parsing, a file larger than the configured limit is mapped to `413 pdf_too_large` with `maxBytes` (`src/TeyPdfCad.Web.Api/Program.cs:178-183`).

### PASS: 400 validation paths

The endpoint rejects non-form requests with `400 multipart_form_data_required`, missing/empty files with `400 pdf_file_required`, invalid PDF signature with `400 pdf_signature_invalid`, missing pipeline version with `400`, and invalid settings with `400` (`src/TeyPdfCad.Web.Api/Program.cs:165-197`). The signature check searches the first 1024 bytes for `%PDF-` and uses the request cancellation token (`src/TeyPdfCad.Web.Api/Program.cs:410-421`).

### P3: form-content error classification is broader than the message

The endpoint checks `request.HasFormContentType` rather than requiring `multipart/form-data` (`src/TeyPdfCad.Web.Api/Program.cs:163-166`). `application/x-www-form-urlencoded` is also a form content type, so it can reach `ReadFormAsync` and return `400 pdf_file_required` instead of `400 multipart_form_data_required`. The HTTP status remains 400, but the error classification does not precisely match the endpoint contract.

### Documentation/test gap

README documents worker, retention, metadata, and queue settings (`README.md:31-56`) but does not document `TEYPDFCAD_MAX_UPLOAD_BYTES`, `400` upload validation errors, or the two `413` cases. The reviewed test file covers queue/registry/storage behavior but has no API upload/cancel/413 tests (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:1-245`).

## Cancel queued versus worker dequeue

### PASS: compare-and-swap prevents both sides from owning a queued job

`TryCancel` only changes `Queued` to `Cancelled` (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:84-90`). `DequeueAsync` changes `Queued` to `Processing` using the opposite compare-and-swap (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:45-60`). If cancellation wins, the channel tombstone is skipped when dequeue sees `Cancelled`; if dequeue wins, cancellation returns `false` and the API returns `409 job_not_cancellable` (`src/TeyPdfCad.Web.Api/Program.cs:328-358`). There is no source-level double ownership race in this transition.

### PASS: cancelled input cleanup is best effort with retention fallback

After a successful queued cancellation, the API marks the job terminal and attempts to delete `jobs/{jobId}/input.pdf` with a non-cancelled token (`src/TeyPdfCad.Web.Api/Program.cs:338-345`). Any deletion exception is logged and the endpoint still returns `200 cancelled`, explicitly stating that retention will retry (`src/TeyPdfCad.Web.Api/Program.cs:346-350`). `FileSystemArtifactStore.DeleteAsync` itself checks cancellation, resolves the safe path, and returns false when the file is already absent (`src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs:74-85`).

## Upload rollback when artifact deletion fails

### P2: upload rollback can mask the original outcome and leave the input until retention

The cancel endpoint protects its delete call, but upload rollback does not. In the JSON path, capacity handling removes the registry entry and returns 429 without artifact work (`src/TeyPdfCad.Web.Api/Program.cs:120-125`), while the multipart capacity path calls `DeleteAsync` directly (`src/TeyPdfCad.Web.Api/Program.cs:240-247`). The multipart duplicate path also calls it directly (`src/TeyPdfCad.Web.Api/Program.cs:250-260`), and the general multipart catch block calls it directly while rethrowing (`src/TeyPdfCad.Web.Api/Program.cs:265-270`).

If one of those `DeleteAsync` calls throws, the intended `429` or deduplicated `200` response can become a storage exception response, and the original queue/cancellation exception can be masked. The registry has already been removed in the capacity/duplicate branches, while the input artifact may remain until retention. This is a rollback robustness finding; the existing retention policy provides eventual cleanup but does not preserve the original HTTP outcome.

## Validation boundary

This is a source-only audit of the requested Program upload/cancel paths, queue transition semantics, artifact delete behavior, README claims, and the listed Web tests. It does not certify Kestrel/proxy request limits, multipart parser behavior outside the configured `FormOptions`, concurrent HTTP timing under load, filesystem fault injection, or production runtime behavior. No source/test edits, build, tests, or runtime execution were performed.
