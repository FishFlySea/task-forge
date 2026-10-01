using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class FileSystemRepositorySearchTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "taskforge-search-tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Search_ranks_matching_source_file()
    {
        Directory.CreateDirectory(_directory);

        var services =
            Path.Combine(
                _directory,
                "src",
                "Services");

        Directory.CreateDirectory(services);

        await File.WriteAllTextAsync(
            Path.Combine(
                services,
                "PropertyBindingService.cs"),
            "public sealed class PropertyBindingService { }");

        await File.WriteAllTextAsync(
            Path.Combine(
                services,
                "OtherService.cs"),
            "public sealed class OtherService { }");

        var sut =
            new FileSystemRepositorySearch();

        var result = await sut.SearchAsync(
            _directory,
            ["PropertyBinding"],
            10,
            CancellationToken.None);

        var match = Assert.Single(result);

        Assert.Equal(
            "src/Services/PropertyBindingService.cs",
            match.Path);

        Assert.True(
            match.Score > 0);
    }

    [Fact]
    public async Task Search_finds_term_after_context_truncation_boundary()
    {
        Directory.CreateDirectory(_directory);

        var path = Path.Combine(
            _directory,
            "LargeService.cs");

        var content =
            new string('x', 20_000)
            + Environment.NewLine
            + "public sealed class ImportantNeedle { }";

        await File.WriteAllTextAsync(
            path,
            content);

        var sut =
            new FileSystemRepositorySearch(
                maxContentChars: 1_000);

        var result =
            await sut.SearchAsync(
                _directory,
                ["ImportantNeedle"],
                10,
                CancellationToken.None);

        var match = Assert.Single(result);

        Assert.Equal(
            "LargeService.cs",
            match.Path);

        Assert.True(
            match.Content.Length <= 1_000);
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
