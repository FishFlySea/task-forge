namespace TaskForge.Core;

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Success => ExitCode == 0;

    public string CombinedOutput =>
        string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
}
