using TaskForge.Application;
using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Cli;

internal static class Program
{
    public static async Task<int> Main(
        string[] args)
    {
        using var cancellation =
            new CancellationTokenSource();

        Console.CancelKeyPress +=
            (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 5;
            }

            var store =
                CreateRunStore();

            return args[0]
                .ToLowerInvariant() switch
            {
                "run" => await RunAsync(
                    store,
                    args[1..],
                    cancellation.Token),
                "plan" => await PlanAsync(
                    store,
                    args[1..],
                    cancellation.Token),
                "apply" => await ApplyAsync(
                    store,
                    args[1..],
                    cancellation.Token),
                "runs" => await ListRunsAsync(
                    store,
                    cancellation.Token),
                "show" => await ShowRunAsync(
                    store,
                    args[1..],
                    cancellation.Token),
                "inspect" => await InspectRunAsync(
                    store,
                    args[1..],
                    cancellation.Token),
                _ => UnknownCommand(
                    args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(
                "Cancelled.");

            return 4;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(
                exception.Message);

            return 5;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(
                exception.Message);

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                exception);

            return 1;
        }
    }

    private static JsonRunStore CreateRunStore()
    {
        var runsDirectory =
            Environment.GetEnvironmentVariable(
                "TASKFORGE_RUNS_DIRECTORY")
            ?? JsonRunStore
                .GetDefaultRunsDirectory();

        return new JsonRunStore(
            runsDirectory);
    }

    private static async Task<int> RunAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        var request =
            ParseTaskRequest(
                args,
                "run");

        using var runtime =
            CreateRuntime(
                runStore);

        var result =
            await runtime.Orchestrator.RunAsync(
                request,
                cancellationToken);

        PrintResult(
            result);

