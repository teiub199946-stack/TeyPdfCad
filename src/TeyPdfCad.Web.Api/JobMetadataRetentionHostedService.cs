using TeyPdfCad.Web.Jobs;
using TeyPdfCad.Web.Storage;

namespace TeyPdfCad.Web.Api;

public sealed class JobMetadataRetentionHostedService : BackgroundService
{
    private readonly IConversionJobRegistry _jobs;
    private readonly IConversionJobQueue _queue;
    private readonly IConversionResultRegistry _results;
    private readonly IConversionArtifactRetention _artifacts;
    private readonly ILogger<JobMetadataRetentionHostedService> _logger;
    private readonly TimeSpan _retentionAge;
    private readonly TimeSpan _sweepInterval;

    public JobMetadataRetentionHostedService(
        IConversionJobRegistry jobs,
        IConversionJobQueue queue,
        IConversionResultRegistry results,
        IConversionArtifactRetention artifacts,
        IConfiguration configuration,
        ILogger<JobMetadataRetentionHostedService> logger)
    {
        _jobs = jobs;
        _queue = queue;
        _results = results;
        _artifacts = artifacts;
        _logger = logger;
        var hours = configuration.GetValue("TEYPDFCAD_ARTIFACT_RETENTION_HOURS", 24);
        if (hours <= 0)
            throw new InvalidOperationException("TEYPDFCAD_ARTIFACT_RETENTION_HOURS must be positive.");
        _retentionAge = TimeSpan.FromHours(hours);
        var sweepMinutes = configuration.GetValue("TEYPDFCAD_RETENTION_SWEEP_MINUTES", 5);
        if (sweepMinutes <= 0)
            throw new InvalidOperationException("TEYPDFCAD_RETENTION_SWEEP_MINUTES must be positive.");
        _sweepInterval = TimeSpan.FromMinutes(sweepMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_sweepInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Conversion metadata retention pass failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunSweepAsync(CancellationToken stoppingToken)
    {
        var artifacts = 0;
        var results = 0;
        var jobs = 0;
        var queue = 0;

        try
        {
            artifacts = await _artifacts.DeleteOlderThanAsync(_retentionAge, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Artifact retention pass failed; metadata retention will continue.");
        }

        try
        {
            results = _results.RemoveOlderThan(_retentionAge);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Conversion result retention pass failed; job retention will continue.");
        }

        try
        {
            jobs = _jobs.RemoveTerminalOlderThan(_retentionAge);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Job metadata retention pass failed; queue retention will continue.");
        }

        try
        {
            queue = _queue.RemoveTerminalOlderThan(_retentionAge);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Conversion queue retention pass failed.");
        }

        if (artifacts > 0 || results > 0 || jobs > 0 || queue > 0)
            _logger.LogInformation("Removed expired conversion state: artifacts={Artifacts}, results={Results}, jobs={Jobs}, queue={Queue}.", artifacts, results, jobs, queue);
    }
}
