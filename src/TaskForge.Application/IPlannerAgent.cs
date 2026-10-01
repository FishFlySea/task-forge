using TaskForge.Core;

namespace TaskForge.Application;

public interface IPlannerAgent
{
    Task<PlanResult> PlanAsync(
        TaskRequest request,
        CancellationToken cancellationToken);
}
