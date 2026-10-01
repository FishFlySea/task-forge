namespace TaskForge.Core;

public sealed record AgentBudgetOptions
{
    public int MaxCodexRunsPerTask { get; init; } = 2;

    public int MaxCodexRetries { get; init; } = 1;

    public int MaxConcurrentCodexRuns { get; init; } = 1;
}
