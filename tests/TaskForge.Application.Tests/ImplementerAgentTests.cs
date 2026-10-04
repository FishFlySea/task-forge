using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class ImplementerAgentTests
{
    [Fact]
    public async Task Includes_prepared_context_spans_in_codex_prompt()
    {
        var codex =
            new FakeCodexClient();

        var sut =
            new ImplementerAgent(
                codex);

        var packet =
            new TaskPacket
            {
                SchemaVersion = 2,
                BaseCommit =
                    "0123456789abcdef0123456789abcdef01234567",
                Goal =
                    "Fix cleanup",
                Constraints = [],
                AcceptanceCriteria =
                [
                    "Cleanup works"
                ],
                RelevantFiles =
                [
                    "src/CleanupService.cs"
                ],
                ContextSpans =
                [
                    new ContextSpan
                    {
                        Path =
                            "src/CleanupService.cs",
                        StartLine = 40,
                        EndLine = 52,
                        Content =
                            "public void Cleanup() => RemoveBindings();"
                    }
                ],
                Budget =
                    new TaskPacketBudget
                    {
                        MaxContextCharacters = 32_000,
                        UsedContextCharacters = 42,
                        EstimatedContextTokens = 11
                    },
                Observations =
                [
                    "Cleanup currently misses bindings."
                ],
                WriteScope =
                [
                    "src/**"
                ],
                TestTargets = []
            };

        await sut.ExecuteAsync(
            packet,
            Directory.GetCurrentDirectory(),
            CodexRunKind.Implementation,
            CancellationToken.None);

        Assert.NotNull(
            codex.LastRequest);

        Assert.Contains(
            "--- src/CleanupService.cs:40-52 ---",
            codex.LastRequest!.Prompt);

        Assert.Contains(
            "public void Cleanup() => RemoveBindings();",
            codex.LastRequest.Prompt);
    }

    private sealed class FakeCodexClient :
        ICodexClient
    {
        public CodexRunRequest? LastRequest { get; private set; }

        public Task<CodexRunResult> ExecuteAsync(
            CodexRunRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(
                new CodexRunResult(
                    0,
                    string.Empty,
                    string.Empty));
        }
    }
}
