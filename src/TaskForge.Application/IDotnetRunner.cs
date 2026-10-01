using TaskForge.Core;

namespace TaskForge.Application;

public interface IDotnetRunner
{
    Task<ProcessResult> BuildAsync(
        string workspace,
        CancellationToken cancellationToken);

    Task<ProcessResult> TestAsync(
        string workspace,
        IReadOnlyList<TestTarget> targets,
        CancellationToken cancellationToken);
}
