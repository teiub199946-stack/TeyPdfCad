# Input and Cancellation Audit v8

Date: 2026-09-18 13:34 (Europe/Moscow)
Scope: bounded read-only follow-up of strict multipart parsing and the latest input/cancellation hardening.

## Checked files

- `src/TeyPdfCad.Web.Api/Program.cs`
- `src/TeyPdfCad.Web/Storage/IConversionArtifactStore.cs`
- `src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs`
- `README.md`
- `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs`

Source and tests were not changed. Runtime, build, and test commands were not run.

## Findings and verification

### Strict multipart media-type parsing: PASS

The upload endpoint now parses `Content-Type` with `Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse` and compares the parsed media type to `multipart/form-data` (`src/TeyPdfCad.Web.Api/Program.cs:163-169`). This accepts valid boundary parameters while rejecting URL-encoded forms and malformed media-type suffixes at the application guard. The previous prefix-comparison P3 is closed.

### Upload limits and signature validation: PASS

The configured max upload size is validated and applied to multipart parsing with a one-megabyte envelope (`src/TeyPdfCad.Web.Api/Program.cs:5-10`). Body parse overflow maps to `413 multipart_body_too_large`; a parsed file above the configured limit maps to `413 pdf_too_large` (`src/TeyPdfCad.Web.Api/Program.cs:170-186`). The first 1024 bytes are scanned for `%PDF-`, with invalid signatures returning `400 pdf_signature_invalid` (`src/TeyPdfCad.Web.Api/Program.cs:188-192`, `:409-421`).

### Rollback exception isolation: PASS

Capacity, duplicate, and general upload rollback paths use `TryDeleteInputArtifactAsync` (`src/TeyPdfCad.Web.Api/Program.cs:243-269`). The helper catches cleanup exceptions, logs them, and returns normally (`src/TeyPdfCad.Web.Api/Program.cs:424-438`), preserving the original 429/deduplication/error outcome while retention remains responsible for abandoned artifacts. The cancel endpoint separately follows the same best-effort policy (`src/TeyPdfCad.Web.Api/Program.cs:327-350`).

### DI and artifact-store contract: PASS

One singleton artifact store is registered and reused for both `IConversionArtifactStore` and retention (`src/TeyPdfCad.Web.Api/Program.cs:17-25`). `DeleteAsync` is null-safe at the call boundary through DI, checks cancellation, confines the path, and treats an absent file as a non-error (`src/TeyPdfCad.Web/Storage/FileSystemArtifactStore.cs:74-85`). No NRE or service-registration mismatch was found in source inspection.

### README alignment: PASS

README now documents the upload size setting and the current multipart/signature/400/413/rollback/cancel contract (`README.md:31-43`, `:49-59`). The text matches the inspected runtime paths, including best-effort rollback and retention fallback.

## Remaining validation gap

The Web test project still contains queue, registry, runner, and artifact-store tests but no endpoint-level tests for parsed media-type rejection, valid multipart boundary acceptance, 400/413 mapping, PDF signature rejection, rollback delete failure, or queued cancellation (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:1-267`). This is a test/evidence gap, not a newly confirmed source defect.

## Validation boundary

This is a source-only audit of strict multipart parsing, upload limits/signature checks, rollback exception isolation, cancellation cleanup, DI registration, README alignment, and the listed Web tests. It does not certify Kestrel/proxy limits, framework parser behavior under malformed wire data, concurrent HTTP timing, filesystem fault injection, or production runtime behavior. No source/test edits, build, tests, or runtime execution were performed.
