namespace TaskForge.Core;

public sealed record ReviewResult
{
    public required bool Acceptable { get; init; }

    public required IReadOnlyList<string> Findings { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
