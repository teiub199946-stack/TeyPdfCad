# AutoCAD Worker Boundary

Date: 2026-09-18

## Scope

Read-only inspection of the current upload/API boundary, `src/TeyPdfCad.AutoCAD`, `src/TeyPdfCad.Core` contracts, artifact storage, and AutoCAD MVP documentation. No source files were changed and no builds were run.

## Input PDF boundary

The current upload endpoint is:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:86-163`

The request must be `multipart/form-data` with a file field named `pdf` (`Program.cs:94-100`) and a non-empty `pipelineVersion` form field (`Program.cs:102-104`). The endpoint computes SHA-256 from the uploaded stream (`Program.cs:106-108`), creates the `ConversionJob` (`Program.cs:110-119`), and stores the original input before queueing:

```text
jobs/{jobId}/input.pdf
```

The key is defined by:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\ConversionArtifactKeys.cs:3-7`
- storage call: `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web.Api\Program.cs:141-143`

The future AutoCAD worker should read this input through `IConversionArtifactStore.OpenReadAsync`:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\IConversionArtifactStore.cs:3-12`

`FileSystemArtifactStore` resolves relative keys below its configured root and rejects rooted/path-traversal keys (`C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Storage\FileSystemArtifactStore.cs:74-87`). It writes through a temporary file and atomic move (`FileSystemArtifactStore.cs:29-51`) and enforces a maximum content size (`FileSystemArtifactStore.cs:90-102`).

The upload currently stores the input before queue enqueue (`Program.cs:141-149`). If enqueue fails, there is no delete operation in `IConversionArtifactStore`, so an orphaned `input.pdf` can remain. This should be handled by a future transactional store or cleanup policy.

## Current AutoCAD command boundary

The AutoCAD adapter exposes command methods, not a callable PDF-to-DWG service:

- `TEYPDFPING`: `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\ReconstructionCommands.cs:15-20`
- `TEYPDFANALYZE`: `ReconstructionCommands.cs:22-39`
- `TEYPDFDUMP`: `ReconstructionCommands.cs:41-79`
- `TEYPDFRECONSTRUCT`: `ReconstructionCommands.cs:81-148`

Both dump and reconstruct obtain `Application.DocumentManager.MdiActiveDocument` and ask the user for a selection (`ReconstructionCommands.cs:44-50`, `84-90`, `150-162`). The current code therefore requires an AutoCAD UI document containing objects already imported by `PDFIMPORT`; it cannot consume `jobs/{jobId}/input.pdf` directly from the web worker.

`TEYPDFDUMP` reads the selected entities inside a transaction, formats the exact `PrimitiveScene`, and writes a deterministic JSON fixture under `%TEMP%\\TeyPdfCad\\` (`ReconstructionCommands.cs:52-78`). It is read-only and does not produce a DWG artifact.

`TEYPDFRECONSTRUCT` reads the selected PDFIMPORT entities, analyzes them, rejects zero dimensions and multiple scale groups, applies the scale transform, writes native dimensions, validates native measurements, and commits only after validation (`ReconstructionCommands.cs:94-147`). A failed validation returns before `transaction.Commit()` and leaves the drawing unchanged.

There is no existing method that accepts a PDF path/stream, creates an isolated drawing, runs PDFIMPORT, invokes the semantic pipeline, saves a DWG, and returns an artifact path. A future worker must use a verified AutoCAD-host bridge or an AutoCAD automation process; it must not invoke the interactive command methods from the web process.

## Core contracts available to the adapter

The Core input contract is a CAD-neutral `PrimitiveScene` containing line and text primitives:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Core\Primitives\PrimitiveScene.cs:3-7`

The semantic entry point is:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Core\Recognition\SemanticReconstructionEngine.cs:7-27`
- It accepts `PrimitiveScene` and returns `SemanticReconstructionResult` with dimensions, chains, dominant scale, and average confidence (`C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Core\Semantics\SemanticReconstructionResult.cs:5-17`).

The current AutoCAD reader is internal and transaction-bound:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\AutoCadPrimitiveReader.cs:7-53`
- It reads selected `Line`, `Polyline`, `DBText`, and `MText` entities from an AutoCAD `Transaction`; it does not read a PDF file directly.

