# API worker lifecycle

## Changed files

- `src/TeyPdfCad.Web.Api/ConversionJobWorkerHostedService.cs`
  - Adds an ASP.NET Core `BackgroundService` loop.
  - Repeatedly calls the existing `ConversionJobRunner.RunOnceAsync`.
  - Passes the host cancellation token through dequeue and conversion.
  - Stops cleanly on host cancellation.
  - Logs unexpected loop errors and waits briefly before retrying, so one dequeue/lifecycle failure does not terminate the hosted service.
- `src/TeyPdfCad.Web.Api/Program.cs`
  - Registers singleton `IConversionJobQueue` with `InMemoryConversionJobQueue`.
  - Registers singleton `IConversionWorker` with `InMemoryConversionWorker`.
  - Registers singleton `ConversionJobRunner` and `ConversionJobWorkerHostedService`.
  - Injects the shared queue into the existing job endpoints.

## Check

Command:

```text
dotnet build src\\TeyPdfCad.Web.Api\\TeyPdfCad.Web.Api.csproj --no-restore --verbosity minimal
```

Result: **passed**, 0 warnings and 0 errors. Both `TeyPdfCad.Web` and `TeyPdfCad.Web.Api` compiled successfully.

## Scope

No files under `src/TeyPdfCad.Web/Jobs`, tests, Core, AutoCAD, or `docs/PROJECT_STATE.md` were changed. The API still uses process-local in-memory state and the current `InMemoryConversionWorker`; jobs are completed by the hosted loop and are not persisted across process restarts.