        return MapExitCode(
            result.State);
    }

    private static async Task<int> PlanAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        var request =
            ParseTaskRequest(
                args,
                "plan");

        using var runtime =
            CreateRuntime(
                runStore);

        var result =
            await runtime.Orchestrator.PlanAsync(
                request,
                cancellationToken);

        PrintResult(
            result);

        return MapExitCode(
            result.State);
    }

    private static async Task<int> ApplyAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1
            || string.IsNullOrWhiteSpace(
                args[0]))
        {
            throw new ArgumentException(
                "Usage: taskforge apply <run-id>");
        }

        using var runtime =
            CreateRuntime(
                runStore);

        var result =
            await runtime.Orchestrator.ApplyAsync(
                new TaskId(
                    args[0]),
                cancellationToken);

        PrintResult(
            result);

        return MapExitCode(
            result.State);
    }

    private static OrchestratorRuntime CreateRuntime(
        IRunStore runStore)
    {
        var ollamaUrl =
            Environment.GetEnvironmentVariable(
                "TASKFORGE_OLLAMA_URL")
            ?? "http://localhost:11434/";

        var ollamaModel =
            Environment.GetEnvironmentVariable(
                "TASKFORGE_OLLAMA_MODEL")
            ?? "qwen3-coder";

        var ollamaTimeout =
            GetPositiveIntEnvironmentVariable(
                "TASKFORGE_OLLAMA_TIMEOUT_SECONDS",
                120);

        var httpClient =
            new HttpClient
            {
                BaseAddress =
                    NormalizeBaseAddress(
                        ollamaUrl),
                Timeout =
                    TimeSpan.FromSeconds(
                        ollamaTimeout)
            };

        var localLlm =
            new OllamaClient(
                httpClient,
                ollamaModel);

        var codexOptions =
            new CodexCliOptions
            {
                Executable =
                    Environment.GetEnvironmentVariable(
                        "TASKFORGE_CODEX_EXECUTABLE")
                    ?? "codex",
                Timeout =
                    TimeSpan.FromSeconds(
                        GetPositiveIntEnvironmentVariable(
                            "TASKFORGE_CODEX_TIMEOUT_SECONDS",
                            1200)),
                ImplementationTokenBudget =
                    GetPositiveIntEnvironmentVariable(
                        "TASKFORGE_CODEX_IMPLEMENTATION_TOKEN_BUDGET",
                        40_000),
                CorrectionTokenBudget =
                    GetPositiveIntEnvironmentVariable(
                        "TASKFORGE_CODEX_CORRECTION_TOKEN_BUDGET",
                        20_000)
            };

        var codexGate =
            new CodexRunGate(
                new AgentBudgetOptions
                {
                    MaxCodexRunsPerTask = 2,
                    MaxCodexRetries = 1,
                    MaxConcurrentCodexRuns = 1
                });

        var orchestrator =
            new TaskOrchestrator(
                runStore,
                new PlannerAgent(
                    localLlm),
                new ExplorerAgent(
                    localLlm,
                    new FileSystemRepositorySearch()),
                new ImplementerAgent(
                    new CodexCliClient(
                        codexOptions)),
                codexGate,
                new DotnetRunner(
                    new DotnetRunnerOptions
                    {
                        Executable =
                            Environment.GetEnvironmentVariable(
                                "TASKFORGE_DOTNET_EXECUTABLE")
                            ?? "dotnet"
                    }),
                new GitClient(),
                new GitWorktreeManager(
                    Environment.GetEnvironmentVariable(
                        "TASKFORGE_WORKTREES_DIRECTORY")
                    ?? GitWorktreeManager.GetDefaultWorktreesDirectory()),
                new DiagnosticAgent(
                    localLlm),
                new ReviewAgent(
                    localLlm),
                TimeProvider.System);

        return new OrchestratorRuntime(
            orchestrator,
            httpClient,
            codexGate);
    }

    private static async Task<int> ListRunsAsync(
        IRunStore runStore,
        CancellationToken cancellationToken)
    {
        var runs =
            await runStore.ListAsync(
                20,
                cancellationToken);

        if (runs.Count == 0)
        {
            Console.WriteLine(
                "No TaskForge runs found.");

            return 0;
        }

        Console.WriteLine(
            $"{"ID",-30} {"STATE",-16} {"CODEX",-7} {"IN/OUT",-20} STARTED");

        foreach (var run in runs)
        {
            var tokens =
                run.CodexInputTokens is null
                && run.CodexOutputTokens is null
                    ? "-"
                    : $"{run.CodexInputTokens ?? 0}/{run.CodexOutputTokens ?? 0}";

            Console.WriteLine(
                $"{run.Id.Value,-30} {run.State,-16} "
                + $"{run.CodexRuns + "/2",-7} {tokens,-20} {run.StartedAt:O}");
        }

        return 0;
    }

    private static async Task<int> ShowRunAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1
            || string.IsNullOrWhiteSpace(
                args[0]))
        {
            throw new ArgumentException(
                "Usage: taskforge show <run-id>");
        }

        var metadata =
            await runStore.GetAsync(
                new TaskId(args[0]),
                cancellationToken);

        if (metadata is null)
        {
            Console.Error.WriteLine(
                $"Run not found: {args[0]}");

            return 1;
        }

        Console.WriteLine(
            $"ID:         {metadata.Id}");
        Console.WriteLine(
            $"State:      {metadata.State}");
        Console.WriteLine(
            $"Repository: {metadata.RepositoryPath}");
        Console.WriteLine(
            $"Started:    {metadata.StartedAt:O}");
        Console.WriteLine(
            $"Finished:   {metadata.FinishedAt:O}");
        Console.WriteLine(
            $"Codex runs: {metadata.CodexRuns}");

        if (metadata.CodexInputTokens is not null
            || metadata.CodexOutputTokens is not null)
        {
            Console.WriteLine(
                $"Input:      {metadata.CodexInputTokens ?? 0}");
            Console.WriteLine(
                $"  cached:   {metadata.CodexCachedInputTokens ?? 0}");
            Console.WriteLine(
                $"  cache wr: {metadata.CodexCacheWriteInputTokens ?? 0}");
            Console.WriteLine(
                $"Output:     {metadata.CodexOutputTokens ?? 0}");
            Console.WriteLine(
                $"  reasoning:{metadata.CodexReasoningOutputTokens ?? 0}");
        }

        if (!string.IsNullOrWhiteSpace(
                metadata.Message))
        {
            Console.WriteLine(
                $"Message:    {metadata.Message}");
        }

        return 0;
    }

    private static async Task<int> InspectRunAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1
            || string.IsNullOrWhiteSpace(
                args[0]))
        {
            throw new ArgumentException(
                "Usage: taskforge inspect <run-id>");
        }

        var inspector =
            new RunInspector(
                runStore);

        var inspection =
            await inspector.InspectAsync(
                new TaskId(
                    args[0]),
                cancellationToken);

        PrintInspection(
            inspection);

        return 0;
    }

    private static void PrintInspection(
        RunInspection inspection)
    {
        var metadata =
            inspection.Metadata;

        Console.WriteLine(
            $"ID:             {metadata.Id}");
        Console.WriteLine(
            $"State:          {metadata.State}");
        Console.WriteLine(
            $"Repository:     {metadata.RepositoryPath}");
        Console.WriteLine(
            $"Base commit:    {inspection.TaskPacket?.BaseCommit ?? "-"}");
        Console.WriteLine(
            $"Codex runs:     {metadata.CodexRuns}/2");
        Console.WriteLine(
            $"Ready to apply: {(inspection.CanApply ? "yes" : "no")}");
        Console.WriteLine();

        if (inspection.Request is not null)
        {
            Console.WriteLine("Goal:");
            Console.WriteLine(
                $"  {inspection.Request.Goal}");
            Console.WriteLine();
        }

        if (inspection.Plan is not null)
        {
            Console.WriteLine("Plan:");
            Console.WriteLine(
                $"  {inspection.Plan.Summary}");

            PrintItems(
                "Search terms",
                inspection.Plan.SearchTerms);

            PrintItems(
                "Likely areas",
                inspection.Plan.LikelyAreas);
        }

        var acceptanceCriteria =
            inspection.TaskPacket?.AcceptanceCriteria
            ?? inspection.Plan?.AcceptanceCriteria
            ?? [];

        PrintItems(
            "Acceptance criteria",
            acceptanceCriteria);

        PrintItems(
            "Relevant files",
            inspection.TaskPacket?.RelevantFiles
            ?? inspection.Exploration?.RelevantFiles
            ?? []);

        PrintItems(
            "Observations",
            inspection.TaskPacket?.Observations
            ?? inspection.Exploration?.Observations
            ?? []);

        PrintItems(
            "Write scope",
            inspection.TaskPacket?.WriteScope
            ?? []);

        var testTargets =
            inspection.TaskPacket?.TestTargets
            ?? inspection.Exploration?.TestTargets
            ?? [];

        if (testTargets.Count > 0)
        {
            Console.WriteLine("Test targets:");

            foreach (var target in testTargets)
            {
                var filter =
                    string.IsNullOrWhiteSpace(
                        target.Filter)
                        ? string.Empty
                        : $" [filter: {target.Filter}]";

                Console.WriteLine(
                    $"  - {target.ProjectPath}{filter}");
            }

            Console.WriteLine();
        }

        if (inspection.Exploration is not null)
        {
            Console.WriteLine(
                $"Explorer confidence: {inspection.Exploration.Confidence:P0}");
            Console.WriteLine();
        }

        PrintItems(
            "Warnings",
            inspection.Warnings);

        if (inspection.CanApply)
        {
            Console.WriteLine(
                $"Next: taskforge apply {metadata.Id}");
        }
    }

    private static void PrintItems(
        string title,
        IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        Console.WriteLine(
            $"{title}:");

        foreach (var item in items)
        {
            Console.WriteLine(
                $"  - {item}");
        }

        Console.WriteLine();
    }

    private static TaskRequest ParseTaskRequest(
        string[] args,
        string command)
    {
        var repositoryPath =
            Directory.GetCurrentDirectory();

        var goalParts =
            new List<string>();

        for (var i = 0;
             i < args.Length;
             i++)
        {
            if (string.Equals(
                    args[i],
                    "--repo",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException(
                        "--repo requires a path.");
                }

                repositoryPath =
                    args[++i];

                continue;
            }

            goalParts.Add(
                args[i]);
        }

        var goal =
            string.Join(
                    ' ',
                    goalParts)
                .Trim();

        if (string.IsNullOrWhiteSpace(
                goal))
        {
            throw new ArgumentException(
                $"Usage: taskforge {command} [--repo <path>] <task>");
        }

        if (!Directory.Exists(
                repositoryPath))
        {
            throw new ArgumentException(
                $"Repository path does not exist: {repositoryPath}");
        }

        return new TaskRequest(
            goal,
            Path.GetFullPath(
                repositoryPath));
    }

    private static void PrintResult(
        TaskRunResult result)
    {
        Console.WriteLine(
            $"Run:   {result.Id}");
        Console.WriteLine(
            $"State: {result.State}");
        Console.WriteLine(
            result.Message);
    }

    private static Uri NormalizeBaseAddress(
        string value)
    {
        if (!value.EndsWith(
                "/",
                StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(
            value,
            UriKind.Absolute);
    }

    private static int GetPositiveIntEnvironmentVariable(
        string name,
        int defaultValue)
    {
        var value =
            Environment.GetEnvironmentVariable(
                name);

        return int.TryParse(
                   value,
                   out var parsed)
               && parsed > 0
            ? parsed
            : defaultValue;
    }

    private static int MapExitCode(
        WorkflowState state) =>
        state switch
        {
            WorkflowState.Completed => 0,
            WorkflowState.ReadyToApply => 0,
            WorkflowState.NeedsUser => 2,
            WorkflowState.BudgetExceeded => 3,
            WorkflowState.Cancelled => 4,
            _ => 1
        };

    private static int UnknownCommand(
        string command)
    {
        Console.Error.WriteLine(
            $"Unknown command: {command}");

        PrintUsage();
        return 5;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "TaskForge");
        Console.WriteLine();
        Console.WriteLine(
            "Usage:");
        Console.WriteLine(
            "  taskforge run [--repo <path>] <task>");
        Console.WriteLine(
            "  taskforge plan [--repo <path>] <task>");
        Console.WriteLine(
            "  taskforge apply <run-id>");
        Console.WriteLine(
            "  taskforge runs");
        Console.WriteLine(
            "  taskforge show <run-id>");
        Console.WriteLine(
            "  taskforge inspect <run-id>");
    }

    private sealed class OrchestratorRuntime(
        TaskOrchestrator orchestrator,
        HttpClient httpClient,
        CodexRunGate codexGate) : IDisposable
    {
        public TaskOrchestrator Orchestrator { get; } =
            orchestrator;

        public void Dispose()
        {
            codexGate.Dispose();
            httpClient.Dispose();
        }
    }
}
