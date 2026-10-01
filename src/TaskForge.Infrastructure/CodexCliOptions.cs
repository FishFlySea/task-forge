namespace TaskForge.Infrastructure;

public sealed record CodexCliOptions
{
    public string Executable { get; init; } = "codex";

    public TimeSpan Timeout { get; init; } =
        TimeSpan.FromMinutes(20);

    public int ImplementationTokenBudget { get; init; } =
        40_000;

    public int CorrectionTokenBudget { get; init; } =
        20_000;
}
