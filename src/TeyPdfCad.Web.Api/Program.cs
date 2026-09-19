using TeyPdfCad.Web.Api;
using TeyPdfCad.Web.Jobs;
using TeyPdfCad.Web.Storage;

var builder = WebApplication.CreateBuilder(args);
var maxUploadBytes = builder.Configuration.GetValue<long>("TEYPDFCAD_MAX_UPLOAD_BYTES", 512L * 1024 * 1024);
if (maxUploadBytes <= 0)
    throw new InvalidOperationException("TEYPDFCAD_MAX_UPLOAD_BYTES must be positive.");
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = checked(maxUploadBytes + 1024 * 1024));
var queueCapacity = builder.Configuration.GetValue("TEYPDFCAD_QUEUE_CAPACITY", 128);
if (queueCapacity <= 0)
    throw new InvalidOperationException("TEYPDFCAD_QUEUE_CAPACITY must be positive.");
builder.Services.AddSingleton<IConversionJobQueue>(_ => new InMemoryConversionJobQueue(queueCapacity));
builder.Services.AddSingleton<IConversionResultRegistry, InMemoryConversionResultRegistry>();
var maxJobMetadata = builder.Configuration.GetValue("TEYPDFCAD_MAX_JOB_METADATA", 10_000);
if (maxJobMetadata <= 0)
    throw new InvalidOperationException("TEYPDFCAD_MAX_JOB_METADATA must be positive.");
builder.Services.AddSingleton<IConversionJobRegistry>(_ => new InMemoryConversionJobRegistry(maxJobMetadata));
var artifactRoot = builder.Configuration["TEYPDFCAD_ARTIFACT_ROOT"]
    ?? Path.Combine(AppContext.BaseDirectory, "artifacts");
builder.Services.AddSingleton<IConversionArtifactStore>(
    _ => new FileSystemArtifactStore(artifactRoot));
builder.Services.AddSingleton<IConversionArtifactRetention>(serviceProvider =>
    (FileSystemArtifactStore)serviceProvider.GetRequiredService<IConversionArtifactStore>());
var workerMode = builder.Configuration["TEYPDFCAD_WORKER_MODE"]?.Trim().ToLowerInvariant() ?? "autocad";
var bridgeExecutable = builder.Configuration["TEYPDFCAD_AUTOCAD_BRIDGE_EXE"];
var coreConsole = builder.Configuration["TEYPDFCAD_AUTOCAD_CORE_CONSOLE"];
var pluginDll = builder.Configuration["TEYPDFCAD_AUTOCAD_PLUGIN_DLL"];
var baseDrawing = builder.Configuration["TEYPDFCAD_AUTOCAD_BASE_DWG"];
var autocadReadiness = workerMode == "autocad"
    ? AutoCadRuntimeReadiness.Evaluate(bridgeExecutable, coreConsole, pluginDll, baseDrawing)
    : new AutoCadRuntimeReadiness(true, null);
var conversionReady = autocadReadiness.IsReady;
var readinessError = autocadReadiness.Error;
if (workerMode == "autocad")
{
    if (string.IsNullOrWhiteSpace(bridgeExecutable))
    {
        builder.Services.AddSingleton<IAutoCadHostBridge, UnavailableAutoCadHostBridge>();
    }
    else
    {
        builder.Services.AddSingleton(_ => AutoCadHostBridgeOptions.FromConfiguration(
            builder.Configuration["TEYPDFCAD_AUTOCAD_BRIDGE_EXE"],
            builder.Configuration["TEYPDFCAD_AUTOCAD_TIMEOUT_SECONDS"]));
        builder.Services.AddSingleton<IAutoCadHostBridge, AutoCadProcessHostBridge>();
    }
    builder.Services.AddSingleton<IConversionWorker, AutoCadConversionWorker>();
}
else if (workerMode == "inmemory")
{
    builder.Services.AddSingleton<IConversionWorker>(serviceProvider =>
        new InMemoryConversionWorker(artifacts: serviceProvider.GetRequiredService<IConversionArtifactStore>()));
}
else
{
    throw new InvalidOperationException(
        $"Unsupported TEYPDFCAD_WORKER_MODE '{workerMode}'. Use 'inmemory' or 'autocad'.");
}
builder.Services.AddSingleton<ConversionJobRunner>();
builder.Services.AddHostedService<ConversionJobWorkerHostedService>();
builder.Services.AddHostedService<JobMetadataRetentionHostedService>();
builder.Services.AddSingleton(new SemaphoreSlim(1, 1));
var app = builder.Build();

