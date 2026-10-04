namespace TaskForge.Core;

public sealed record TaskPacketBudget
{
    public required int MaxContextCharacters { get; init; }

    public required int UsedContextCharacters { get; init; }

    public required int EstimatedContextTokens { get; init; }
}
