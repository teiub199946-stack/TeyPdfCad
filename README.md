# TeyPdfCad

Precision vector PDF -> DWG semantic reconstruction engine.

## Primary goal
Turn vector engineering PDFs into editable DWG while reconstructing native CAD semantics instead of returning exploded geometry.

V0.1 success criterion: a PDF dimension such as `5200` is reconstructed as a native AutoCAD `DIMENSION` with the correct definition points and measurement.

## Scope for the first sprint
- Vector PDF only; raster/scanned PDFs are out of scope.
- Geometry primitives: line, polyline, arc, circle.
- Text primitives.
- Scale estimation.
- Native linear/aligned/rotated dimension reconstruction.
- Confidence scoring and mathematical validation.
- Automated synthetic/regression tests.

## Architecture
- `TeyPdfCad.Core` — CAD-neutral semantic engine.
- `TeyPdfCad.AutoCAD` — temporary AutoCAD adapter for PDFIMPORT input and native DWG output.
- `TeyPdfCad.Tests` — unit/regression tests for the core.
- `TeyPdfCad.TestGenerator` — synthetic dimension/golden corpus generator.

The Core must never depend on AutoCAD assemblies. The AutoCAD adapter is replaceable later by a direct PDF primitive reader such as PDFium.

`VectorTextRecognizer` is an explicit upstream stage for vector-glyph text. It accepts only registered stroke templates, preserves source provenance, and rejects unknown or ambiguous groups without mutating the source scene. The default template set is intentionally conservative seven-segment control digits; SHX recognition requires a verified template set for the source font.

## Web worker modes

`TeyPdfCad.Web.Api` is fail-closed by default:

```text
TEYPDFCAD_WORKER_MODE=autocad
TEYPDFCAD_AUTOCAD_BRIDGE_EXE=C:\path\to\verified-autocad-bridge.exe
TEYPDFCAD_AUTOCAD_TIMEOUT_SECONDS=600
TEYPDFCAD_AUTOCAD_CORE_CONSOLE=C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe
TEYPDFCAD_AUTOCAD_PLUGIN_DLL=C:\path\to\TeyPdfCad.AutoCAD.dll
TEYPDFCAD_AUTOCAD_BASE_DWG=C:\path\to\base.dwg
TEYPDFCAD_ARTIFACT_RETENTION_HOURS=24
TEYPDFCAD_RETENTION_SWEEP_MINUTES=5
TEYPDFCAD_MAX_JOB_METADATA=10000
TEYPDFCAD_QUEUE_CAPACITY=128
TEYPDFCAD_MAX_UPLOAD_BYTES=536870912
```

The bridge protocol is version `1` and receives the PDF and job settings as command-line arguments. It must write a valid DWG whose first six bytes match an `AC10xx` DWG header. The API also requires the AutoCAD 2022 runtime gate with the real control PDF before this mode is accepted for production.

`TEYPDFCAD_WORKER_MODE=inmemory` is reserved for local lifecycle smoke tests. It creates only a deterministic DWG-header smoke artifact, not a converted drawing, and must not be used as a production conversion mode.

The API exposes `/ready` separately from `/health`. In AutoCAD mode, both endpoints return `503` and a specific readiness error until the bridge executable, `accoreconsole.exe`, and the plugin DLL are all configured and present. Job submission is rejected while readiness is degraded. JSON `POST /jobs` is metadata-only and is therefore accepted only in `inmemory` mode; AutoCAD mode requires `POST /jobs/upload` so the PDF input artifact exists.

AutoCAD readiness requires `acad2022.cfg` beside the configured Core Console
executable and a trusted base DWG/DWT path. The bridge copies that base drawing
into a fresh isolated job directory before launching Core Console.

The bounded queue uses fail-fast backpressure. When `TEYPDFCAD_QUEUE_CAPACITY` pending jobs are already buffered, new submissions receive `429 queue_full` instead of holding an HTTP request open while waiting for a worker.

Uploads must use `multipart/form-data`, are limited by `TEYPDFCAD_MAX_UPLOAD_BYTES`, and must contain a `%PDF-` signature in the first 1024 bytes. Invalid form/signatures return `400`; oversized uploads return `413`. Upload rollback is best effort and logged, so a cleanup failure does not mask the original queue or request error. Cancelling a queued job removes its stored input PDF immediately, while retention remains the fallback for abandoned artifacts.

`pipelineVersion` is limited to 128 characters, `outputUnits` to 32, and `recognizerProfile` to 64. Control characters in conversion settings are rejected before idempotency hashing.

Multipart uploads accept optional `outputUnits`, `preserveSourceGeometry`, and `recognizerProfile` fields. The AutoCAD bridge supports pipeline `1`/`v1`, recognizer profiles `default`/`strict`, and output units `drawing`, `mm`, `cm`, `m`, `in`, or `ft`; unsupported values fail closed instead of silently using defaults. When `preserveSourceGeometry=false`, PDFIMPORT source entities are erased only after native dimension validation succeeds. In-memory mode writes a small valid DWG-header smoke artifact so result download can be tested without AutoCAD.

Job metadata and terminal results use the same retention window as artifacts. Queued and processing jobs are retained; completed, failed, and cancelled records are removed after the window, and subsequent status/result requests return `404`. The in-memory job registry is bounded by `TEYPDFCAD_MAX_JOB_METADATA`; when it is full, new submissions fail closed with `503` until retention frees capacity. Queue tombstones are removed with the same policy, including cancelled jobs that were still waiting in the channel.

Artifact and metadata cleanup run from one coordinator using `TEYPDFCAD_RETENTION_SWEEP_MINUTES` (default `5`), so the retention boundary is eventual within the configured sweep interval rather than an exact deadline.

The bridge executable now runs the configured AutoCAD Core Console with an isolated script, writes a conversion log, and returns distinct exit codes for missing configuration, AutoCAD failure, invalid output, and cancellation. The installed AutoCAD profile must be validated separately; a profile startup failure is reported as a bridge failure and never becomes a successful conversion.

Bridge conversion settings are passed to the plugin through a per-process environment (`pipelineVersion`, `outputUnits`, `preserveSourceGeometry`, `recognizerProfile`, and status-file path). Temporary bridge roots are deleted after completion and retried after cancellation; logs are retained only when `TEYPDFCAD_AUTOCAD_PRESERVE_LOGS=true`.

## Sheet fidelity progress

The Core scene can now carry optional A3 sheet metadata, explicit page-coordinate bounds, and an editable title-block candidate contract. `TitleBlockDetector` fails closed unless it sees both text and at least two source line segments in the candidate region. Unclassified values are serialized as `Unknown` fields with source provenance; the detector does not fabricate drawing numbers, dates, or approvals. The AutoCAD adapter now has a controlled sheet path: when `TEYPDFCAD_SHEET_WIDTH_MM` and `TEYPDFCAD_SHEET_HEIGHT_MM` are explicitly supplied, it can create a named Paper Space layout, editable frame, and named title-block block after native-dimension validation. This environment-driven path is a controlled integration hook, not proof of automatic PDF page extraction; runtime acceptance still requires a real A3 vector PDF and saved/reopened DWG.
