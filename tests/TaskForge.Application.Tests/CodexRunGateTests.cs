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
                new CodexBudgetOptions
                {
                    MaxCodexRuns = 2,
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
    public async Task Correction_is_allowed_while_run_budget_remains()
    {
        using var gate =
            new CodexRunGate(
                new CodexBudgetOptions
                {
                    MaxCodexRuns = 3
                });

        await using var firstCorrection =
            await gate.AcquireAsync(
                new TaskId("tf-test"),
                currentRuns: 1,
                CodexRunKind.Correction,
                CancellationToken.None);

        await firstCorrection.DisposeAsync();

        await using var secondCorrection =
            await gate.AcquireAsync(
                new TaskId("tf-test"),
                currentRuns: 2,
                CodexRunKind.Correction,
                CancellationToken.None);
    }

    [Fact]
    public async Task Correction_before_implementation_is_rejected()
    {
        using var gate =
            new CodexRunGate(
                new CodexBudgetOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await using var lease =
                    await gate.AcquireAsync(
                        new TaskId("tf-test"),
                        currentRuns: 0,
                        CodexRunKind.Correction,
                        CancellationToken.None);
            });
    }

    [Fact]
    public async Task Second_implementation_is_blocked()
    {
        using var gate =
            new CodexRunGate(
                new CodexBudgetOptions());

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
