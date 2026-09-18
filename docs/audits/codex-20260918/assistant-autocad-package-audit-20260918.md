# AutoCAD package and manual-test audit

Date: 2026-09-18
Workspace: `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored`
Audit mode: read-only source, package, timestamp, hash, and instruction comparison. No source, test, package, or runtime files were modified.

## Scope checked

- Latest local package: `artifacts\autocad-test\20260918_213131`.
- Package instructions: `artifacts\autocad-test\20260918_213131\TEST_INSTRUCTIONS.md`.
- Package generator: `tools\prepare-autocad-test.ps1`.
- Manual acceptance document: `docs\AUTOCAD_MVP_TEST.md`.
- Bridge execution and status handshake: `src\TeyPdfCad.AutoCAD.Bridge\AutoCadExecutor.cs`, `BridgeRequest.cs`, `BridgeProtocol.cs`.
- Web readiness and host selection: `src\TeyPdfCad.Web\Jobs\AutoCadRuntimeReadiness.cs`, `AutoCadProcessHostBridge.cs`, `UnavailableAutoCadHostBridge.cs`, and `src\TeyPdfCad.Web.Api\Program.cs`.
- Readiness tests: `tests\TeyPdfCad.Web.Tests\AutoCadRuntimeReadinessTests.cs`.

## Findings

### P1 — Latest package is stale relative to the current Core build

The plugin DLL and bridge executable in package `20260918_213131` match their current Release outputs by SHA-256:

- `plugin\TeyPdfCad.AutoCAD.dll`: `B73ABB0B...7E1413`, package and `src\TeyPdfCad.AutoCAD\bin\Release\net48` equal.
- `plugin\TeyPdfCad.Core.dll`: `55FDF150...9A54C`, package and `src\TeyPdfCad.Core\bin\Release\net48` equal.
- `bridge\TeyPdfCad.AutoCAD.Bridge.exe`: `B2861645...35793`, package and `src\TeyPdfCad.AutoCAD.Bridge\bin\Release\net8.0-windows` equal.

However, the bridge package's bundled Core dependency is older than the current net8 Release Core output:

- package `bridge\TeyPdfCad.Core.dll`: 98,816 bytes, SHA-256 `88C6F969...5DA332`, timestamp `20:44:31`;
- current `src\TeyPdfCad.Core\bin\Release\net8.0\TeyPdfCad.Core.dll`: 99,328 bytes, SHA-256 `5BD6A61E...5BC88`, timestamp `21:32:52`.

Therefore the package cannot be treated as a complete fresh package after the latest Core build. Rebuild the bridge after the final Core source changes and regenerate the package before runtime acceptance.

### P1 — Runtime fallback is fail-closed, not a working conversion fallback

The installed AutoCAD paths exist for `acad.exe`, `accoreconsole.exe`, and the package plugin, but `C:\Program Files\Autodesk\AutoCAD 2022\acad2022.cfg` is absent. The current readiness implementation therefore returns `autocad_configuration_not_found` (`src\TeyPdfCad.Web\Jobs\AutoCadRuntimeReadiness.cs:23-28`). The API exposes this as degraded `503` for `/health` and `/ready` and rejects job submission while not ready (`src\TeyPdfCad.Web.Api\Program.cs:30-34,66-72,81-84,163-164`).

The manual smoke script and batch runner also stop before Core Console when the same configuration file is missing (`tools\autocad-plugin-smoke.ps1:14-17`, `tools\autocad-batch-run.ps1:20-22`). This is consistent and safe, but it is not evidence of a fallback connection or successful conversion. The only explicit host fallback is `UnavailableAutoCadHostBridge`, which throws a configuration error when no bridge executable is configured (`src\TeyPdfCad.Web.Api\Program.cs:35-47`, `src\TeyPdfCad.Web\Jobs\UnavailableAutoCadHostBridge.cs:3-15`). The in-memory worker is selected only when `TEYPDFCAD_WORKER_MODE=inmemory` (`src\TeyPdfCad.Web.Api\Program.cs:26,50-53`) and does not provide AutoCAD conversion.

The bridge code does have a profile fallback: unnamed profile values are ignored and Core Console starts with its default profile (`src\TeyPdfCad.AutoCAD.Bridge\AutoCadExecutor.cs:66-76,209-217`). This path is source-verified only; the missing configuration prevented runtime proof.

### P2 — Package instructions are behind the current test procedure

The package instruction says to run `TEYPDFDUMPALL` and `TEYPDFRECONSTRUCTALL` directly after import and only mentions entering A3 bounds if process variables were not set (`artifacts\autocad-test\20260918_213131\TEST_INSTRUCTIONS.md:14-17`). The current generator requires an explicit measured sheet configuration for the current A3 fixture, including scale `100`, `MinX 2829.645`, and `MinY 2065.104`, before reconstruction (`tools\prepare-autocad-test.ps1:64-79`). The durable manual document separately requires measured bounds before `TEYPDFRECONSTRUCTALL` and records the scale-verification gate (`docs\AUTOCAD_MVP_TEST.md:94-104,131-149`).

The package text also omits the current geometry-only title-block fallback warning and the explicit statement that the process variables do not provide drawing-units-per-millimetre scale, both present in the generator (`tools\prepare-autocad-test.ps1:64-79`). A tester following only the package instructions can run the current A3 flow with the wrong scale or without the required sheet configuration.

### P2 — Package lacks the `BUILD_INFO.txt` listed by the acceptance document

`docs\AUTOCAD_MVP_TEST.md:29-35` lists `BUILD_INFO.txt` as an expected artifact file. The latest local package contains only `TEST_INSTRUCTIONS.md`, the plugin pair, and bridge output files; no `BUILD_INFO.txt` is present. The current package generator copies bridge binaries and writes instructions but does not create `BUILD_INFO.txt` (`tools\prepare-autocad-test.ps1:43-84`). This is a packaging/documentation contract mismatch, not evidence of a runtime defect.

## Consistency results

- Input/output and bridge protocol validation are consistent with the current bridge source: strict argument pairs, protocol `1`, distinct `.pdf`/`.dwg` paths, status-file `ok` handshake, and DWG header validation (`BridgeRequest.cs:24-74`, `BridgeProtocol.cs:5-42`, `AutoCadExecutor.cs:108-120`).
- Cleanup and cancellation behavior is present in the current source (`AutoCadExecutor.cs:124-142,170-207`, `AutoCadProcessHostBridge.cs:79-101,145-184`), but the local package audit does not provide runtime cancellation evidence.
- The package points to existing fixture and executable paths, but the configured AutoCAD installation is currently not ready because `acad2022.cfg` is missing.
- No real Core Console conversion, `ok` status file, or independently inspected DWG was produced by this audit.

## Validation boundary

This report verifies package contents, hashes, timestamps, source contracts, and the fail-closed readiness path. It does not certify AutoCAD runtime conversion, native dimension measurements, sheet layout output, or release readiness. Those require a regenerated package, repaired/initialized AutoCAD configuration, Core Console exit code `0`, status file `ok`, and independent inspection of the saved DWG and fixture JSON.
