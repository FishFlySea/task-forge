using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class DotnetRunnerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "taskforge-dotnet-tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Project_target_must_stay_inside_workspace()
    {
        Directory.CreateDirectory(_directory);

        var outsideDirectory =
            Path.Combine(
                Path.GetDirectoryName(_directory)!,
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(outsideDirectory);

        var outsideProject =
            Path.Combine(
                outsideDirectory,
                "Outside.csproj");

        await File.WriteAllTextAsync(
            outsideProject,
            "<Project />");

        var relativeEscape =
            Path.GetRelativePath(
                _directory,
                outsideProject);

        Assert.Null(
            DotnetRunner.ResolveProjectPath(
                _directory,
                relativeEscape));

        Assert.Null(
            DotnetRunner.ResolveProjectPath(
                _directory,
                outsideProject));

        Directory.Delete(
            outsideDirectory,
            recursive: true);
    }

    [Fact]
    public async Task Project_target_inside_workspace_is_accepted()
    {
        Directory.CreateDirectory(_directory);

        var project =
            Path.Combine(
                _directory,
                "tests",
                "Tests.csproj");

        Directory.CreateDirectory(
            Path.GetDirectoryName(project)!);

        await File.WriteAllTextAsync(
            project,
            "<Project />");

        var resolved =
            DotnetRunner.ResolveProjectPath(
                _directory,
                "tests/Tests.csproj");

        Assert.Equal(
            Path.GetFullPath(project),
            resolved);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(
                _directory,
                recursive: true);
        }
    }
}
