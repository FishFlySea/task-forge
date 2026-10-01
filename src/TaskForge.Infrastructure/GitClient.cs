using TaskForge.Application;

namespace TaskForge.Infrastructure;

public sealed class GitClient(
    TimeSpan? timeout = null) : IGitClient
{
    private readonly TimeSpan _timeout =
        timeout ?? TimeSpan.FromMinutes(1);

    public async Task<string> GetWorkingTreeSnapshotAsync(
        string workspace,
        CancellationToken cancellationToken)
    {
        var status =
            await ExternalProcessRunner.RunAsync(
                "git",
                ["status", "--short"],
                workspace,
                _timeout,
                cancellationToken);

        var diff =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "diff",
                    "--no-ext-diff",
                    "--unified=3",
                    "--"
                ],
                workspace,
                _timeout,
                cancellationToken);

        return
            "## git status --short"
            + Environment.NewLine
            + status.StandardOutput
            + Environment.NewLine
            + "## git diff"
            + Environment.NewLine
            + diff.StandardOutput;
    }
}
