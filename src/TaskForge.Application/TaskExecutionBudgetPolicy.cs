using TaskForge.Core;

namespace TaskForge.Application;

public static class TaskExecutionBudgetPolicy
{
    public static TaskExecutionBudget Snapshot(
        CodexBudgetOptions options)
    {
        ValidateRuntimeOptions(
            options);

        return new TaskExecutionBudget
        {
            MaxCodexRuns =
                options.MaxCodexRuns,
            MaxConcurrentCodexRuns =
                options.MaxConcurrentCodexRuns,
            RunTimeoutSeconds =
                (long)Math.Ceiling(
                    options.RunTimeout.TotalSeconds),
            ImplementationTokenBudget =
                options.ImplementationTokenBudget,
            CorrectionTokenBudget =
                options.CorrectionTokenBudget
        };
    }

    public static IReadOnlyList<string> ValidatePacket(
        TaskExecutionBudget? budget)
    {
        if (budget is null)
        {
            return
            [
                "TaskPacket has no execution budget."
            ];
        }

        var errors =
            new List<string>();

        if (budget.MaxCodexRuns <= 0)
        {
            errors.Add(
                "TaskPacket MaxCodexRuns must be positive.");
        }

        if (budget.MaxConcurrentCodexRuns <= 0)
        {
            errors.Add(
                "TaskPacket MaxConcurrentCodexRuns must be positive.");
        }

        if (budget.RunTimeoutSeconds <= 0)
        {
            errors.Add(
                "TaskPacket RunTimeoutSeconds must be positive.");
        }

        if (budget.ImplementationTokenBudget <= 0)
        {
            errors.Add(
                "TaskPacket implementation token budget must be positive.");
        }

        if (budget.CorrectionTokenBudget <= 0)
        {
            errors.Add(
                "TaskPacket correction token budget must be positive.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateRuntimeWithinPacket(
        TaskExecutionBudget? packetBudget,
        CodexBudgetOptions runtime)
    {
        var errors =
            ValidatePacket(
                    packetBudget)
                .ToList();

        if (packetBudget is null)
        {
            return errors;
        }

        ValidateRuntimeOptions(
            runtime);

        if (runtime.MaxCodexRuns
            > packetBudget.MaxCodexRuns)
        {
            errors.Add(
                $"Runtime MaxCodexRuns ({runtime.MaxCodexRuns}) exceeds the planned packet limit ({packetBudget.MaxCodexRuns}).");
        }

        if (runtime.MaxConcurrentCodexRuns
            > packetBudget.MaxConcurrentCodexRuns)
        {
            errors.Add(
                $"Runtime MaxConcurrentCodexRuns ({runtime.MaxConcurrentCodexRuns}) exceeds the planned packet limit ({packetBudget.MaxConcurrentCodexRuns}).");
        }

        if (runtime.RunTimeout.TotalSeconds
            > packetBudget.RunTimeoutSeconds)
        {
            errors.Add(
                $"Runtime Codex timeout ({runtime.RunTimeout.TotalSeconds:0}s) exceeds the planned packet limit ({packetBudget.RunTimeoutSeconds}s).");
        }

        if (runtime.ImplementationTokenBudget
            > packetBudget.ImplementationTokenBudget)
        {
            errors.Add(
                $"Runtime implementation token budget ({runtime.ImplementationTokenBudget}) exceeds the planned packet limit ({packetBudget.ImplementationTokenBudget}).");
        }

        if (runtime.CorrectionTokenBudget
            > packetBudget.CorrectionTokenBudget)
        {
            errors.Add(
                $"Runtime correction token budget ({runtime.CorrectionTokenBudget}) exceeds the planned packet limit ({packetBudget.CorrectionTokenBudget}).");
        }

        return errors;
    }

    private static void ValidateRuntimeOptions(
        CodexBudgetOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        if (options.MaxCodexRuns <= 0
            || options.MaxConcurrentCodexRuns <= 0
            || options.RunTimeout <= TimeSpan.Zero
            || options.ImplementationTokenBudget <= 0
            || options.CorrectionTokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Codex budget options must contain positive limits.");
        }
    }
}
