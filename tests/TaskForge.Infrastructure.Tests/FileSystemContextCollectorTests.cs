using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class FileSystemContextCollectorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "taskforge-context-tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Collects_bounded_span_around_search_term()
    {
        var sourceDirectory =
            Path.Combine(
                _directory,
                "src");

        Directory.CreateDirectory(
            sourceDirectory);

        var lines =
            Enumerable.Range(
                    1,
                    200)
                .Select(
                    i => i == 120
                        ? "public sealed class ImportantNeedle { }"
                        : $"// line {i}")
                .ToArray();

        await File.WriteAllLinesAsync(
            Path.Combine(
                sourceDirectory,
                "LargeService.cs"),
            lines);

        var sut =
            new FileSystemContextCollector(
                new TaskPacketOptions
                {
                    MaxContextCharacters = 1_000,
                    MaxSpanCharacters = 600,
                    ApproximateCharactersPerToken = 4
                });

        var result =
            await sut.CollectAsync(
                _directory,
                ["src/LargeService.cs"],
                ["ImportantNeedle"],
                CancellationToken.None);

        var span =
            Assert.Single(
                result.Spans);

        Assert.Equal(
            "src/LargeService.cs",
            span.Path);

        Assert.InRange(
            span.StartLine,
            90,
            120);

        Assert.Contains(
            "ImportantNeedle",
            span.Content);

        Assert.True(
            result.Budget.UsedContextCharacters
            <= result.Budget.MaxContextCharacters);

        Assert.True(
            span.Content.Length <= 600);
    }

    [Fact]
    public async Task Total_context_never_exceeds_budget()
    {
        Directory.CreateDirectory(
            _directory);

        await File.WriteAllTextAsync(
            Path.Combine(
                _directory,
                "One.cs"),
            new string(
                'a',
                2_000));

        await File.WriteAllTextAsync(
            Path.Combine(
                _directory,
                "Two.cs"),
            new string(
                'b',
                2_000));

        var sut =
            new FileSystemContextCollector(
                new TaskPacketOptions
                {
                    MaxContextCharacters = 750,
                    MaxSpanCharacters = 500
                });

        var result =
            await sut.CollectAsync(
                _directory,
                ["One.cs", "Two.cs"],
                [],
                CancellationToken.None);

        Assert.True(
            result.Budget.UsedContextCharacters
            <= 750);

        Assert.Equal(
            result.Spans.Sum(
                x => x.Content.Length),
            result.Budget.UsedContextCharacters);
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\file.txt")]
    public async Task Rejects_paths_outside_repository(
        string path)
    {
        Directory.CreateDirectory(
            _directory);

        var sut =
            new FileSystemContextCollector(
                new TaskPacketOptions());

        var result =
            await sut.CollectAsync(
                _directory,
                [path],
                [],
                CancellationToken.None);

        Assert.Empty(
            result.Spans);
    }

    public void Dispose()
    {
        if (Directory.Exists(
                _directory))
        {
            Directory.Delete(
                _directory,
                recursive: true);
        }
    }
}
