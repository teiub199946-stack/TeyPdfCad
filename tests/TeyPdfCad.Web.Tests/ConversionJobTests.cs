using System.Diagnostics;
using System.Text;
using TeyPdfCad.Web.Jobs;
using TeyPdfCad.Web.Storage;
using Xunit;

namespace TeyPdfCad.Web.Tests;

public sealed class ConversionJobTests
{
    private const string PdfHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void IdempotencyKey_IsStableForEquivalentSettings()
    {
        var first = ConversionJob.Create("job-a", PdfHash.ToUpperInvariant(), "v0.1", new ConversionSettings());
        var second = ConversionJob.Create("job-b", PdfHash, " v0.1 ", new ConversionSettings());

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Fact]
    public void IdempotencyKey_ChangesWhenPipelineVersionChanges()
    {
        var first = ConversionJob.Create("job-a", PdfHash, "v0.1");
        var second = ConversionJob.Create("job-b", PdfHash, "v0.2");

        Assert.NotEqual(first.IdempotencyKey, second.IdempotencyKey);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("job\\escape")]
    [InlineData("job with spaces")]
    public void Create_RejectsUnsafeJobId(string jobId)
    {
        Assert.Throws<ArgumentException>(() => ConversionJob.Create(jobId, PdfHash, "v0.1"));
    }

    [Fact]
    public void Create_RejectsOversizedPipelineAndSettings()
    {
        Assert.Throws<ArgumentException>(() => ConversionJob.Create("job-a", PdfHash, new string('v', 129)));
        Assert.Throws<ArgumentException>(() => ConversionJob.Create(
            "job-b",
            PdfHash,
            "v0.1",
            new ConversionSettings { RecognizerProfile = new string('r', 65) }));
    }

    [Fact]
    public void Create_RejectsControlCharactersInSettings()
    {
        Assert.Throws<ArgumentException>(() => ConversionJob.Create(
            "job-a",
            PdfHash,
            "v0.1",
            new ConversionSettings { OutputUnits = "mm\n" }));
    }

    [Fact]
    public void IdempotencyKey_DefensivelyRejectsInvalidDirectRecord()
    {
        var job = new ConversionJob(
            "job-direct",
            PdfHash,
            "v0.1",
            new ConversionSettings { OutputUnits = "mm\n" });

        Assert.Throws<ArgumentException>(() => _ = job.IdempotencyKey);
    }

