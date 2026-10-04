using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskPacketFactoryTests
{
    [Fact]
    public void Creates_schema_v2_packet_with_bounded_context()
    {
        var context =
            new ContextCollectionResult
            {
                Spans =
                [
                    new ContextSpan
                    {
                        Path = "src/Test.cs",
                        StartLine = 10,
                        EndLine = 20,
                        Content = "class Test { }"
                    }
                ],
                Budget = new TaskPacketBudget
                {
                    MaxContextCharacters = 32_000,
                    UsedContextCharacters = 14,
                    EstimatedContextTokens = 4
                }
            };

        var packet =
            TaskPacketFactory.Create(
                new TaskRequest(
                    "Fix Test",
                    Directory.GetCurrentDirectory()),
                new PlanResult
                {
                    Summary = "Fix",
                    SearchTerms = ["Test"],
                    LikelyAreas = ["src"],
                    AcceptanceCriteria =
                    [
                        "Tests pass"
                    ]
                },
                new ExplorationResult
                {
                    RelevantFiles =
                    [
                        "src/Test.cs"
                    ],
                    Observations =
                    [
                        "Test is relevant"
                    ],
                    TestTargets = [],
                    Confidence = 1
                },
                context,
                "0123456789abcdef0123456789abcdef01234567",
                new CodexBudgetOptions());

        Assert.Equal(
            3,
            packet.SchemaVersion);

        Assert.Same(
            context.Budget,
            packet.Budget);

        Assert.Equal(
            context.Spans,
            packet.ContextSpans);

        Assert.NotEmpty(
            packet.WriteScope);

        Assert.NotNull(
            packet.ExecutionBudget);

        Assert.Equal(
            2,
            packet.ExecutionBudget!.MaxCodexRuns);

        Assert.Contains(
            packet.AllowedCommands,
            x => x.Tool
                 == TaskCommandTool.DotnetBuild);
    }
}
