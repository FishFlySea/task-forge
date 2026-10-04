namespace TaskForge.Core;

public sealed record ContextSpan
{
    public required string Path { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }

    public required string Content { get; init; }
}
