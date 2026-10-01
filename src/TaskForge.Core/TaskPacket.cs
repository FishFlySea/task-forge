namespace TaskForge.Core;

public sealed record TaskPacket
{
    public required string Goal { get; init; }

    public required IReadOnlyList<string> Constraints { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }

    public required IReadOnlyList<string> RelevantFiles { get; init; }

    public required IReadOnlyList<string> Observations { get; init; }

    public required IReadOnlyList<TestTarget> TestTargets { get; init; }

    public string? Diagnostics { get; init; }
}