    [Fact]
    public void AutoCadHostBridgeOptions_RejectsInvalidTimeout()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AutoCadHostBridgeOptions.FromConfiguration("C:\\bridge.exe", "not-a-number"));
    }

    [Fact]
    public void ComputePdfSha256_IsDeterministic()
    {
        using var first = new MemoryStream(Encoding.UTF8.GetBytes("pdf fixture"));
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("pdf fixture"));
        first.Position = 4;

        Assert.Equal(ConversionJob.ComputePdfSha256(first), ConversionJob.ComputePdfSha256(second));
        Assert.Equal(4, first.Position);
    }

    [Fact]
    public async Task InMemoryWorker_ReturnsCompletedArtifact()
    {
        var job = ConversionJob.Create("job-a", PdfHash, "v0.1");
        var worker = new InMemoryConversionWorker();

        var result = await worker.ConvertAsync(job);

        Assert.Equal(ConversionJobStatus.Completed, result.Status);
        Assert.Equal("jobs/job-a/result.dwg", result.OutputArtifactKey);
    }

    [Fact]
    public async Task Queue_DeduplicatesByIdempotencyKeyAndTracksLifecycle()
    {
        var job = ConversionJob.Create("job-a", PdfHash, "v0.1");
        var duplicate = ConversionJob.Create("job-b", PdfHash, "v0.1");
        var queue = new InMemoryConversionJobQueue();

        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(job));
        Assert.Equal(ConversionJobEnqueueResult.Duplicate, await queue.EnqueueAsync(duplicate));
        Assert.Equal(ConversionJobStatus.Queued, queue.GetStatus(job));
        Assert.True(queue.TryGetByIdempotencyKey(duplicate.IdempotencyKey, out var existing));
        Assert.Equal(job.JobId, existing!.JobId);

        var dequeued = await queue.DequeueAsync();

        Assert.Equal(job, dequeued);
        Assert.Equal(ConversionJobStatus.Processing, queue.GetStatus(job));
        Assert.True(queue.TryComplete(job, ConversionJobStatus.Completed));
        Assert.Equal(ConversionJobStatus.Completed, queue.GetStatus(job));
    }

    [Fact]
    public async Task Queue_CancelledQueuedJobIsSkippedByDequeue()
    {
        var job = ConversionJob.Create("job-a", PdfHash, "v0.1");
        var next = ConversionJob.Create("job-b", "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", "v0.1");
        var queue = new InMemoryConversionJobQueue();

        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(job));
        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(next));
        Assert.True(queue.TryCancel(job));

        Assert.Equal(next, await queue.DequeueAsync());
    }

    [Fact]
    public async Task Queue_ReturnsCapacityExceededWithoutWaiting()
    {
        var first = ConversionJob.Create("job-first", PdfHash, "v0.1");
        var second = ConversionJob.Create("job-second", "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", "v0.1");
        var queue = new InMemoryConversionJobQueue(capacity: 1);

        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(first));
        var stopwatch = Stopwatch.StartNew();
        Assert.Equal(ConversionJobEnqueueResult.CapacityExceeded, await queue.EnqueueAsync(second));
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"A full queue enqueue took {stopwatch.Elapsed}, indicating it waited instead of failing fast.");
        Assert.False(queue.TryGetByIdempotencyKey(second.IdempotencyKey, out _));
    }

    [Fact]
    public async Task Queue_RemovesExpiredTerminalEntriesButKeepsProcessingEntries()
    {
        var completed = ConversionJob.Create("job-completed", PdfHash, "v0.1");
        var processing = ConversionJob.Create("job-processing", "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", "v0.1");
        var queue = new InMemoryConversionJobQueue();

        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(completed));
        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(processing));
        Assert.Equal(completed, await queue.DequeueAsync());
        Assert.True(queue.TryComplete(completed, ConversionJobStatus.Completed));
        Assert.Equal(processing, await queue.DequeueAsync());
        await Task.Delay(10);

        Assert.Equal(1, queue.RemoveTerminalOlderThan(TimeSpan.FromMilliseconds(1)));
        Assert.Throws<KeyNotFoundException>(() => queue.GetStatus(completed));
        Assert.Equal(ConversionJobStatus.Processing, queue.GetStatus(processing));
    }

    [Fact]
    public async Task JobRegistry_IsCapacityBoundAndExpiresOnlyMarkedTerminalEntries()
    {
        var first = ConversionJob.Create("job-a", PdfHash, "v0.1");
        var second = ConversionJob.Create("job-b", "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", "v0.1");
        var registry = new InMemoryConversionJobRegistry(capacity: 1);

        Assert.True(registry.TryAdd(first));
        Assert.False(registry.TryAdd(second));
        registry.MarkTerminal(first);
        Assert.True(registry.TryGet(first.JobId, out _));
        Assert.Equal(0, registry.RemoveTerminalOlderThan(TimeSpan.FromHours(1)));
        await Task.Delay(10);
        Assert.Equal(1, registry.RemoveTerminalOlderThan(TimeSpan.FromMilliseconds(1)));
        Assert.False(registry.TryGet(first.JobId, out _));
    }

    [Fact]
    public async Task JobRegistry_DoesNotMarkReplacementWhenOldJobCompletes()
    {
        var first = ConversionJob.Create("reused-id", PdfHash, "v0.1");
        var replacement = ConversionJob.Create("reused-id", "abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd", "v0.1");
        var registry = new InMemoryConversionJobRegistry();

        Assert.True(registry.TryAdd(first));
        Assert.True(registry.TryRemove(first.JobId, out _));
        Assert.True(registry.TryAdd(replacement));
        registry.MarkTerminal(first);
        await Task.Delay(10);

        Assert.Equal(0, registry.RemoveTerminalOlderThan(TimeSpan.FromMilliseconds(1)));
        Assert.True(registry.TryGet(replacement.JobId, out var current));
        Assert.Equal(replacement.IdempotencyKey, current!.IdempotencyKey);
    }

    [Fact]
    public async Task ResultRegistry_RemovesExpiredResults()
    {
        var registry = new InMemoryConversionResultRegistry();
        registry.Set(ConversionResult.Completed("job-a", "jobs/job-a/result.dwg"));

        await Task.Delay(10);

        Assert.Equal(1, registry.RemoveOlderThan(TimeSpan.FromMilliseconds(1)));
        Assert.False(registry.TryGet("job-a", out _));
    }

    [Fact]
    public async Task Runner_ValidatesResultAndCompletesLifecycle()
    {
        var job = ConversionJob.Create("job-a", PdfHash, "v0.1");
        var queue = new InMemoryConversionJobQueue();
        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(job));
        var runner = new ConversionJobRunner(queue, new InMemoryConversionWorker(), new InMemoryConversionJobRegistry());

        var result = await runner.RunOnceAsync();

        Assert.Equal(ConversionJobStatus.Completed, result.Status);
        Assert.Equal(ConversionJobStatus.Completed, queue.GetStatus(job));
    }

    [Fact]
    public async Task Runner_PublishesResultBeforeMarkingQueueCompleted()
    {
        var job = ConversionJob.Create("job-result-order", PdfHash, "v0.1");
        var queue = new InMemoryConversionJobQueue();
        Assert.Equal(ConversionJobEnqueueResult.Accepted, await queue.EnqueueAsync(job));
        var results = new InMemoryConversionResultRegistry();
        var runner = new ConversionJobRunner(
            queue,
            new InMemoryConversionWorker(),
            new InMemoryConversionJobRegistry(),
            results);

        var result = await runner.RunOnceAsync();

        Assert.True(results.TryGet(job.JobId, out var published));
        Assert.Equal(result, published);
        Assert.Equal(ConversionJobStatus.Completed, queue.GetStatus(job));
    }

    [Fact]
    public async Task FileSystemArtifactStore_RejectsPathTraversalAndRoundTripsContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "TeyPdfCad", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemArtifactStore(root, maxBytes: 64);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await store.StoreAsync("../outside.bin", new MemoryStream([1, 2, 3])));

            await store.StoreAsync("jobs/job-a/result.dwg", new MemoryStream([4, 5, 6]));
            await using var result = await store.OpenReadAsync("jobs/job-a/result.dwg");
            Assert.NotNull(result);
            Assert.Equal(new byte[] { 4, 5, 6 }, await ReadAllAsync(result!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemArtifactStore_RetentionIsScopedToJobs()
    {
        var root = Path.Combine(Path.GetTempPath(), "TeyPdfCad", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSystemArtifactStore(root);
            await store.StoreAsync("unrelated.txt", new MemoryStream([1]));
            await store.StoreAsync("jobs/job-a/result.dwg", new MemoryStream([2]));
            var old = DateTime.UtcNow.AddDays(-2);
            File.SetLastWriteTimeUtc(Path.Combine(root, "unrelated.txt"), old);
            File.SetLastWriteTimeUtc(Path.Combine(root, "jobs", "job-a", "result.dwg"), old);

            Assert.Equal(1, await store.DeleteOlderThanAsync(TimeSpan.FromDays(1)));
            Assert.True(File.Exists(Path.Combine(root, "unrelated.txt")));
            Assert.False(File.Exists(Path.Combine(root, "jobs", "job-a", "result.dwg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
