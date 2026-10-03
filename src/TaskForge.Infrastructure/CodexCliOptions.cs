namespace TaskForge.Infrastructure;

public sealed record CodexCliOptions
{
    public string Executable { get; init; } = "codex";
}
