using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class JsonRunStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(
            Path.GetTempPath(),
            "taskforge-tests",
            Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Create_get_list_and_artifact_roundtrip()
    {
        var store =
            new JsonRunStore(_directory);

        var id =
            new TaskId("tf-test");

        var request =
            new TaskRequest(
                "Test persistence",
                "/tmp/repository");

        var metadata =
            new RunMetadata
            {
                Id = id,
                RepositoryPath =
                    request.RepositoryPath,
                StartedAt =
                    DateTimeOffset.Parse(
                        "2026-10-01T09:00:00Z"),
                State =
                    WorkflowState.Created
            };

        await store.CreateAsync(
            request,
            metadata,
            CancellationToken.None);

        await store.SaveArtifactAsync(
            id,
            "plan.json",
            new PlanResult
            {
                Summary = "test",
                SearchTerms = ["test"],
                LikelyAreas = [],
                AcceptanceCriteria = []
            },
            CancellationToken.None);

        var loaded =
            await store.GetAsync(
                id,
                CancellationToken.None);

        var listed =
            await store.ListAsync(
                10,
                CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(
            id,
            loaded.Id);
        Assert.Equal(
            WorkflowState.Created,
            loaded.State);

        var item =
            Assert.Single(listed);

        Assert.Equal(
            id,
            item.Id);

        Assert.True(
            File.Exists(
                Path.Combine(
                    _directory,
                    id.Value,
                    "plan.json")));
    }

    [Fact]
    public async Task Artifact_file_name_cannot_escape_run_directory()
    {
        var store =
            new JsonRunStore(_directory);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveArtifactAsync(
                new TaskId("tf-test"),
                "../escape.json",
                new { Value = 1 },
                CancellationToken.None));
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
