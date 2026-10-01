namespace TaskForge.Core;

public sealed record PlanResult
{
    public required string Summary { get; init; }

    public required IReadOnlyList<string> SearchTerms { get; init; }

    public required IReadOnlyList<string> LikelyAreas { get; init; }

    public required IReadOnlyList<string> AcceptanceCriteria { get; init; }
}
