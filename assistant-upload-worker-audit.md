# Upload and Worker Audit

Date: 2026-09-18

## Scope

Read-only review of current upload lifecycle, worker mode readiness, artifact publication, multipart settings, cleanup, and tests. No source files or tests were changed. No build or runtime test was run.

## Closed findings

- Upload rollback deletes `jobs/{jobId}/input.pdf` when storing or enqueueing fails (`src/TeyPdfCad.Web.Api/Program.cs:186-205`).
- Artifact storage provides root-confined `DeleteAsync` (`src/TeyPdfCad.Web/Storage/IConversionArtifactStore.cs:14-16`; `FileSystemArtifactStore.cs:74-100`).
- Failure metadata is returned by status/result endpoints (`Program.cs:214-260`, `283-294`).
- `/health` and `/ready` return degraded `503` when autocad bridge configuration is missing (`Program.cs:13-53`).
- JSON `/jobs` is rejected in autocad mode because it has no uploaded PDF (`Program.cs:62-65`).
- Multipart upload now parses `outputUnits`, `preserveSourceGeometry`, and `recognizerProfile` (`Program.cs:147-164`, `308-328`).
- Explicit in-memory mode writes a deterministic fake DWG artifact through the artifact store (`src/TeyPdfCad.Web/Jobs/InMemoryConversionWorker.cs:24-31`).

## Remaining findings

### P1 - No real AutoCAD conversion is available from the configured bridge

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD.Bridge\Program.cs:3-15`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadProcessHostBridge.cs:49-107`
- **Reason:** The bridge executable only validates CLI arguments, reports that the AutoCAD 2022 runtime executor is not implemented, and exits with code `20`. Upload can now create the input artifact correctly, but the AutoCAD worker still cannot produce a real DWG.

### P2 - DWG output validation is not sufficient for artifact publication

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadProcessHostBridge.cs:98-143`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\AutoCadConversionWorker.cs:38-40`
- **Reason:** A six-byte `AC10xx` header is enough to pass the bridge check. There is no DWG openability/schema or AutoCAD-native measurement validation before the output is stored and reported as `Completed`.

### P2 - Terminal artifacts have no retention or cleanup policy

- **Absolute paths/lines:**
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:186-205`
  - `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\ConversionArtifactKeys.cs:3-7`
- **Reason:** Rollback deletion is implemented, but successful, failed, and cancelled jobs retain input/output artifacts indefinitely. The process-local queue and result registry also retain terminal metadata until process exit.

## Runtime-tested boundary

Not runtime-tested in this audit:

- AutoCAD 2022 PDFIMPORT and native dimension conversion;
- bridge exit code `20`/success protocol behavior through the API;
- real output DWG download;
- upload settings propagation through a live HTTP request;
- terminal artifact retention/cleanup.

Current tests cover queue lifecycle, safe JobId rejection, timeout parsing, in-memory result metadata, and basic artifact store round-trip. No API endpoint or process bridge integration test was found.
