using TaskForge.Core;

namespace TaskForge.Application;

public sealed record ContextCollectionResult
{
    public required IReadOnlyList<ContextSpan> Spans { get; init; }

    public required TaskPacketBudget Budget { get; init; }
}
