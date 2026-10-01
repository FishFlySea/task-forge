using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskOrchestratorTests
{
    [Fact]
    public async Task Happy_path_uses_one_codex_run_and_completes()
    {
        var store =
            new FakeRunStore();

        using var gate =
            new CodexRunGate(
                new AgentBudgetOptions());

        var orchestrator =
            new TaskOrchestrator(
                store,
                new FakePlannerAgent(),
                new FakeExplorerAgent(),
                new FakeImplementerAgent(),
                gate,
                new FakeDotnetRunner(),
                new FakeGitClient(),
                new FakeDiagnosticAgent(),
                new FakeReviewAgent(),
                TimeProvider.System);

        var result =
            await orchestrator.RunAsync(
                new TaskRequest(
                    "Implement planner",
                    "/tmp/repository"),
                CancellationToken.None);

        Assert.Equal(
            WorkflowState.Completed,
            result.State);

        Assert.Equal(
            1,
            store.LastMetadata?.CodexRuns);

        Assert.Contains(
            "codex-01.jsonl",
            store.TextArtifacts);

        Assert.Contains(
            "review.json",
            store.JsonArtifacts);
    }

    private sealed class FakePlannerAgent :
        IPlannerAgent
    {
        public Task<PlanResult> PlanAsync(
            TaskRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new PlanResult
                {
                    Summary = "Find planner code",
                    SearchTerms = ["Planner"],
                    LikelyAreas = ["Application"],
                    AcceptanceCriteria =
                    [
                        "Planner integration is available"
                    ]
                });
    }

    private sealed class FakeExplorerAgent :
        IExplorerAgent
    {
        public Task<ExplorationResult> ExploreAsync(
            TaskRequest request,
            PlanResult plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ExplorationResult
                {
                    RelevantFiles =
                    [
                        "src/TaskForge.Application/PlannerAgent.cs"
                    ],
                    Observations =
                    [
                        "Planner belongs to the application layer."
                    ],
                    TestTargets = [],
                    Confidence = 0.9
                });
    }

    private sealed class FakeImplementerAgent :
        IImplementerAgent
    {
        public Task<CodexRunResult> ExecuteAsync(
            TaskPacket taskPacket,
            string workspace,
            CodexRunKind kind,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new CodexRunResult(
                    0,
                    "{\"type\":\"done\"}",
                    string.Empty));
    }

    private sealed class FakeDotnetRunner :
        IDotnetRunner
    {
        public Task<ProcessResult> BuildAsync(
            string workspace,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ProcessResult(
                    0,
                    "build ok",
                    string.Empty));

        public Task<ProcessResult> TestAsync(
            string workspace,
            IReadOnlyList<TestTarget> targets,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ProcessResult(
                    0,
                    "tests ok",
                    string.Empty));
    }

    private sealed class FakeGitClient :
        IGitClient
    {
        public Task<string> GetWorkingTreeSnapshotAsync(
            string workspace,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                "M src/Test.cs");
    }

    private sealed class FakeDiagnosticAgent :
        IDiagnosticAgent
    {
        public Task<DiagnosticResult> DiagnoseAsync(
            TaskPacket taskPacket,
            ProcessResult failure,
            string gitSnapshot,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Diagnostic should not run on happy path.");
    }

    private sealed class FakeReviewAgent :
        IReviewAgent
    {
        public Task<ReviewResult> ReviewAsync(
            TaskPacket taskPacket,
            string gitSnapshot,
            ProcessResult buildResult,
            ProcessResult testResult,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ReviewResult
                {
                    Acceptable = true,
                    Findings = [],
                    Warnings = []
                });
    }

    private sealed class FakeRunStore :
        IRunStore
    {
        public RunMetadata? LastMetadata { get; private set; }

        public List<string> JsonArtifacts { get; } = [];

        public List<string> TextArtifacts { get; } = [];

        public Task CreateAsync(
            TaskRequest request,
            RunMetadata metadata,
            CancellationToken cancellationToken)
        {
            LastMetadata = metadata;
            return Task.CompletedTask;
        }

        public Task SaveMetadataAsync(
            RunMetadata metadata,
            CancellationToken cancellationToken)
        {
            LastMetadata = metadata;
            return Task.CompletedTask;
        }

        public Task SaveArtifactAsync<T>(
            TaskId id,
            string fileName,
            T value,
            CancellationToken cancellationToken)
        {
            JsonArtifacts.Add(
                fileName);

            return Task.CompletedTask;
        }

        public Task SaveTextArtifactAsync(
            TaskId id,
            string fileName,
            string content,
            CancellationToken cancellationToken)
        {
            TextArtifacts.Add(
                fileName);

            return Task.CompletedTask;
        }

        public Task<RunMetadata?> GetAsync(
            TaskId id,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                LastMetadata);

        public Task<IReadOnlyList<RunMetadata>> ListAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunMetadata>>(
                LastMetadata is null
                    ? []
                    : [LastMetadata]);
    }
}
