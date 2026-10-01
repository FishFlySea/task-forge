namespace TaskForge.Core;

public sealed record ExplorationResult
{
    public required IReadOnlyList<string> RelevantFiles { get; init; }

    public required IReadOnlyList<string> Observations { get; init; }

    public required IReadOnlyList<TestTarget> TestTargets { get; init; }

    public required double Confidence { get; init; }
}
