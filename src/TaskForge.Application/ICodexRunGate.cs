using TaskForge.Core;

namespace TaskForge.Application;

public interface ICodexRunGate
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        TaskId taskId,
        int currentRuns,
        CodexRunKind kind,
        CancellationToken cancellationToken);
}
