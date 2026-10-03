using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class RunInspectorTests
{
    [Fact]
    public async Task Ready_run_with_complete_packet_can_be_applied()
    {
        var id =
            new TaskId("tf-test");

        var store =
            new FakeRunStore
            {
                Metadata =
                    CreateMetadata(
                        id,
                        WorkflowState.ReadyToApply),
                Request =
                    new TaskRequest(
                        "Fix the bug",
                        Directory.GetCurrentDirectory()),
                Plan =
                    new PlanResult
                    {
                        Summary = "Find bug",
                        SearchTerms = ["Bug"],
                        LikelyAreas = ["src"],
                        AcceptanceCriteria =
                        [
                            "Bug is fixed"
                        ]
                    },
                Exploration =
                    new ExplorationResult
                    {
                        RelevantFiles =
                        [
                            "src/Bug.cs"
                        ],
                        Observations =
                        [
                            "Bug is in Bug.cs"
                        ],
                        TestTargets = [],
                        Confidence = 0.9
                    },
                Packet =
                    new TaskPacket
                    {
                        SchemaVersion = 1,
                        BaseCommit = "0123456789abcdef0123456789abcdef01234567",
                        Goal = "Fix the bug",
                        Constraints = [],
                        AcceptanceCriteria =
                        [
                            "Bug is fixed"
                        ],
                        RelevantFiles =
                        [
                            "src/Bug.cs"
                        ],
                        Observations =
                        [
                            "Bug is in Bug.cs"
                        ],
                        WriteScope =
                        [
                            "src/**"
                        ],
                        TestTargets = []
                    }
            };

        var sut =
            new RunInspector(
                store);

        var result =
            await sut.InspectAsync(
                id,
                CancellationToken.None);

        Assert.True(
            result.CanApply);

        Assert.Empty(
            result.Warnings);

        Assert.Equal(
            "Fix the bug",
            result.Request?.Goal);
    }

    [Fact]
    public async Task Missing_packet_blocks_apply_and_is_reported()
    {
        var id =
            new TaskId("tf-test");

        var store =
            new FakeRunStore
            {
                Metadata =
                    CreateMetadata(
                        id,
                        WorkflowState.ReadyToApply),
                Request =
                    new TaskRequest(
                        "Fix the bug",
                        Directory.GetCurrentDirectory()),
                Plan =
                    new PlanResult
                    {
                        Summary = "Find bug",
                        SearchTerms = ["Bug"],
                        LikelyAreas = ["src"],
                        AcceptanceCriteria =
                        [
                            "Bug is fixed"
                        ]
                    },
                Exploration =
                    new ExplorationResult
                    {
                        RelevantFiles =
                        [
                            "src/Bug.cs"
                        ],
                        Observations = [],
                        TestTargets = [],
                        Confidence = 0.8
                    }
            };

        var sut =
            new RunInspector(
                store);

        var result =
            await sut.InspectAsync(
                id,
                CancellationToken.None);

        Assert.False(
            result.CanApply);

        Assert.Contains(
            "task-packet.json is missing.",
            result.Warnings);
    }

    [Fact]
    public async Task Already_started_run_is_not_applyable()
    {
        var id =
            new TaskId("tf-test");

        var store =
            new FakeRunStore
            {
                Metadata =
                    CreateMetadata(
                        id,
                        WorkflowState.Building)
                    with
                    {
                        CodexRuns = 1
                    },
                Request =
                    new TaskRequest(
                        "Fix the bug",
                        Directory.GetCurrentDirectory()),
                Packet =
                    new TaskPacket
                    {
                        SchemaVersion = 1,
                        BaseCommit = "0123456789abcdef0123456789abcdef01234567",
                        Goal = "Fix the bug",
                        Constraints = [],
                        AcceptanceCriteria =
                        [
                            "Bug is fixed"
                        ],
                        RelevantFiles =
                        [
                            "src/Bug.cs"
                        ],
                        Observations = [],
                        WriteScope =
                        [
                            "src/**"
                        ],
                        TestTargets = []
                    }
            };

        var sut =
            new RunInspector(
                store);

        var result =
            await sut.InspectAsync(
                id,
                CancellationToken.None);

        Assert.False(
            result.CanApply);

        Assert.Contains(
            result.Warnings,
            x => x.Contains(
                "already been invoked",
                StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            result.Warnings,
            x => x.Contains(
                "not applyable",
                StringComparison.OrdinalIgnoreCase));
    }

    private static RunMetadata CreateMetadata(
        TaskId id,
        WorkflowState state) =>
        new()
        {
            Id = id,
            RepositoryPath =
                Directory.GetCurrentDirectory(),
            StartedAt =
                DateTimeOffset.Parse(
                    "2026-10-03T12:00:00Z"),
            State = state
        };

    private sealed class FakeRunStore :
        IRunStore
    {
        public RunMetadata? Metadata { get; init; }

        public TaskRequest? Request { get; init; }

        public PlanResult? Plan { get; init; }

        public ExplorationResult? Exploration { get; init; }

        public TaskPacket? Packet { get; init; }

        public Task CreateAsync(
            TaskRequest request,
            RunMetadata metadata,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveMetadataAsync(
            RunMetadata metadata,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveArtifactAsync<T>(
            TaskId id,
            string fileName,
            T value,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<T?> LoadArtifactAsync<T>(
            TaskId id,
            string fileName,
            CancellationToken cancellationToken)
            where T : class
        {
            object? value =
                fileName switch
                {
                    "request.json" => Request,
                    "plan.json" => Plan,
                    "exploration.json" => Exploration,
                    "task-packet.json" => Packet,
                    _ => null
                };

            return Task.FromResult(
                value as T);
        }

        public Task SaveTextArtifactAsync(
            TaskId id,
            string fileName,
            string content,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RunMetadata?> GetAsync(
            TaskId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Metadata?.Id == id
                    ? Metadata
                    : null);

        public Task<IReadOnlyList<RunMetadata>> ListAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunMetadata>>(
                Metadata is null
                    ? []
                    : [Metadata]);
    }
}
