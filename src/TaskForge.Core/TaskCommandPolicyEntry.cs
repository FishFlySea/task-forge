namespace TaskForge.Core;

public sealed record TaskCommandPolicyEntry
{
    public required TaskCommandTool Tool { get; init; }

    public string? ProjectPath { get; init; }

    public string? Filter { get; init; }
}
