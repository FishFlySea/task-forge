using TaskForge.Core;

namespace TaskForge.Application;

public sealed record RunInspection
{
    public required RunMetadata Metadata { get; init; }

    public TaskRequest? Request { get; init; }

    public PlanResult? Plan { get; init; }

    public ExplorationResult? Exploration { get; init; }

    public TaskPacket? TaskPacket { get; init; }

    public required bool CanApply { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}
