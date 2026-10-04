using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskExecutionBudgetPolicyTests
{
    [Fact]
    public void Equal_or_stricter_runtime_is_allowed()
    {
        var packet =
            TaskExecutionBudgetPolicy.Snapshot(
                new CodexBudgetOptions
                {
                    MaxCodexRuns = 2,
                    MaxConcurrentCodexRuns = 1,
                    RunTimeout = TimeSpan.FromMinutes(20),
                    ImplementationTokenBudget = 40_000,
                    CorrectionTokenBudget = 20_000
                });

        var errors =
            TaskExecutionBudgetPolicy.ValidateRuntimeWithinPacket(
                packet,
                new CodexBudgetOptions
                {
                    MaxCodexRuns = 1,
                    MaxConcurrentCodexRuns = 1,
                    RunTimeout = TimeSpan.FromMinutes(10),
                    ImplementationTokenBudget = 20_000,
                    CorrectionTokenBudget = 10_000
                });

        Assert.Empty(
            errors);
    }

    [Fact]
    public void Looser_runtime_is_rejected()
    {
        var packet =
            TaskExecutionBudgetPolicy.Snapshot(
                new CodexBudgetOptions
                {
                    MaxCodexRuns = 2,
                    MaxConcurrentCodexRuns = 1,
                    RunTimeout = TimeSpan.FromMinutes(20),
                    ImplementationTokenBudget = 40_000,
                    CorrectionTokenBudget = 20_000
                });

        var errors =
            TaskExecutionBudgetPolicy.ValidateRuntimeWithinPacket(
                packet,
                new CodexBudgetOptions
                {
                    MaxCodexRuns = 3,
                    MaxConcurrentCodexRuns = 2,
                    RunTimeout = TimeSpan.FromMinutes(30),
                    ImplementationTokenBudget = 50_000,
                    CorrectionTokenBudget = 25_000
                });

        Assert.Equal(
            5,
            errors.Count);
    }
}
