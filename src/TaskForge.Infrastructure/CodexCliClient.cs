using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class CodexCliClient(
    CodexCliOptions options) : ICodexClient
{
    private readonly CodexCliOptions _options =
        Validate(
            options
            ?? throw new ArgumentNullException(
                nameof(options)));

    public async Task<CodexRunResult> ExecuteAsync(
        CodexRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Workspace);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Prompt);

        var tokenBudget =
            request.Kind == CodexRunKind.Implementation
                ? _options.ImplementationTokenBudget
                : _options.CorrectionTokenBudget;

        var arguments = BuildArguments(
            request,
            tokenBudget);

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
            result.StandardError,
            CodexJsonlUsageParser.Parse(
                result.StandardOutput));
    }

    internal static IReadOnlyList<string> BuildArguments(
        CodexRunRequest request,
        int tokenBudget) =>
        [
            "--disable",
            "multi_agent",
            "-c",
            "features.rollout_budget.enabled=true",
            "-c",
            $"features.rollout_budget.limit_tokens={tokenBudget}",
            "exec",
            "--json",
            "--ephemeral",
            "--sandbox",
            "workspace-write",
            request.Prompt
        ];

    private static CodexCliOptions Validate(
        CodexCliOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.Executable);

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Codex timeout must be positive.");
        }

        if (options.ImplementationTokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Implementation token budget must be positive.");
        }

        if (options.CorrectionTokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Correction token budget must be positive.");
        }

        return options;
    }
}
