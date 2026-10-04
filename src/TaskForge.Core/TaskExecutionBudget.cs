namespace TaskForge.Core;

public sealed record TaskExecutionBudget
{
    public required int MaxCodexRuns { get; init; }

    public required int MaxConcurrentCodexRuns { get; init; }

    public required long RunTimeoutSeconds { get; init; }

    public required int ImplementationTokenBudget { get; init; }

    public required int CorrectionTokenBudget { get; init; }
}
