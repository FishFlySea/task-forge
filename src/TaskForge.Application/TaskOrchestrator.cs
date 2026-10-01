using TaskForge.Core;

namespace TaskForge.Application;

public sealed class TaskOrchestrator(
    IRunStore runStore,
    IPlannerAgent plannerAgent,
    IExplorerAgent explorerAgent,
    TimeProvider timeProvider)
{
    private readonly IRunStore _runStore = runStore;
    private readonly IPlannerAgent _plannerAgent = plannerAgent;
    private readonly IExplorerAgent _explorerAgent = explorerAgent;
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

        try
        {
            metadata = await TransitionAsync(
                metadata,
                WorkflowState.Planning,
                cancellationToken);

            var plan = await _plannerAgent.PlanAsync(
                request,
                cancellationToken);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "plan.json",
                plan,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata,
                WorkflowState.Exploring,
                cancellationToken);

            var exploration = await _explorerAgent.ExploreAsync(
                request,
                plan,
                cancellationToken);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "exploration.json",
                exploration,
                cancellationToken);

            var taskPacket = TaskPacketFactory.Create(
                request,
                plan,
                exploration);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "task-packet.json",
                taskPacket,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata,
                WorkflowState.PacketReady,
                cancellationToken);

            const string message =
                "Task packet prepared successfully. "
                + "Codex implementation will be added in Phase 3.";

            metadata = await FinishAsync(
                metadata,
                WorkflowState.NeedsUser,
                message,
                cancellationToken);

            return new TaskRunResult(
                metadata.Id,
                metadata.State,
                message);
        }
        catch (OperationCanceledException)
        {
            await TryFinishAsync(
                metadata,
                WorkflowState.Cancelled,
                "Run cancelled.",
                CancellationToken.None);

            throw;
        }
        catch (Exception exception)
        {
            await TryFinishAsync(
                metadata,
                WorkflowState.Failed,
                exception.Message,
                CancellationToken.None);

            throw;
        }
    }

    private async Task<RunMetadata> TransitionAsync(
        RunMetadata metadata,
        WorkflowState nextState,
        CancellationToken cancellationToken)
    {
        WorkflowStateMachine.EnsureCanTransition(
            metadata.State,
            nextState);

        metadata = metadata with { State = nextState };

        await _runStore.SaveMetadataAsync(
            metadata,
            cancellationToken);

        return metadata;
    }

    private async Task<RunMetadata> FinishAsync(
        RunMetadata metadata,
        WorkflowState finalState,
        string message,
        CancellationToken cancellationToken)
    {
        WorkflowStateMachine.EnsureCanTransition(
            metadata.State,
            finalState);

        metadata = metadata with
        {
            State = finalState,
            FinishedAt = _timeProvider.GetUtcNow(),
            Message = message
        };

        await _runStore.SaveMetadataAsync(
            metadata,
            cancellationToken);

        return metadata;
    }

    private async Task TryFinishAsync(
        RunMetadata metadata,
        WorkflowState finalState,
        string message,
        CancellationToken cancellationToken)
    {
        if (!WorkflowStateMachine.CanTransition(
                metadata.State,
                finalState))
        {
            return;
        }

        try
        {
            await FinishAsync(
                metadata,
                finalState,
                message,
                cancellationToken);
        }
        catch
        {
            // Preserve the original workflow exception.
        }
    }
}
