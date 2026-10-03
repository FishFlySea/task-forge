using System.Diagnostics;
using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class GitWorktreeManagerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "taskforge-worktree-tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Creates_snapshots_and_cleans_disposable_worktree()
    {
        var repository =
            Path.Combine(
                _root,
                "repo");

        var worktrees =
            Path.Combine(
                _root,
                "worktrees");

        Directory.CreateDirectory(
            repository);

        await GitAsync(
            repository,
            "init");

        await GitAsync(
            repository,
            "config",
            "user.email",
            "taskforge-tests@example.invalid");

        await GitAsync(
            repository,
            "config",
            "user.name",
            "TaskForge Tests");

        var tracked =
            Path.Combine(
                repository,
                "tracked.txt");

        await File.WriteAllTextAsync(
            tracked,
            "original");

        await GitAsync(
            repository,
            "add",
            "tracked.txt");

        await GitAsync(
            repository,
            "commit",
            "-m",
            "initial");

        var gitClient =
            new GitClient();

        var baseCommit =
            await gitClient.GetHeadCommitAsync(
                repository,
                CancellationToken.None);

        Assert.True(
            await gitClient.IsWorkingTreeCleanAsync(
                repository,
                CancellationToken.None));

        var sut =
            new GitWorktreeManager(
                worktrees);

        var workspace =
            await sut.CreateAsync(
                repository,
                baseCommit,
                new TaskId("tf-test"),
                CancellationToken.None);

        Assert.True(
            Directory.Exists(
                workspace.Path));

        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(
                Path.Combine(
                    workspace.Path,
                    "tracked.txt")));

        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.Path,
                "tracked.txt"),
            "changed");

        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.Path,
                "new.txt"),
            "new");

        var snapshot =
            await sut.SnapshotAsync(
                workspace,
                CancellationToken.None);

        Assert.Contains(
            "tracked.txt",
            snapshot.ChangedFiles);

        Assert.Contains(
            "new.txt",
            snapshot.ChangedFiles);

        Assert.Contains(
            "new.txt",
            snapshot.UntrackedFiles);

        Assert.Contains(
            "tracked.txt",
            snapshot.Diff);

        await sut.CleanupAsync(
            workspace,
            CancellationToken.None);

        Assert.False(
            Directory.Exists(
                workspace.Path));

        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(
                tracked));

        Assert.True(
            await gitClient.IsWorkingTreeCleanAsync(
                repository,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("HEAD")]
    [InlineData("../main")]
    [InlineData("012345")]
    public void Rejects_non_full_commit_ids(
        string commit)
    {
        Assert.False(
            GitWorktreeManager.IsValidCommitId(
                commit));
    }

    private static async Task GitAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using var process =
            Process.Start(
                startInfo)
            ?? throw new InvalidOperationException(
                "Unable to start git.");

        var stdout =
            process.StandardOutput.ReadToEndAsync();

        var stderr =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed:"
                + Environment.NewLine
                + await stdout
                + Environment.NewLine
                + await stderr);
        }

        await stdout;
        await stderr;
    }

    public void Dispose()
    {
        if (Directory.Exists(
                _root))
        {
            Directory.Delete(
                _root,
                recursive: true);
        }
    }
}
