namespace TaskForge.Core;

public sealed record DiagnosticResult
{
    public required string Summary { get; init; }

    public required bool RequiresCodexCorrection { get; init; }

    public required bool NeedsUser { get; init; }
}
