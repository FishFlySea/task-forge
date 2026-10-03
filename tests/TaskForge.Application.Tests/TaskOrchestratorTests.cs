using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class TaskOrchestratorTests
{
    [Fact]
    public async Task Plan_stops_before_codex_and_can_be_applied_later()
    {
        var store =
            new FakeRunStore();

        var planner =
            new FakePlannerAgent();

        var explorer =
            new FakeExplorerAgent();

        var implementer =
            new FakeImplementerAgent();

        using var gate =
            new CodexRunGate(
                new AgentBudgetOptions());

        var workspaceManager =
            new FakeWorkspaceManager();

        var orchestrator =
            CreateOrchestrator(
                store,
                planner,
                explorer,
                implementer,
                gate,
                workspaceManager);

        var request =
            new TaskRequest(
                "Implement planner",
                Directory.GetCurrentDirectory());

        var planned =
            await orchestrator.PlanAsync(
                request,
                CancellationToken.None);

        Assert.Equal(
            WorkflowState.ReadyToApply,
            planned.State);

        Assert.Equal(
            0,
            store.LastMetadata?.CodexRuns);

        Assert.Equal(
            0,
            implementer.Calls);

        Assert.Equal(
            1,
            planner.Calls);

        Assert.Equal(
            1,
            explorer.Calls);

        var applied =
            await orchestrator.ApplyAsync(
                planned.Id,
                CancellationToken.None);

        Assert.Equal(
            WorkflowState.Completed,
            applied.State);

        Assert.Equal(
            1,
            implementer.Calls);

        Assert.Equal(
            1,
            planner.Calls);

        Assert.Equal(
            1,
            explorer.Calls);

        Assert.Equal(
            1,
            store.LastMetadata?.CodexRuns);

        Assert.Equal(
            1,
            workspaceManager.CreateCalls);

        Assert.Equal(
            1,
            workspaceManager.CleanupCalls);

        Assert.Equal(
            workspaceManager.WorkspacePath,
            implementer.LastWorkspace);
    }

    [Fact]
    public async Task Write_scope_violation_stops_before_build()
    {
        var store =
            new FakeRunStore();

        var dotnetRunner =
            new FakeDotnetRunner();

        using var gate =
            new CodexRunGate(
                new AgentBudgetOptions());

        var orchestrator =
            CreateOrchestrator(
                store,
                new FakePlannerAgent(),
                new FakeExplorerAgent(),
                new FakeImplementerAgent(),
                gate,
                new FakeWorkspaceManager(
                    ["README.md"]),
                dotnetRunner);

        var result =
            await orchestrator.RunAsync(
                new TaskRequest(
                    "Implement planner",
                    Directory.GetCurrentDirectory()),
                CancellationToken.None);

        Assert.Equal(
            WorkflowState.NeedsUser,
            result.State);

        Assert.Equal(
            0,
            dotnetRunner.BuildCalls);

        Assert.Contains(
            "write-scope-violation-01.json",
            store.JsonArtifacts);

        Assert.Contains(
            "README.md",
            result.Message);
    }

    [Fact]
    public async Task Run_keeps_one_shot_behavior()
    {
        var store =
            new FakeRunStore();

        using var gate =
            new CodexRunGate(
                new AgentBudgetOptions());

        var orchestrator =
            CreateOrchestrator(
                store,
                new FakePlannerAgent(),
                new FakeExplorerAgent(),
                new FakeImplementerAgent(),
                gate,
                new FakeWorkspaceManager());

        var result =
            await orchestrator.RunAsync(
                new TaskRequest(
                    "Implement planner",
                    Directory.GetCurrentDirectory()),
                CancellationToken.None);

        Assert.Equal(
            WorkflowState.Completed,
            result.State);

        Assert.Equal(
            1,
            store.LastMetadata?.CodexRuns);

        Assert.Equal(
            1200,
            store.LastMetadata?.CodexInputTokens);

        Assert.Contains(
            "codex-01.jsonl",
            store.TextArtifacts);

        Assert.Contains(
            "review.json",
            store.JsonArtifacts);
    }

    private static TaskOrchestrator CreateOrchestrator(
        FakeRunStore store,
        FakePlannerAgent planner,
        FakeExplorerAgent explorer,
        FakeImplementerAgent implementer,
        CodexRunGate gate,
        FakeWorkspaceManager workspaceManager,
        FakeDotnetRunner? dotnetRunner = null) =>
        new(
            store,
            planner,
            explorer,
            implementer,
            gate,
            dotnetRunner ?? new FakeDotnetRunner(),
            new FakeGitClient(),
            workspaceManager,
            new FakeDiagnosticAgent(),
            new FakeReviewAgent(),
            TimeProvider.System);

    private sealed class FakePlannerAgent :
        IPlannerAgent
    {
        public int Calls { get; private set; }

        public Task<PlanResult> PlanAsync(
            TaskRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(
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
    }

    private sealed class FakeExplorerAgent :
        IExplorerAgent
    {
        public int Calls { get; private set; }

        public Task<ExplorationResult> ExploreAsync(
            TaskRequest request,
            PlanResult plan,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(
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
    }

    private sealed class FakeImplementerAgent :
        IImplementerAgent
    {
        public int Calls { get; private set; }

        public string? LastWorkspace { get; private set; }

        public Task<CodexRunResult> ExecuteAsync(
            TaskPacket taskPacket,
            string workspace,
            CodexRunKind kind,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastWorkspace = workspace;

            return Task.FromResult(
                new CodexRunResult(
                    0,
                    "{\"type\":\"done\"}",
                    string.Empty,
                    new CodexUsage(
                        InputTokens: 1200,
                        CachedInputTokens: 800,
                        CacheWriteInputTokens: 100,
                        OutputTokens: 250,
                        ReasoningOutputTokens: 90)));
        }
    }

    private sealed class FakeDotnetRunner :
        IDotnetRunner
    {
        public int BuildCalls { get; private set; }

        public int TestCalls { get; private set; }

        public Task<ProcessResult> BuildAsync(
            string workspace,
            CancellationToken cancellationToken)
        {
            BuildCalls++;

            return Task.FromResult(
                new ProcessResult(
                    0,
                    "build ok",
                    string.Empty));
        }

        public Task<ProcessResult> TestAsync(
            string workspace,
            IReadOnlyList<TestTarget> targets,
            CancellationToken cancellationToken)
        {
            TestCalls++;

            return Task.FromResult(
                new ProcessResult(
                    0,
                    "tests ok",
                    string.Empty));
        }
    }

    private sealed class FakeGitClient :
        IGitClient
    {
        private const string Head =
            "0123456789abcdef0123456789abcdef01234567";

        public Task<string> GetHeadCommitAsync(
            string repositoryPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Head);

        public Task<bool> IsWorkingTreeCleanAsync(
            string repositoryPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                true);

        public Task<bool> CommitExistsAsync(
            string repositoryPath,
            string commit,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                string.Equals(
                    commit,
                    Head,
                    StringComparison.Ordinal));
    }

    private sealed class FakeWorkspaceManager(
        IReadOnlyList<string>? changedFiles = null) :
        IWorkspaceManager
    {
        private readonly IReadOnlyList<string> _changedFiles =
            changedFiles
            ?? ["src/TaskForge.Application/PlannerAgent.cs"];

        public string WorkspacePath { get; } =
            Directory.GetCurrentDirectory();

        public int CreateCalls { get; private set; }

        public int CleanupCalls { get; private set; }

        public Task<WorkspaceHandle> CreateAsync(
            string repositoryPath,
            string baseCommit,
            TaskId taskId,
            CancellationToken cancellationToken)
        {
            CreateCalls++;

            return Task.FromResult(
                new WorkspaceHandle(
                    repositoryPath,
                    WorkspacePath,
                    baseCommit));
        }

        public Task<WorkspaceSnapshot> SnapshotAsync(
            WorkspaceHandle workspace,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new WorkspaceSnapshot
                {
                    ChangedFiles =
                        _changedFiles,
                    UntrackedFiles = [],
                    Status =
                        string.Join(
                            Environment.NewLine,
                            _changedFiles.Select(
                                x => $" M {x}")),
                    Diff =
                        string.Join(
                            Environment.NewLine,
                            _changedFiles.Select(
                                x => $"diff --git a/{x} b/{x}"))
                });

        public Task CleanupAsync(
            WorkspaceHandle workspace,
            CancellationToken cancellationToken)
        {
            CleanupCalls++;
            return Task.CompletedTask;
        }
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
        private readonly Dictionary<string, object> _artifacts =
            new(StringComparer.OrdinalIgnoreCase);

        public RunMetadata? LastMetadata { get; private set; }

        public List<string> JsonArtifacts { get; } = [];

        public List<string> TextArtifacts { get; } = [];

        public Task CreateAsync(
            TaskRequest request,
            RunMetadata metadata,
            CancellationToken cancellationToken)
        {
            LastMetadata = metadata;
            _artifacts["request.json"] = request;

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

            _artifacts[fileName] =
                value!;

            return Task.CompletedTask;
        }

        public Task<T?> LoadArtifactAsync<T>(
            TaskId id,
            string fileName,
            CancellationToken cancellationToken)
            where T : class
        {
            return Task.FromResult(
                _artifacts.TryGetValue(
                    fileName,
                    out var value)
                    ? value as T
                    : null);
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
                LastMetadata?.Id == id
                    ? LastMetadata
                    : null);

        public Task<IReadOnlyList<RunMetadata>> ListAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunMetadata>>(
                LastMetadata is null
                    ? []
                    : [LastMetadata]);
    }
}
