using TaskForge.Core;

namespace TaskForge.Application;

public static class TaskPacketFactory
{
    public static TaskPacket Create(
        TaskRequest request,
        PlanResult plan,
        ExplorationResult exploration) =>
        new()
        {
            Goal = request.Goal,
            Constraints = [],
            AcceptanceCriteria = plan.AcceptanceCriteria
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToArray(),
            RelevantFiles = exploration.RelevantFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Observations = exploration.Observations
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToArray(),
            TestTargets = exploration.TestTargets
                .Distinct()
                .ToArray()
        };
}
