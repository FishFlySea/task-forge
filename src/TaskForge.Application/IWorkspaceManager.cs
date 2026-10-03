using TaskForge.Core;

namespace TaskForge.Application;

public interface IWorkspaceManager
{
    Task<WorkspaceHandle> CreateAsync(
        string repositoryPath,
        string baseCommit,
        TaskId taskId,
        CancellationToken cancellationToken);

    Task<WorkspaceSnapshot> SnapshotAsync(
        WorkspaceHandle workspace,
        CancellationToken cancellationToken);

    Task CleanupAsync(
        WorkspaceHandle workspace,
        CancellationToken cancellationToken);
}
