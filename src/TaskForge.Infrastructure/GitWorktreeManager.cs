using System.Text.RegularExpressions;
using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed partial class GitWorktreeManager(
    string worktreesDirectory,
    TimeSpan? timeout = null) : IWorkspaceManager
{
    private readonly string _worktreesDirectory =
        Path.GetFullPath(
            string.IsNullOrWhiteSpace(worktreesDirectory)
                ? throw new ArgumentException(
                    "Worktrees directory is required.",
                    nameof(worktreesDirectory))
                : worktreesDirectory);

    private readonly TimeSpan _timeout =
        timeout ?? TimeSpan.FromMinutes(2);

    public static string GetDefaultWorktreesDirectory()
    {
        var localData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".taskforge");
        }

        return Path.Combine(
            localData,
            "TaskForge",
            "worktrees");
    }

    public async Task<WorkspaceHandle> CreateAsync(
        string repositoryPath,
        string baseCommit,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            repositoryPath);

        if (!IsValidCommitId(baseCommit))
        {
            throw new ArgumentException(
                "Base commit must be a full hexadecimal Git object id.",
                nameof(baseCommit));
        }

        ValidateTaskId(
            taskId);

        var repository =
            Path.GetFullPath(
                repositoryPath);

        Directory.CreateDirectory(
            _worktreesDirectory);

        var workspacePath =
            Path.Combine(
                _worktreesDirectory,
                taskId.Value);

        if (Directory.Exists(workspacePath)
            || File.Exists(workspacePath))
        {
            throw new InvalidOperationException(
                $"Workspace path already exists: {workspacePath}");
        }

        var result =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "worktree",
                    "add",
                    "--detach",
                    workspacePath,
                    baseCommit
                ],
                repository,
                _timeout,
                cancellationToken);

        EnsureSuccess(
            result,
            "Unable to create Git worktree.");

        return new WorkspaceHandle(
            repository,
            workspacePath,
            baseCommit);
    }

    public async Task<WorkspaceSnapshot> SnapshotAsync(
        WorkspaceHandle workspace,
        CancellationToken cancellationToken)
    {
        var status =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "status",
                    "--porcelain=v1",
                    "--untracked-files=all"
                ],
                workspace.Path,
                _timeout,
                cancellationToken);

        EnsureSuccess(
            status,
            "Unable to read workspace status.");

        var diff =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "diff",
                    "--no-ext-diff",
                    "--no-color",
                    "HEAD",
                    "--"
                ],
                workspace.Path,
                _timeout,
                cancellationToken);

        EnsureSuccess(
            diff,
            "Unable to read workspace diff.");

        var lines =
            status.StandardOutput.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);

        var changedFiles =
            lines
                .Select(ParseStatusPath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var untrackedFiles =
            lines
                .Where(x => x.StartsWith(
                    "?? ",
                    StringComparison.Ordinal))
                .Select(ParseStatusPath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new WorkspaceSnapshot
        {
            ChangedFiles = changedFiles,
            UntrackedFiles = untrackedFiles,
            Status = status.StandardOutput,
            Diff = diff.StandardOutput
        };
    }

    public async Task CleanupAsync(
        WorkspaceHandle workspace,
        CancellationToken cancellationToken)
    {
        var result =
            await ExternalProcessRunner.RunAsync(
                "git",
                [
                    "worktree",
                    "remove",
                    "--force",
                    workspace.Path
                ],
                workspace.RepositoryPath,
                _timeout,
                cancellationToken);

        if (!result.Success
            && Directory.Exists(
                workspace.Path))
        {
            throw new InvalidOperationException(
                "Unable to remove Git worktree."
                + Environment.NewLine
                + result.CombinedOutput);
        }

        await ExternalProcessRunner.RunAsync(
            "git",
            ["worktree", "prune"],
            workspace.RepositoryPath,
            _timeout,
            cancellationToken);
    }

    internal static bool IsValidCommitId(
        string value) =>
        !string.IsNullOrWhiteSpace(value)
        && CommitIdRegex().IsMatch(value);

    private static string ParseStatusPath(
        string line)
    {
        if (line.Length <= 3)
        {
            return string.Empty;
        }

        var path =
            line[3..].Trim();

        var renameSeparator =
            path.LastIndexOf(
                " -> ",
                StringComparison.Ordinal);

        if (renameSeparator >= 0)
        {
            path =
                path[(renameSeparator + 4)..];
        }

        return path.Trim('"');
    }

    private static void ValidateTaskId(
        TaskId taskId)
    {
        if (string.IsNullOrWhiteSpace(
                taskId.Value)
            || Path.IsPathRooted(
                taskId.Value)
            || !string.Equals(
                taskId.Value,
                Path.GetFileName(
                    taskId.Value),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Task id cannot be used as a workspace directory name.",
                nameof(taskId));
        }
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

    [GeneratedRegex(
        "^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CommitIdRegex();
}
