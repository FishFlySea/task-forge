namespace TaskForge.Core;

public sealed record RunMetadata
{
    public required TaskId Id { get; init; }

    public required string RepositoryPath { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public WorkflowState State { get; init; }

    public int CodexRuns { get; init; }

    public long? CodexInputTokens { get; init; }

    public long? CodexCachedInputTokens { get; init; }

    public long? CodexCacheWriteInputTokens { get; init; }

    public long? CodexOutputTokens { get; init; }

    public long? CodexReasoningOutputTokens { get; init; }

    public string? Message { get; init; }
}
