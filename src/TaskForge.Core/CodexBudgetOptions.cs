namespace TaskForge.Core;

public sealed record CodexBudgetOptions
{
    public int MaxCodexRuns { get; init; } = 2;

    public int MaxConcurrentCodexRuns { get; init; } = 1;

    public TimeSpan RunTimeout { get; init; } =
        TimeSpan.FromMinutes(20);

    public int ImplementationTokenBudget { get; init; } =
        40_000;

    public int CorrectionTokenBudget { get; init; } =
        20_000;
}
