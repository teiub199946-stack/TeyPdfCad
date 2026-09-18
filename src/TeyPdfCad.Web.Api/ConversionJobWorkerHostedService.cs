using TeyPdfCad.Web.Jobs;

namespace TeyPdfCad.Web.Api;

public sealed class ConversionJobWorkerHostedService : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly ConversionJobRunner _runner;
    private readonly ILogger<ConversionJobWorkerHostedService> _logger;

    public ConversionJobWorkerHostedService(
        ConversionJobRunner runner,
        ILogger<ConversionJobWorkerHostedService> logger)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runner.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Conversion job loop iteration failed.");
                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
