namespace TaskForge.Core;

public sealed record CodexRunResult(
    int ExitCode,
    string Jsonl,
    string StandardError,
    CodexUsage? Usage = null)
{
    public bool Success => ExitCode == 0;
}
