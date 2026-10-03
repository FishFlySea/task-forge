using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class GitClient(
    TimeSpan? timeout = null) : IGitClient
{
    private readonly TimeSpan _timeout =
        timeout ?? TimeSpan.FromMinutes(1);

    public async Task<string> GetHeadCommitAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result =
            await ExternalProcessRunner.RunAsync(
                "git",
                ["rev-parse", "HEAD"],
                repositoryPath,
                _timeout,
                cancellationToken);

        EnsureSuccess(
            result,
            "Unable to resolve repository HEAD.");

        var commit =
            result.StandardOutput.Trim();

        if (!GitWorktreeManager.IsValidCommitId(
                commit))
        {
            throw new InvalidOperationException(
                $"Git returned an invalid HEAD commit: '{commit}'.");
        }

        return commit;
    }

    public async Task<bool> IsWorkingTreeCleanAsync(
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        var result =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "status",
                    "--porcelain=v1",
                    "--untracked-files=all"
                ],
                repositoryPath,
                _timeout,
                cancellationToken);

        EnsureSuccess(
            result,
            "Unable to inspect Git working tree.");

        return string.IsNullOrWhiteSpace(
            result.StandardOutput);
    }

    public async Task<bool> CommitExistsAsync(
        string repositoryPath,
        string commit,
        CancellationToken cancellationToken)
    {
        if (!GitWorktreeManager.IsValidCommitId(
                commit))
        {
            return false;
        }

        var result =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "cat-file",
                    "-e",
                    $"{commit}^{{commit}}"
                ],
                repositoryPath,
                _timeout,
                cancellationToken);

        return result.Success;
    }

    private static void EnsureSuccess(
        ProcessResult result,
        string message)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(
                message
                + Environment.NewLine
                + result.CombinedOutput);
        }
    }
}
