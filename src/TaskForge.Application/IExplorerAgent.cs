using TaskForge.Core;

namespace TaskForge.Application;

public interface IExplorerAgent
{
    Task<ExplorationResult> ExploreAsync(
        TaskRequest request,
        PlanResult plan,
        CancellationToken cancellationToken);
}
