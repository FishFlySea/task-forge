using TaskForge.Core;

namespace TaskForge.Application;

public sealed class TaskOrchestrator(IRunStore runStore, TimeProvider timeProvider)
{
    private readonly IRunStore _runStore = runStore;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<TaskRunResult> RunAsync(
        TaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryPath);

        var startedAt = _timeProvider.GetUtcNow();
        var metadata = new RunMetadata
        {
            Id = TaskId.New(startedAt),
            RepositoryPath = request.RepositoryPath,
            StartedAt = startedAt,
            State = WorkflowState.Created
        };

        await _runStore.CreateAsync(request, metadata, cancellationToken);
        metadata = await TransitionAsync(metadata, WorkflowState.Planning, cancellationToken);

        const string message =
            "Planner integration is not implemented yet. "
            + "The Phase 1 skeleton persisted the run successfully.";

        WorkflowStateMachine.EnsureCanTransition(metadata.State, WorkflowState.NeedsUser);

        metadata = metadata with
        {
            State = WorkflowState.NeedsUser,
            FinishedAt = _timeProvider.GetUtcNow(),
            Message = message
        };

        await _runStore.SaveMetadataAsync(metadata, cancellationToken);

        return new TaskRunResult(metadata.Id, metadata.State, message);
    }

    private async Task<RunMetadata> TransitionAsync(
        RunMetadata metadata,
        WorkflowState nextState,
        CancellationToken cancellationToken)
    {
        WorkflowStateMachine.EnsureCanTransition(metadata.State, nextState);
        metadata = metadata with { State = nextState };
        await _runStore.SaveMetadataAsync(metadata, cancellationToken);
        return metadata;
    }
}
