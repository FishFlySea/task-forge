namespace TaskForge.Core;

public sealed record WorkspaceSnapshot
{
    public required IReadOnlyList<string> ChangedFiles { get; init; }

    public required IReadOnlyList<string> UntrackedFiles { get; init; }

    public required string Status { get; init; }

    public required string Diff { get; init; }
}
