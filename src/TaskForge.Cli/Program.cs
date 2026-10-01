using TaskForge.Application;
using TaskForge.Core;
using TaskForge.Infrastructure;

namespace TaskForge.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
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

            var store = CreateRunStore();

            return args[0].ToLowerInvariant() switch
            {
                "run" => await RunAsync(
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
                _ => UnknownCommand(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 4;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 5;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static JsonRunStore CreateRunStore()
    {
        var runsDirectory =
            Environment.GetEnvironmentVariable(
                "TASKFORGE_RUNS_DIRECTORY")
            ?? JsonRunStore.GetDefaultRunsDirectory();

        return new JsonRunStore(runsDirectory);
    }

    private static async Task<int> RunAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        var (goal, repositoryPath) =
            ParseRunArguments(args);

        if (!Directory.Exists(repositoryPath))
        {
            throw new ArgumentException(
                $"Repository path does not exist: {repositoryPath}");
        }

        repositoryPath = Path.GetFullPath(
            repositoryPath);

        var ollamaUrl =
            Environment.GetEnvironmentVariable(
                "TASKFORGE_OLLAMA_URL")
            ?? "http://localhost:11434/";

        var ollamaModel =
            Environment.GetEnvironmentVariable(
                "TASKFORGE_OLLAMA_MODEL")
            ?? "qwen3-coder";

        var timeoutSeconds = GetPositiveIntEnvironmentVariable(
            "TASKFORGE_OLLAMA_TIMEOUT_SECONDS",
            120);

        using var httpClient = new HttpClient
        {
            BaseAddress = NormalizeBaseAddress(ollamaUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        var localLlm = new OllamaClient(
            httpClient,
            ollamaModel);

        var planner = new PlannerAgent(
            localLlm);

        var explorer = new ExplorerAgent(
            localLlm,
            new FileSystemRepositorySearch());

        var orchestrator = new TaskOrchestrator(
            runStore,
            planner,
            explorer,
            TimeProvider.System);

        var result = await orchestrator.RunAsync(
            new TaskRequest(
                goal,
                repositoryPath),
            cancellationToken);

        Console.WriteLine($"Run:   {result.Id}");
        Console.WriteLine($"State: {result.State}");
        Console.WriteLine(result.Message);

        return MapExitCode(result.State);
    }

    private static async Task<int> ListRunsAsync(
        IRunStore runStore,
        CancellationToken cancellationToken)
    {
        var runs = await runStore.ListAsync(
            20,
            cancellationToken);

        if (runs.Count == 0)
        {
            Console.WriteLine(
                "No TaskForge runs found.");

            return 0;
        }

        Console.WriteLine(
            $"{"ID",-30} {"STATE",-16} {"CODEX",-7} STARTED");

        foreach (var run in runs)
        {
            Console.WriteLine(
                $"{run.Id.Value,-30} {run.State,-16} "
                + $"{run.CodexRuns + "/2",-7} {run.StartedAt:O}");
        }

        return 0;
    }

    private static async Task<int> ShowRunAsync(
        IRunStore runStore,
        string[] args,
        CancellationToken cancellationToken)
    {
        if (args.Length != 1
            || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new ArgumentException(
                "Usage: taskforge show <run-id>");
        }

        var metadata = await runStore.GetAsync(
            new TaskId(args[0]),
            cancellationToken);

        if (metadata is null)
        {
            Console.Error.WriteLine(
                $"Run not found: {args[0]}");

            return 1;
        }

        Console.WriteLine($"ID:         {metadata.Id}");
        Console.WriteLine($"State:      {metadata.State}");
        Console.WriteLine($"Repository: {metadata.RepositoryPath}");
        Console.WriteLine($"Started:    {metadata.StartedAt:O}");
        Console.WriteLine($"Finished:   {metadata.FinishedAt:O}");
        Console.WriteLine($"Codex runs: {metadata.CodexRuns}");

        if (!string.IsNullOrWhiteSpace(
                metadata.Message))
        {
            Console.WriteLine(
                $"Message:    {metadata.Message}");
        }

        return 0;
    }

    private static (
        string Goal,
        string RepositoryPath) ParseRunArguments(
        string[] args)
    {
        var repositoryPath =
            Directory.GetCurrentDirectory();

        var goalParts = new List<string>();

        for (var i = 0; i < args.Length; i++)
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

                repositoryPath = args[++i];
                continue;
            }

            goalParts.Add(args[i]);
        }

        var goal = string
            .Join(' ', goalParts)
            .Trim();

        if (string.IsNullOrWhiteSpace(goal))
        {
            throw new ArgumentException(
                "Usage: taskforge run [--repo <path>] <task>");
        }

        return (goal, repositoryPath);
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
            Environment.GetEnvironmentVariable(name);

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
        Console.WriteLine("TaskForge");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  taskforge run [--repo <path>] <task>");
        Console.WriteLine(
            "  taskforge runs");
        Console.WriteLine(
            "  taskforge show <run-id>");
    }
}
