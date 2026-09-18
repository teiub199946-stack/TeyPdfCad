# Input and Cancellation Audit v9: Test Coverage Review

Date: 2026-09-18 13:48 (Europe/Moscow)
Scope: bounded read-only review of the requested upload/cancellation tests and their asserted contracts.

## Checked files

- `tests/TeyPdfCad.Web.Tests/TeyPdfCad.Web.Tests.csproj`
- `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs`
- `src/TeyPdfCad.Web.Api/Program.cs`
- `README.md`

Source and tests were not changed. Runtime, build, and test commands were not run.

## Test project boundary

The test project targets `net8.0`, uses xUnit packages, and references only `src/TeyPdfCad.Web/TeyPdfCad.Web.csproj` (`tests/TeyPdfCad.Web.Tests/TeyPdfCad.Web.Tests.csproj:1-15`). It does not reference `TeyPdfCad.Web.Api`, use `WebApplicationFactory`/`TestServer`, or construct an `HttpClient` against the endpoint handlers. The reviewed test directory contains one test source file, `ConversionJobTests.cs`.

## Coverage that is present

- Idempotency key stability and pipeline/settings validation are asserted (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:12-58`).
- Queue duplicate handling and lifecycle status transitions are asserted (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:90-109`).
- Queued cancellation tombstones are skipped by dequeue (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:111-123`).
- Capacity exhaustion returns `CapacityExceeded` and does not retain the rejected queue item (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:125-135`).
- Terminal queue/registry/result retention and processing preservation are asserted (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:137-201`).
- Artifact path confinement, round-trip content, and job-scoped retention are asserted (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:217-259`).

These tests are useful unit checks for the Web domain layer, but they do not execute the top-level API handlers.

## Findings

### P2: latest upload and cancellation contracts remain untested at the HTTP boundary

No test asserts the new `MediaTypeHeaderValue.TryParse` guard or valid boundary acceptance (`src/TeyPdfCad.Web.Api/Program.cs:163-169`). No test asserts `400 multipart_form_data_required`, `413 multipart_body_too_large`, `413 pdf_too_large`, or `400 pdf_signature_invalid` (`src/TeyPdfCad.Web.Api/Program.cs:170-192`). No test injects an artifact store whose `DeleteAsync` throws to prove `TryDeleteInputArtifactAsync` preserves the original 429/deduplication/exception outcome (`src/TeyPdfCad.Web.Api/Program.cs:243-269`, `:424-438`). No test drives `POST /jobs/{jobId}/cancel` through queued success, processing conflict, or delete-failure fallback (`src/TeyPdfCad.Web.Api/Program.cs:327-357`).

Because the project does not reference the API project or use an HTTP test host (`tests/TeyPdfCad.Web.Tests/TeyPdfCad.Web.Tests.csproj:8-15`), these contracts currently have source evidence only. A regression in endpoint binding, DI, status mapping, or exception handling could pass all existing tests.

### P2: fail-fast timing is still not independently asserted

`Queue_ReturnsCapacityExceededWithoutWaiting` checks only the enum and cleanup (`tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:125-135`). It does not measure a bounded completion time. The implementation's `TryWrite` is direct source evidence of a nonblocking path (`src/TeyPdfCad.Web/Jobs/InMemoryConversionJobQueue.cs:35-42`), but the test name's “WithoutWaiting” claim is not independently proven by the test itself.

## Contract alignment

README states the intended upload and cancellation contract (`README.md:49-59`), and the current `Program.cs` source matches those statements. The gap is evidence quality, not a source/README contradiction.

## Validation boundary

This is a source-only test-coverage audit. It does not claim that existing tests pass, because no test command was run and package restore/runtime prerequisites were not verified in this bounded task. It does not certify Kestrel multipart parsing, endpoint DI, HTTP status serialization, concurrent cancellation timing, or production readiness. No source/test edits, build, tests, or runtime execution were performed.
