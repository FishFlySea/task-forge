namespace TaskForge.Infrastructure;

public sealed record CodexCliOptions
{
    public string Executable { get; init; } = "codex";

    public TimeSpan Timeout { get; init; } =
        TimeSpan.FromMinutes(20);
}