The native output writer and validator are also internal transaction-bound components:

- writer: `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\NativeDimensionWriter.cs:8-54`
- validator: `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\NativeDimensionValidator.cs:6-39`

The validator compares `Dimension.Measurement` with the semantic displayed value and rejects NaN, infinity, or relative error above its default tolerance (`NativeDimensionValidator.cs:24-35`).

## Minimal future `IConversionWorker` boundary

The existing web contract is:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\IConversionWorker.cs:3-7`

```csharp
Task<ConversionResult> ConvertAsync(
    ConversionJob job,
    CancellationToken cancellationToken = default);
```

The minimal AutoCAD-backed implementation should have these private dependencies and steps:

1. `IConversionArtifactStore` to open `ConversionArtifactKeys.InputPdf(job.JobId)`.
2. A verified AutoCAD host bridge that accepts the input PDF and job settings, runs PDFIMPORT plus the semantic/native validation pipeline, and returns a finalized DWG stream or path.
3. `IConversionArtifactStore.StoreAsync(ConversionArtifactKeys.OutputDwg(job.JobId), dwgStream, cancellationToken)` before returning success. The output key is defined at `ConversionArtifactKeys.cs:7`.
4. Return `ConversionResult.Completed(job.JobId, ConversionArtifactKeys.OutputDwg(job.JobId))` only after the artifact write succeeds.
5. Map cancellation to `ConversionResult.Cancelled(job.JobId)` and host/AutoCAD failures to `ConversionResult.Failed(job.JobId, code, message)`; preserve the existing runner validation and queue lifecycle.

The current `InMemoryConversionWorker` is not an implementation of this boundary: it returns a synthetic output key and does not call the artifact store (`C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.Web\Jobs\InMemoryConversionWorker.cs:3-21`).

## AutoCAD 2022 runtime prerequisites

Required project/runtime target:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\TeyPdfCad.AutoCAD.csproj:3-13`
- target `net48`;
- AutoCAD API package version `24.1.51000`;
- AutoCAD managed assemblies supplied by the AutoCAD host, not copied as application runtime dependencies.

The documented MVP gate requires:

- AutoCAD 2022 release family 24.1;
- .NET Framework 4.8 host;
- vector PDF, not raster/scanned PDF;
- linear/aligned/rotated dimensions;
- one detected drawing-scale group per selected region;
- no automatic vector-glyph/SHX text recognition, multi-scale partitioning, or final source cleanup (`C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\docs\AUTOCAD_MVP_TEST.md:11-19`).

The tested manual runtime sequence is blank drawing, `PDFIMPORT`, preserve imported scale, `NETLOAD`, `TEYPDFPING`, `TEYPDFANALYZE`, select the full imported fragment, then `TEYPDFRECONSTRUCT` (`docs\\AUTOCAD_MVP_TEST.md:57-90`).

The AutoCAD process must have an active document, editor selection context, database transaction support, and permission to create/write DWG and temporary fixture files. The command code explicitly depends on `Application.DocumentManager.MdiActiveDocument` and editor selection (`ReconstructionCommands.cs:18-19`, `150-162`).

Units must remain explicit. `DrawingUnitDiagnostics` reports millimeters only for `INSUNITS=4` and refuses to infer millimeters for undefined/other units:

- `C:\Users\Admin\Documents\ChatGPT\TeyConvert\TeyPdfCad-restored\src\TeyPdfCad.AutoCAD\DrawingUnitDiagnostics.cs:5-25`

## Boundary conclusion

The safe integration point is an AutoCAD-hosted adapter/bridge behind `IConversionWorker`, not a direct call from the ASP.NET process to `ReconstructionCommands`. The adapter must own PDFIMPORT/transaction/document context, preserve the validation and rollback gate, write `jobs/{jobId}/result.dwg` through `IConversionArtifactStore`, and publish `Completed` only after that write succeeds.
