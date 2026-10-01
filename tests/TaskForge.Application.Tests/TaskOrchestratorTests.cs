using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskOrchestratorTests
{
    [Fact]
    public async Task Phase_two_run_builds_and_persists_task_packet()
    {
        var store = new FakeRunStore();

        var orchestrator = new TaskOrchestrator(
            store,
            new FakePlannerAgent(),
            new FakeExplorerAgent(),
            TimeProvider.System);

        var result = await orchestrator.RunAsync(
            new TaskRequest(
                "Implement planner",
                "/tmp/repository"),
            CancellationToken.None);

        Assert.Equal(
            WorkflowState.NeedsUser,
            result.State);

        Assert.Equal(
            [
                WorkflowState.Created,
                WorkflowState.Planning,
                WorkflowState.Exploring,
                WorkflowState.PacketReady,
                WorkflowState.NeedsUser
            ],
            store.SavedStates);

        Assert.Equal(
            [
                "plan.json",
                "exploration.json",
                "task-packet.json"
            ],
            store.Artifacts);
    }

    private sealed class FakePlannerAgent : IPlannerAgent
    {
        public Task<PlanResult> PlanAsync(
            TaskRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new PlanResult
                {
                    Summary = "Find planner code",
                    SearchTerms = ["Planner"],
                    LikelyAreas = ["Application"],
                    AcceptanceCriteria =
                    [
                        "Planner integration is available"
                    ]
                });
    }

    private sealed class FakeExplorerAgent : IExplorerAgent
    {
        public Task<ExplorationResult> ExploreAsync(
            TaskRequest request,
            PlanResult plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ExplorationResult
                {
                    RelevantFiles =
                    [
                        "src/TaskForge.Application/PlannerAgent.cs"
                    ],
                    Observations =
                    [
                        "Planner belongs to the application layer."
                    ],
                    TestTargets = [],
                    Confidence = 0.9
                });
    }

    private sealed class FakeRunStore : IRunStore
    {
        public List<WorkflowState> SavedStates { get; } = [];

        public List<string> Artifacts { get; } = [];

        public Task CreateAsync(
            TaskRequest request,
            RunMetadata metadata,
            CancellationToken cancellationToken)
        {
            SavedStates.Add(metadata.State);
            return Task.CompletedTask;
        }

        public Task SaveMetadataAsync(
            RunMetadata metadata,
            CancellationToken cancellationToken)
        {
            SavedStates.Add(metadata.State);
            return Task.CompletedTask;
        }

        public Task SaveArtifactAsync<T>(
            TaskId id,
            string fileName,
            T value,
            CancellationToken cancellationToken)
        {
            Artifacts.Add(fileName);
            return Task.CompletedTask;
        }

        public Task<RunMetadata?> GetAsync(
            TaskId id,
            CancellationToken cancellationToken) =>
            Task.FromResult<RunMetadata?>(null);

        public Task<IReadOnlyList<RunMetadata>> ListAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunMetadata>>([]);
    }
}
