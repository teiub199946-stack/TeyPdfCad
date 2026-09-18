# Input and Cancellation Audit v7

Date: 2026-09-18 13:23 (Europe/Moscow)
Scope: bounded read-only follow-up of the latest upload and cancellation fixes.

## Checked files

- `src/TeyPdfCad.Web.Api/Program.cs`
- `src/TeyPdfCad.Web/Storage/IConversionArtifactStore.cs`
- `src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs`
- `README.md`
- `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs`

Source and tests were not changed. Runtime, build, and test commands were not run.

## Findings and verification

### Strict multipart check: PASS for the intended media type

The upload endpoint now rejects requests unless they are form content and the content type starts with `multipart/form-data` using ordinal case-insensitive comparison (`src/TeyPdfCad.Web.Api/Program.cs:163-168`). This closes the previous acceptance of URL-encoded form content as a multipart upload. Missing/empty files remain a 400, and malformed form bodies are handled separately.

### Upload/form limits and PDF signature: PASS

`TEYPDFCAD_MAX_UPLOAD_BYTES` is validated as positive and applied to `FormOptions.MultipartBodyLengthLimit` with a one-megabyte multipart envelope (`src/TeyPdfCad.Web.Api/Program.cs:5-10`). `ReadFormAsync` `InvalidDataException` maps to `413 multipart_body_too_large`, while a parsed PDF above the configured file limit maps to `413 pdf_too_large` (`src/TeyPdfCad.Web.Api/Program.cs:170-185`). The first 1024 bytes are scanned for `%PDF-` with the request cancellation token; a missing signature returns `400 pdf_signature_invalid` (`src/TeyPdfCad.Web.Api/Program.cs:187-191`, `:409-421`).

### Rollback no longer masks the original error: PASS

Capacity, duplicate, and general upload rollback paths now call the common helper (`src/TeyPdfCad.Web.Api/Program.cs:243-269`). `TryDeleteInputArtifactAsync` catches cleanup exceptions, logs a warning, and returns normally so the caller can preserve its intended 429, deduplicated 200, or original exception (`src/TeyPdfCad.Web.Api/Program.cs:423-437`). The helper uses `CancellationToken.None`, so request cancellation cannot abort the cleanup attempt itself.

The cancel endpoint has equivalent best-effort handling: it marks the queued job terminal, attempts input deletion, logs any exception, and still returns `200 cancelled` (`src/TeyPdfCad.Web.Api/Program.cs:327-350`).

### DI and null-safety: no finding

The API registers one singleton `IConversionArtifactStore` and resolves `IConversionArtifactRetention` from that same `FileSystemArtifactStore` instance (`src/TeyPdfCad.Web.Api/Program.cs:17-25`). The upload and cancel handlers receive `IConversionArtifactStore`, and the helper receives the resolved artifact store plus the injected logger (`src/TeyPdfCad.Web.Api/Program.cs:154-161`, `:327-333`, `:423-426`). No reviewed path dereferences an optional artifact dependency, and no NRE/DI mismatch was found in source inspection.

`FileSystemArtifactStore.DeleteAsync` validates cancellation, resolves a confined path, returns `false` when the target is already absent, and only then deletes the file (`src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs:74-85`).

### README/runtime agreement: PASS

README documents `TEYPDFCAD_MAX_UPLOAD_BYTES` (`README.md:31-43`) and states the current runtime contract: multipart/form-data only, `%PDF-` within the first 1024 bytes, 400 for invalid form/signature, 413 for oversized input, best-effort logged rollback, and retention fallback for abandoned artifacts (`README.md:49-59`). These statements match the inspected `Program.cs` behavior.

## Remaining observations

### P3: `StartsWith("multipart/form-data")` is permissive for malformed suffixes

The strict check uses a prefix comparison (`src/TeyPdfCad.Web.Api/Program.cs:166-168`). A syntactically malformed content type such as `multipart/form-data-invalid` also passes this first check and is then delegated to `ReadFormAsync`. Framework parsing should reject it, but the application-level guard is not a full media-type parser. This is a validation-hardening observation, not a confirmed accepted-upload bug.

### Test coverage gap

The reviewed Web test file contains queue, registry, runner, and artifact-store tests but no API endpoint tests for strict content type, 413 mapping, PDF signature rejection, rollback delete failure, or queued cancellation (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:1-245`). This report therefore provides source evidence only, not runtime confirmation.

## Validation boundary

This is a source-only audit of upload input validation, cancellation cleanup, rollback exception isolation, DI registration, README alignment, and the listed Web tests. It does not certify Kestrel/proxy request limits, framework multipart parser behavior, concurrent HTTP timing, filesystem fault injection, or production runtime behavior. No source/test edits, build, tests, or runtime execution were performed.
