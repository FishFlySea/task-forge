namespace TaskForge.Application;

public interface IGitClient
{
    Task<string> GetWorkingTreeSnapshotAsync(
        string workspace,
        CancellationToken cancellationToken);
}
