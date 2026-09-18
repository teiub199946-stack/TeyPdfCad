# Conversion worker service review

## Result

Implementation was blocked by the current project reference boundary. `TeyPdfCad.Web` is an ordinary `Microsoft.NET.Sdk` `net8.0` class library and does not reference `Microsoft.Extensions.Hosting`. A new `ConversionJobWorkerService : BackgroundService` file therefore cannot compile under the requested "new files inside Jobs only" constraint.

The attempted service file was removed so the project is not left with uncompilable production code. No existing queue, worker, runner, Core, AutoCAD, API, or documentation file was changed.

## Compatibility check

Existing contracts are sufficient for the lifecycle loop:

- `ConversionJobRunner.RunOnceAsync(CancellationToken)` already dequeues a job, invokes `IConversionWorker`, validates a terminal result, completes the queue status, and handles cancellation/worker exceptions.
- A background loop would only need to call `RunOnceAsync` repeatedly and stop on the host cancellation token.

The missing dependency is the hosting base type, not a queue or worker contract.

## Build check

Command:

```text
dotnet build src\\TeyPdfCad.Web\\TeyPdfCad.Web.csproj --no-restore --verbosity minimal
```

The compatibility attempt failed with:

- `CS0234`: `Microsoft.Extensions` does not exist in namespace `Microsoft`.
- `CS0246`: `BackgroundService` could not be found.

After removing the attempted file, no final build was run because no source change remained to validate.

## Required follow-up

To implement a real ASP.NET Core hosted service, the project must receive a framework reference to `Microsoft.AspNetCore.App` or a package/reference to `Microsoft.Extensions.Hosting.Abstractions`. That requires changing the existing `src/TeyPdfCad.Web/TeyPdfCad.Web.csproj`, or moving the service into a host project that already has the shared framework reference. Both options are outside the current instruction boundary.
