using TaskForge.Application;
using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class CodexCliClientTests
{
    [Fact]
    public void Client_uses_shared_budget_validation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CodexCliClient(
                new CodexCliOptions(),
                new CodexBudgetOptions
                {
                    RunTimeout = TimeSpan.Zero
                }));
    }


    [Theory]
    [InlineData(CodexRunKind.Implementation, 40000)]
    [InlineData(CodexRunKind.Correction, 20000)]
    public void Command_enforces_noninteractive_offline_workspace_policy_and_budget(
        CodexRunKind kind,
        int tokenBudget)
    {
        var request =
            new CodexRunRequest(
                "/repo",
                "implement the task",
                kind);

        var arguments =
            CodexCliClient.BuildArguments(
                request,
                tokenBudget);

        Assert.Equal(
            [
                "--disable",
                "multi_agent",
                "-c",
                "sandbox_workspace_write.network_access=false",
                "-c",
                "web_search=\"disabled\"",
                "-c",
                "features.rollout_budget.enabled=true",
                "-c",
                $"features.rollout_budget.limit_tokens={tokenBudget}",
                "--sandbox",
                "workspace-write",
                "--ask-for-approval",
                "never",
                "exec",
                "--json",
                "--ephemeral",
                "implement the task"
            ],
            arguments);
    }
}
