namespace TaskForge.Application;

public interface IGitClient
{
    Task<string> GetHeadCommitAsync(
        string repositoryPath,
        CancellationToken cancellationToken);

    Task<bool> IsWorkingTreeCleanAsync(
        string repositoryPath,
        CancellationToken cancellationToken);

    Task<bool> CommitExistsAsync(
        string repositoryPath,
        string commit,
        CancellationToken cancellationToken);
}
