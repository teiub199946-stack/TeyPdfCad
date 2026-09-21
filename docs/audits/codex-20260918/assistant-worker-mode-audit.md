# Worker Mode Audit

Date: 2026-09-18

## Scope

Read-only review of current worker mode selection, readiness, AutoCAD process bridge, in-memory worker, output validation, upload boundary, and tests. No source files or tests were changed. No build or runtime test was run.

## Closed findings

- JobId is restricted to safe characters and length, with bridge temp-root containment (`src/TeyPdfCad.Web/Jobs/ConversionJob.cs:20-26`; `src/TeyPdfCad.Web/Jobs/AutoCadProcessHostBridge.cs:25-32`). Unit coverage: `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:30-37`.
- Invalid/zero/negative AutoCAD timeout values fail configuration (`src/TeyPdfCad.Web/Jobs/AutoCadHostBridgeOptions.cs:15-24`). Unit coverage: `tests/TeyPdfCad.Web.Tests/ConversionJobTests.cs:39-44`.
- Bridge stdout/stderr retention is capped to 16 KiB per stream (`src/TeyPdfCad.Web/Jobs/AutoCadProcessHostBridge.cs:75-78`, `113-131`).
- Process request carries `--protocol-version 1`; output must have a minimal DWG header (`AutoCadProcessHostBridge.cs:58-67`, `98-143`).
- `/health` and `/ready` now return degraded `503` when autocad bridge is not configured (`src/TeyPdfCad.Web.Api/Program.cs:13-53`).
- JSON `/jobs` is rejected in autocad mode with `pdf_upload_required` (`Program.cs:62-65`).
- Multipart settings are parsed into `ConversionSettings` (`Program.cs:147-164`, `308-328`).
- API DI constructs `InMemoryConversionWorker` with the artifact store; the fake writes a deterministic `AC1032` fixture (`Program.cs:30-35`; `src/TeyPdfCad.Web/Jobs/InMemoryConversionWorker.cs:24-31`).

## Remaining findings

### P1 - The configured AutoCAD bridge is still a protocol parser stub

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD.Bridge\Program.cs:3-15`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadProcessHostBridge.cs:49-107`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:21-29`
- **Reason:** `TeyPdfCad.AutoCAD.Bridge` parses the protocol, prints that the AutoCAD 2022 runtime executor is not implemented, and exits with code `20`. The web process can launch it, but no real PDFIMPORT/AutoCAD/DWG conversion can succeed.
- **Required evidence:** a real versioned bridge executable and AutoCAD 2022 smoke evidence covering PDFIMPORT, native measurement validation, DWG save, exit codes, timeout, and cancellation.

### P2 - DWG validation is only a six-byte header check

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadProcessHostBridge.cs:98-143`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadConversionWorker.cs:38-40`
- **Reason:** Any file beginning with `AC10` plus two digits is accepted, stored, and published as `Completed`; no DWG openability/schema or AutoCAD-native dimension proof is performed at this boundary.
- **Required evidence:** open/validate the produced DWG through AutoCAD or a trusted DWG parser before publishing `Completed`.

### P2 - Terminal artifact retention is still undefined

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:186-205`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\ConversionArtifactKeys.cs:3-7`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\IConversionArtifactStore.cs:14-16`
- **Reason:** `DeleteAsync` is used for enqueue rollback only. Successful, failed, and cancelled jobs retain input/output artifacts indefinitely; no retention worker or documented download window exists.

## Runtime-tested boundary

Not runtime-tested in this audit:

- AutoCAD 2022 process launch and PDFIMPORT execution;
- bridge protocol success path and real DWG output;
- timeout/process-tree cancellation cleanup;
- API upload -> worker -> result download;
- readiness behavior under actual host startup;
- artifact retention/cleanup.

Existing tests cover queue lifecycle, safe JobId rejection, timeout parsing, in-memory result metadata, and basic artifact store round-trip. No process-bridge or API endpoint integration test was found.
