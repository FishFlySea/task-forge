using TaskForge.Core;

namespace TaskForge.Application;

public sealed class CodexRunGate : ICodexRunGate, IDisposable
{
    private readonly AgentBudgetOptions _options;
    private readonly SemaphoreSlim _semaphore;

    public CodexRunGate(
        AgentBudgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxCodexRunsPerTask <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxCodexRunsPerTask must be positive.");
        }

        if (options.MaxCodexRetries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxCodexRetries cannot be negative.");
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
        if (currentRuns >= _options.MaxCodexRunsPerTask)
        {
            throw new CodexBudgetExceededException(
                $"Task {taskId} has exhausted its Codex run budget "
                + $"({_options.MaxCodexRunsPerTask}).");
        }

        if (kind == CodexRunKind.Implementation
            && currentRuns != 0)
        {
            throw new CodexBudgetExceededException(
                $"Task {taskId} may have only one implementation Codex run.");
        }

        if (kind == CodexRunKind.Correction)
        {
            if (currentRuns == 0)
            {
                throw new InvalidOperationException(
                    "A correction run requires a previous implementation run.");
            }

            var completedRetries = currentRuns - 1;

            if (completedRetries >= _options.MaxCodexRetries)
            {
                throw new CodexBudgetExceededException(
                    $"Task {taskId} has exhausted its corrective Codex run budget "
                    + $"({_options.MaxCodexRetries}).");
            }
        }

        await _semaphore.WaitAsync(
            cancellationToken);

        return new Lease(_semaphore);
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
