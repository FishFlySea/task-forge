using TaskForge.Core;

namespace TaskForge.Application;

public sealed class CodexRunGate : ICodexRunGate, IDisposable
{
    private readonly CodexBudgetOptions _options;
    private readonly SemaphoreSlim _semaphore;

    public CodexRunGate(
        CodexBudgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxCodexRuns <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxCodexRuns must be positive.");
        }

        if (options.MaxConcurrentCodexRuns <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxConcurrentCodexRuns must be positive.");
        }

        _options = options;
        _semaphore = new SemaphoreSlim(
            options.MaxConcurrentCodexRuns,
            options.MaxConcurrentCodexRuns);
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        TaskId taskId,
        int currentRuns,
        CodexRunKind kind,
        CancellationToken cancellationToken)
    {
        if (currentRuns < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRuns));
        }

        if (currentRuns >= _options.MaxCodexRuns)
        {
            throw new CodexBudgetExceededException(
                $"Task {taskId} has exhausted its Codex run budget "
                + $"({_options.MaxCodexRuns}).");
        }

        if (kind == CodexRunKind.Implementation
            && currentRuns != 0)
        {
            throw new CodexBudgetExceededException(
                $"Task {taskId} may start Implementation only as its first Codex run.");
        }

        if (kind == CodexRunKind.Correction
            && currentRuns == 0)
        {
            throw new InvalidOperationException(
                "A correction run requires a previous implementation run.");
        }

        await _semaphore.WaitAsync(
            cancellationToken);

        return new Lease(
            _semaphore);
    }

    public void Dispose() =>
        _semaphore.Dispose();

    private sealed class Lease(
        SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(
                    ref _semaphore,
                    null)
                ?.Release();

            return ValueTask.CompletedTask;
        }
    }
}
