namespace TaskForge.Core;

public sealed record CodexRunResult(
    int ExitCode,
    string Jsonl,
    string StandardError)
{
    public bool Success => ExitCode == 0;
}