app.MapGet("/health", () => conversionReady
    ? Results.Ok(new { status = "ok", workerMode })
    : Results.Json(new { status = "degraded", workerMode, error = readinessError }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapGet("/ready", () => conversionReady
    ? Results.Ok(new { status = "ready", workerMode })
    : Results.Json(new { status = "degraded", workerMode, error = readinessError }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapPost("/jobs", async (
    CreateJobRequest request,
    IConversionJobQueue queue,
    IConversionJobRegistry jobsById,
    SemaphoreSlim submissionGate,
    CancellationToken cancellationToken) =>
{
    if (!conversionReady)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    if (workerMode == "autocad")
        return Results.BadRequest(new { error = "pdf_upload_required", message = "Use POST /jobs/upload in autocad mode so the PDF artifact is stored." });

    if (string.IsNullOrWhiteSpace(request.PdfSha256) || string.IsNullOrWhiteSpace(request.PipelineVersion))
        return Results.BadRequest(new { error = "pdfSha256 and pipelineVersion are required" });

    ConversionJob job;
    try
    {
        job = ConversionJob.Create(
            request.JobId ?? Guid.NewGuid().ToString("N"),
            request.PdfSha256,
            request.PipelineVersion,
            request.Settings ?? ConversionSettings.Default);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }

    await submissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        if (queue.TryGetByIdempotencyKey(job.IdempotencyKey, out var existingByKey) && existingByKey is not null)
        {
            if (!jobsById.TryAdd(existingByKey) && !jobsById.TryGet(existingByKey.JobId, out _))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(ToResponse(existingByKey, GetStatusOrQueued(queue, existingByKey), true));
        }

        if (jobsById.TryGet(job.JobId, out var existingById) && existingById is not null)
            return Results.Conflict(new { error = "job_id_conflict", jobId = existingById.JobId });

        if (!jobsById.TryAdd(job))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        try
        {
            var enqueueResult = await queue.EnqueueAsync(job, cancellationToken).ConfigureAwait(false);
            if (enqueueResult == ConversionJobEnqueueResult.CapacityExceeded)
            {
                jobsById.TryRemove(job.JobId, out _);
                return Results.Json(new { error = "queue_full", message = "The conversion queue is temporarily full." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (enqueueResult == ConversionJobEnqueueResult.Duplicate)
            {
                jobsById.TryRemove(job.JobId, out _);
                queue.TryGetByIdempotencyKey(job.IdempotencyKey, out var existing);
                if (existing is null)
                    return Results.Conflict(new { error = "idempotency_conflict" });

                if (!jobsById.TryAdd(existing) && !jobsById.TryGet(existing.JobId, out _))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                return Results.Ok(ToResponse(existing, GetStatusOrQueued(queue, existing), true));
            }

            return Results.Accepted($"/jobs/{job.JobId}", ToResponse(job, GetStatusOrQueued(queue, job), false));
        }
        catch
        {
            jobsById.TryRemove(job.JobId, out _);
            throw;
        }
    }
    finally
    {
        submissionGate.Release();
    }
});

app.MapPost("/jobs/upload", async (
    HttpRequest request,
    IConversionJobQueue queue,
    IConversionArtifactStore artifacts,
    IConversionJobRegistry jobsById,
    SemaphoreSlim submissionGate,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (!conversionReady)
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

    if (!request.HasFormContentType || request.ContentType is null ||
        !Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) ||
        !string.Equals(mediaType.MediaType.ToString(), "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "multipart_form_data_required" });

    IFormCollection form;
    try
    {
        form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (InvalidDataException)
    {
        return Results.Json(new { error = "multipart_body_too_large" },
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    var pdf = form.Files.GetFile("pdf");
    if (pdf is null || pdf.Length == 0)
        return Results.BadRequest(new { error = "pdf_file_required" });
    if (pdf.Length > maxUploadBytes)
        return Results.Json(new { error = "pdf_too_large", maxBytes = maxUploadBytes },
            statusCode: StatusCodes.Status413PayloadTooLarge);

    await using (var signatureStream = pdf.OpenReadStream())
    {
        if (!await LooksLikePdfAsync(signatureStream, cancellationToken).ConfigureAwait(false))
            return Results.BadRequest(new { error = "pdf_signature_invalid" });
    }

    var pipelineVersion = form["pipelineVersion"].ToString();
    if (string.IsNullOrWhiteSpace(pipelineVersion))
        return Results.BadRequest(new { error = "pipelineVersion is required" });

    var settingsResult = ParseSettings(form);
    if (settingsResult.Error is not null)
        return Results.BadRequest(new { error = settingsResult.Error });

    string pdfSha256;
    await using (var input = pdf.OpenReadStream())
        pdfSha256 = ConversionJob.ComputePdfSha256(input);

    ConversionJob job;
    try
    {
        job = ConversionJob.Create(
            string.IsNullOrWhiteSpace(form["jobId"])
                ? Guid.NewGuid().ToString("N")
                : form["jobId"].ToString(),
            pdfSha256,
            pipelineVersion,
            settingsResult.Settings);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }

    await submissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        if (queue.TryGetByIdempotencyKey(job.IdempotencyKey, out var existing) && existing is not null)
        {
            if (!jobsById.TryAdd(existing) && !jobsById.TryGet(existing.JobId, out _))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(ToResponse(existing, GetStatusOrQueued(queue, existing), true));
        }

        if (jobsById.TryGet(job.JobId, out var existingById) && existingById is not null)
            return Results.Conflict(new { error = "job_id_conflict", jobId = existingById.JobId });

        if (!jobsById.TryAdd(job))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        try
        {
            await using (var input = pdf.OpenReadStream())
                await artifacts.StoreAsync(ConversionArtifactKeys.InputPdf(job.JobId), input, cancellationToken)
                    .ConfigureAwait(false);

            var enqueueResult = await queue.EnqueueAsync(job, cancellationToken).ConfigureAwait(false);
            if (enqueueResult == ConversionJobEnqueueResult.CapacityExceeded)
            {
                jobsById.TryRemove(job.JobId, out _);
                await TryDeleteInputArtifactAsync(artifacts, job.JobId, logger).ConfigureAwait(false);
                return Results.Json(new { error = "queue_full", message = "The conversion queue is temporarily full." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (enqueueResult == ConversionJobEnqueueResult.Duplicate)
            {
                jobsById.TryRemove(job.JobId, out _);
                await TryDeleteInputArtifactAsync(artifacts, job.JobId, logger).ConfigureAwait(false);
                queue.TryGetByIdempotencyKey(job.IdempotencyKey, out var existingDuplicate);
                if (existingDuplicate is null)
                    return Results.Conflict(new { error = "idempotency_conflict" });
                if (!jobsById.TryAdd(existingDuplicate) && !jobsById.TryGet(existingDuplicate.JobId, out _))
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                return Results.Ok(ToResponse(existingDuplicate, GetStatusOrQueued(queue, existingDuplicate), true));
            }

            return Results.Accepted($"/jobs/{job.JobId}", ToResponse(job, GetStatusOrQueued(queue, job), false));
        }
        catch
        {
            jobsById.TryRemove(job.JobId, out _);
            await TryDeleteInputArtifactAsync(artifacts, job.JobId, logger).ConfigureAwait(false);
            throw;
        }
    }
    finally
    {
        submissionGate.Release();
    }
});

app.MapGet("/jobs/{jobId}", (
    string jobId,
    IConversionJobQueue queue,
    IConversionJobRegistry jobsById,
    IConversionResultRegistry results) =>
{
    if (!jobsById.TryGet(jobId, out var job) || job is null)
        return Results.NotFound(new { error = "job_not_found" });

    results.TryGet(job.JobId, out var result);
    return Results.Ok(ToResponse(job, GetStatusOrQueued(queue, job), false, result));
});

app.MapGet("/jobs/{jobId}/result", async (
    string jobId,
    IConversionJobQueue queue,
    IConversionResultRegistry results,
    IConversionArtifactStore artifacts,
    IConversionJobRegistry jobsById,
    CancellationToken cancellationToken) =>
{
    if (!jobsById.TryGet(jobId, out var job) || job is null)
        return Results.NotFound(new { error = "job_not_found" });

    var status = GetStatusOrQueued(queue, job);
    if (status == ConversionJobStatus.Failed &&
        results.TryGet(job.JobId, out var failedResult) && failedResult is not null)
    {
        return Results.UnprocessableEntity(new
        {
            error = failedResult.FailureCode ?? "conversion_failed",
            message = failedResult.FailureMessage ?? "The conversion failed."
        });
    }

    if (status != ConversionJobStatus.Completed)
        return Results.Conflict(new { error = "result_not_ready", status = status.ToString().ToLowerInvariant() });

    if (!results.TryGet(job.JobId, out var result) || result is null ||
        string.IsNullOrWhiteSpace(result.OutputArtifactKey))
        return Results.Conflict(new { error = "result_pending", status = "completed" });

    var stream = await artifacts.OpenReadAsync(result.OutputArtifactKey, cancellationToken).ConfigureAwait(false);
    if (stream is null)
        return Results.NotFound(new { error = "artifact_not_found" });

    return Results.File(stream, "application/acad", Path.GetFileName(result.OutputArtifactKey));
});

app.MapPost("/jobs/{jobId}/cancel", async (
    string jobId,
    IConversionJobQueue queue,
    IConversionJobRegistry jobsById,
    IConversionArtifactStore artifacts,
    ILogger<Program> logger) =>
{
    if (!jobsById.TryGet(jobId, out var job) || job is null)
        return Results.NotFound(new { error = "job_not_found" });

    if (queue.TryCancel(job))
    {
        jobsById.MarkTerminal(job);
        try
        {
            await artifacts.DeleteAsync(ConversionArtifactKeys.InputPdf(job.JobId), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not immediately delete cancelled job input {JobId}; retention will retry.", job.JobId);
        }
        return Results.Ok(ToResponse(job, ConversionJobStatus.Cancelled, false));
    }

    return Results.Conflict(new
    {
        error = "job_not_cancellable",
        status = GetStatusOrQueued(queue, job).ToString().ToLowerInvariant()
    });
});

app.Run();

static JobResponse ToResponse(
    ConversionJob job,
    ConversionJobStatus status,
    bool deduplicated,
    ConversionResult? result = null)
    => new(
        job.JobId,
        status.ToString().ToLowerInvariant(),
        job.IdempotencyKey,
        deduplicated,
        result?.FailureCode,
        result?.FailureMessage);

static ConversionJobStatus GetStatusOrQueued(IConversionJobQueue queue, ConversionJob job)
{
    try
    {
        return queue.GetStatus(job);
    }
    catch (KeyNotFoundException)
    {
        return ConversionJobStatus.Queued;
    }
}

static (ConversionSettings Settings, string? Error) ParseSettings(IFormCollection form)
{
    var outputUnits = form["outputUnits"].ToString();
    var recognizerProfile = form["recognizerProfile"].ToString();
    var preserveText = form["preserveSourceGeometry"].ToString();

    if (string.IsNullOrWhiteSpace(outputUnits))
        outputUnits = ConversionSettings.Default.OutputUnits;
    if (string.IsNullOrWhiteSpace(recognizerProfile))
        recognizerProfile = ConversionSettings.Default.RecognizerProfile;

    var preserve = ConversionSettings.Default.PreserveSourceGeometry;
    if (!string.IsNullOrWhiteSpace(preserveText) && !bool.TryParse(preserveText, out preserve))
        return (ConversionSettings.Default, "preserveSourceGeometry must be true or false");

    return (new ConversionSettings
    {
        OutputUnits = outputUnits,
        PreserveSourceGeometry = preserve,
        RecognizerProfile = recognizerProfile,
    }, null);
}

static async Task<bool> LooksLikePdfAsync(Stream stream, CancellationToken cancellationToken)
{
    var buffer = new byte[1024];
    var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    ReadOnlySpan<byte> signature = "%PDF-"u8;
    for (var index = 0; index <= read - signature.Length; index++)
    {
        if (buffer.AsSpan(index, signature.Length).SequenceEqual(signature))
            return true;
    }

    return false;
}

static async Task TryDeleteInputArtifactAsync(
    IConversionArtifactStore artifacts,
    string jobId,
    ILogger logger)
{
    try
    {
        await artifacts.DeleteAsync(ConversionArtifactKeys.InputPdf(jobId), CancellationToken.None)
            .ConfigureAwait(false);
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Could not rollback input artifact for job {JobId}; retention will retry.", jobId);
    }
}

public sealed record CreateJobRequest(
    string? PdfSha256,
    string? PipelineVersion,
    string? JobId,
    ConversionSettings? Settings);

public sealed record JobResponse(
    string JobId,
    string Status,
    string IdempotencyKey,
    bool Deduplicated,
    string? FailureCode = null,
    string? FailureMessage = null);
