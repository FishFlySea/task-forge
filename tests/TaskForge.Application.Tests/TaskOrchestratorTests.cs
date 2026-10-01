using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskOrchestratorTests
{
    [Fact]
    public async Task Phase_one_run_is_persisted_and_stops_at_needs_user()
    {
        var store = new FakeRunStore();
        var orchestrator = new TaskOrchestrator(store, TimeProvider.System);

        var result = await orchestrator.RunAsync(
            new TaskRequest("Implement planner", "/tmp/repository"),
            CancellationToken.None);

        Assert.Equal(WorkflowState.NeedsUser, result.State);
        Assert.Equal(
            [
                WorkflowState.Created,
                WorkflowState.Planning,
                WorkflowState.NeedsUser
            ],
            store.SavedStates);
    }

    private sealed class FakeRunStore : IRunStore
    {
        public List<WorkflowState> SavedStates { get; } = [];

        public Task CreateAsync(TaskRequest request, RunMetadata metadata, CancellationToken cancellationToken)
        {
            SavedStates.Add(metadata.State);
            return Task.CompletedTask;
        }

        public Task SaveMetadataAsync(RunMetadata metadata, CancellationToken cancellationToken)
        {
            SavedStates.Add(metadata.State);
            return Task.CompletedTask;
        }

        public Task<RunMetadata?> GetAsync(TaskId id, CancellationToken cancellationToken) =>
            Task.FromResult<RunMetadata?>(null);

        public Task<IReadOnlyList<RunMetadata>> ListAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunMetadata>>([]);
    }
}
