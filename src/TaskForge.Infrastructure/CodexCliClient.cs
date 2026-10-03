using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class CodexCliClient : ICodexClient
{
    private readonly CodexCliOptions _options;
    private readonly CodexBudgetOptions _budget;

    public CodexCliClient(
        CodexCliOptions options,
        CodexBudgetOptions budget)
    {
        _options =
            ValidateOptions(
                options
                ?? throw new ArgumentNullException(
                    nameof(options)));

        _budget =
            ValidateBudget(
                budget
                ?? throw new ArgumentNullException(
                    nameof(budget)));
    }

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
                ? _budget.ImplementationTokenBudget
                : _budget.CorrectionTokenBudget;

        var arguments =
            BuildArguments(
                request,
                tokenBudget);

        var result =
            await ExternalProcessRunner.RunAsync(
                _options.Executable,
                arguments,
                request.Workspace,
                _budget.RunTimeout,
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

    private static CodexCliOptions ValidateOptions(
        CodexCliOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.Executable);

        return options;
    }

    private static CodexBudgetOptions ValidateBudget(
        CodexBudgetOptions budget)
    {
        if (budget.RunTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budget),
                "Codex run timeout must be positive.");
        }

        if (budget.ImplementationTokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budget),
                "Implementation token budget must be positive.");
        }

        if (budget.CorrectionTokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budget),
                "Correction token budget must be positive.");
        }

        return budget;
    }
}
