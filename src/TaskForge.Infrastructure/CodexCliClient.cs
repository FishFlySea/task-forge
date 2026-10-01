using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class CodexCliClient(
    CodexCliOptions options) : ICodexClient
{
    private readonly CodexCliOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    public async Task<CodexRunResult> ExecuteAsync(
        CodexRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Workspace);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Prompt);

        var arguments = new[]
        {
            "--disable",
            "multi_agent",
            "exec",
            "--json",
            "--ephemeral",
            "--sandbox",
            "workspace-write",
            request.Prompt
        };

        var result =
            await ExternalProcessRunner.RunAsync(
                _options.Executable,
                arguments,
                request.Workspace,
                _options.Timeout,
                cancellationToken);

        return new CodexRunResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }
}
