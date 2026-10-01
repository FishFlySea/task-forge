using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class CodexRunGateTests
{
    [Fact]
    public async Task Second_correction_is_blocked()
    {
        using var gate =
            new CodexRunGate(
                new AgentBudgetOptions
                {
                    MaxCodexRunsPerTask = 2,
                    MaxCodexRetries = 1,
                    MaxConcurrentCodexRuns = 1
                });

        await Assert.ThrowsAsync<CodexBudgetExceededException>(
            async () =>
            {
                await using var lease =
                    await gate.AcquireAsync(
                        new TaskId("tf-test"),
                        currentRuns: 2,
                        CodexRunKind.Correction,
                        CancellationToken.None);
            });
    }

    [Fact]
    public async Task Second_implementation_is_blocked()
    {
        using var gate =
            new CodexRunGate(
                new AgentBudgetOptions());

        await Assert.ThrowsAsync<CodexBudgetExceededException>(
            async () =>
            {
                await using var lease =
                    await gate.AcquireAsync(
                        new TaskId("tf-test"),
                        currentRuns: 1,
                        CodexRunKind.Implementation,
                        CancellationToken.None);
            });
    }
}
