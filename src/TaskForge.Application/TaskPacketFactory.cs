using TaskForge.Core;

namespace TaskForge.Application;

public static class TaskPacketFactory
{
    public static TaskPacket Create(
        TaskRequest request,
        PlanResult plan,
        ExplorationResult exploration,
        ContextCollectionResult context,
        string baseCommit,
        CodexBudgetOptions codexBudget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            baseCommit);

        return new TaskPacket
        {
            SchemaVersion = 3,
            BaseCommit = baseCommit,
            Goal = request.Goal,
            Constraints = [],
            AcceptanceCriteria = plan.AcceptanceCriteria
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToArray(),
            RelevantFiles = exploration.RelevantFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            ContextSpans = context.Spans,
            Budget = context.Budget,
            ExecutionBudget =
                TaskExecutionBudgetPolicy.Snapshot(
                    codexBudget),
            AllowedCommands =
                TaskCommandPolicy.Derive(
                    exploration.TestTargets),
            Observations = exploration.Observations
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToArray(),
            WriteScope = WriteScopePolicy.Derive(
                exploration.RelevantFiles,
                exploration.TestTargets),
            TestTargets = exploration.TestTargets
                .Distinct()
                .ToArray()
        };
    }
}
