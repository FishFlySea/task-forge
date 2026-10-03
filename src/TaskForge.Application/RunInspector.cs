using TaskForge.Core;

namespace TaskForge.Application;

public sealed class RunInspector(
    IRunStore runStore)
{
    private readonly IRunStore _runStore =
        runStore;

    public async Task<RunInspection> InspectAsync(
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        var metadata =
            await _runStore.GetAsync(
                taskId,
                cancellationToken)
            ?? throw new ArgumentException(
                $"Run not found: {taskId}");

        var request =
            await _runStore.LoadArtifactAsync<TaskRequest>(
                taskId,
                "request.json",
                cancellationToken);

        var plan =
            await _runStore.LoadArtifactAsync<PlanResult>(
                taskId,
                "plan.json",
                cancellationToken);

        var exploration =
            await _runStore.LoadArtifactAsync<ExplorationResult>(
                taskId,
                "exploration.json",
                cancellationToken);

        var taskPacket =
            await _runStore.LoadArtifactAsync<TaskPacket>(
                taskId,
                "task-packet.json",
                cancellationToken);

        var warnings =
            BuildWarnings(
                metadata,
                request,
                plan,
                exploration,
                taskPacket);

        var canApply =
            !string.IsNullOrWhiteSpace(
                taskPacket?.BaseCommit)
            && taskPacket.WriteScope.Count > 0
            && metadata.State
                is WorkflowState.ReadyToApply
                or WorkflowState.PacketReady
            && metadata.CodexRuns == 0
            && request is not null
            && taskPacket is not null
            && Directory.Exists(
                metadata.RepositoryPath);

        return new RunInspection
        {
            Metadata = metadata,
            Request = request,
            Plan = plan,
            Exploration = exploration,
            TaskPacket = taskPacket,
            CanApply = canApply,
            Warnings = warnings
        };
    }

    private static IReadOnlyList<string> BuildWarnings(
        RunMetadata metadata,
        TaskRequest? request,
        PlanResult? plan,
        ExplorationResult? exploration,
        TaskPacket? taskPacket)
    {
        var warnings =
            new List<string>();

        if (request is null)
        {
            warnings.Add(
                "request.json is missing.");
        }

        if (plan is null)
        {
            warnings.Add(
                "plan.json is missing.");
        }

        if (exploration is null)
        {
            warnings.Add(
                "exploration.json is missing.");
        }

        if (taskPacket is null)
        {
            warnings.Add(
                "task-packet.json is missing.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(
                    taskPacket.BaseCommit))
            {
                warnings.Add(
                    "TaskPacket has no base commit and cannot be safely applied.");
            }

            if (taskPacket.WriteScope.Count == 0)
            {
                warnings.Add(
                    "TaskPacket has an empty write scope and cannot be safely applied.");
            }
        }

        if (!Directory.Exists(
                metadata.RepositoryPath))
        {
            warnings.Add(
                "Repository path no longer exists.");
        }

        if (metadata.CodexRuns > 0)
        {
            warnings.Add(
                "Codex has already been invoked for this run.");
        }

        if (metadata.State
            is not WorkflowState.ReadyToApply
            and not WorkflowState.PacketReady)
        {
            warnings.Add(
                $"Run state '{metadata.State}' is not applyable.");
        }

        if (taskPacket is not null
            && taskPacket.RelevantFiles.Count == 0)
        {
            warnings.Add(
                "TaskPacket contains no relevant files.");
        }

        if (taskPacket is not null
            && taskPacket.AcceptanceCriteria.Count == 0)
        {
            warnings.Add(
                "TaskPacket contains no acceptance criteria.");
        }

        return warnings;
    }
}
