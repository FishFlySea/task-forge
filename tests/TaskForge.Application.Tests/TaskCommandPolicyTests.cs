using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskCommandPolicyTests
{
    [Fact]
    public void Derive_creates_build_and_typed_test_commands()
    {
        var commands =
            TaskCommandPolicy.Derive(
                [
                    new TestTarget(
                        "tests/Operations.Tests/Operations.Tests.csproj",
                        "FullyQualifiedName~Cleanup")
                ]);

        Assert.Contains(
            commands,
            x => x.Tool
                 == TaskCommandTool.DotnetBuild);

        Assert.Contains(
            commands,
            x => x.Tool
                 == TaskCommandTool.DotnetTest
                 && x.ProjectPath
                    == "tests/Operations.Tests/Operations.Tests.csproj"
                 && x.Filter
                    == "FullyQualifiedName~Cleanup");
    }

    [Fact]
    public void Validate_rejects_missing_test_permission()
    {
        var packet =
            CreatePacket();

        packet =
            packet with
            {
                TestTargets =
                [
                    new TestTarget(
                        "tests/App.Tests/App.Tests.csproj")
                ]
            };

        var errors =
            TaskCommandPolicy.Validate(
                packet);

        Assert.Contains(
            errors,
            x => x.Contains(
                "does not allow test target",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../escape.csproj")]
    [InlineData("/tmp/test.csproj")]
    [InlineData("C:\\temp\\test.csproj")]
    public void Validate_rejects_unsafe_project_paths(
        string path)
    {
        var packet =
            CreatePacket()
            with
            {
                AllowedCommands =
                [
                    new TaskCommandPolicyEntry
                    {
                        Tool =
                            TaskCommandTool.DotnetBuild
                    },
                    new TaskCommandPolicyEntry
                    {
                        Tool =
                            TaskCommandTool.DotnetTest,
                        ProjectPath =
                            path
                    }
                ]
            };

        var errors =
            TaskCommandPolicy.Validate(
                packet);

        Assert.Contains(
            errors,
            x => x.Contains(
                "invalid project path",
                StringComparison.OrdinalIgnoreCase));
    }

    private static TaskPacket CreatePacket() =>
        new()
        {
            SchemaVersion = 3,
            BaseCommit =
                "0123456789abcdef0123456789abcdef01234567",
            Goal = "test",
            Constraints = [],
            AcceptanceCriteria =
            [
                "pass"
            ],
            RelevantFiles =
            [
                "src/Test.cs"
            ],
            ContextSpans =
            [
                new ContextSpan
                {
                    Path = "src/Test.cs",
                    StartLine = 1,
                    EndLine = 1,
                    Content = "class Test {}"
                }
            ],
            Budget =
                new TaskPacketBudget
                {
                    MaxContextCharacters = 100,
                    UsedContextCharacters = 13,
                    EstimatedContextTokens = 4
                },
            ExecutionBudget =
                new TaskExecutionBudget
                {
                    MaxCodexRuns = 2,
                    MaxConcurrentCodexRuns = 1,
                    RunTimeoutSeconds = 1200,
                    ImplementationTokenBudget = 40_000,
                    CorrectionTokenBudget = 20_000
                },
            AllowedCommands =
            [
                new TaskCommandPolicyEntry
                {
                    Tool =
                        TaskCommandTool.DotnetBuild
                }
            ],
            Observations = [],
            WriteScope =
            [
                "src/**"
            ],
            TestTargets = []
        };
}
