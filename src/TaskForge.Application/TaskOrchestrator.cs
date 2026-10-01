using TaskForge.Core;

namespace TaskForge.Application;

public sealed class TaskOrchestrator(
    IRunStore runStore,
    IPlannerAgent plannerAgent,
    IExplorerAgent explorerAgent,
    IImplementerAgent implementerAgent,
    ICodexRunGate codexRunGate,
    IDotnetRunner dotnetRunner,
    IGitClient gitClient,
    IDiagnosticAgent diagnosticAgent,
    IReviewAgent reviewAgent,
    TimeProvider timeProvider)
{
    private readonly IRunStore _runStore = runStore;
    private readonly IPlannerAgent _plannerAgent = plannerAgent;
    private readonly IExplorerAgent _explorerAgent = explorerAgent;
    private readonly IImplementerAgent _implementerAgent = implementerAgent;
    private readonly ICodexRunGate _codexRunGate = codexRunGate;
    private readonly IDotnetRunner _dotnetRunner = dotnetRunner;
    private readonly IGitClient _gitClient = gitClient;
    private readonly IDiagnosticAgent _diagnosticAgent = diagnosticAgent;
    private readonly IReviewAgent _reviewAgent = reviewAgent;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<TaskRunResult> RunAsync(
        TaskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryPath);

        var startedAt = _timeProvider.GetUtcNow();
        var metadata = new RunMetadata
        {
            Id = TaskId.New(startedAt),
            RepositoryPath = request.RepositoryPath,
            StartedAt = startedAt,
            State = WorkflowState.Created
        };

        await _runStore.CreateAsync(
            request,
            metadata,
            cancellationToken);

        try
        {
            metadata = await TransitionAsync(
                metadata,
                WorkflowState.Planning,
                cancellationToken);

            var plan = await _plannerAgent.PlanAsync(
                request,
                cancellationToken);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "plan.json",
                plan,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata,
                WorkflowState.Exploring,
                cancellationToken);

            var exploration =
                await _explorerAgent.ExploreAsync(
                    request,
                    plan,
                    cancellationToken);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "exploration.json",
                exploration,
                cancellationToken);

            var taskPacket = TaskPacketFactory.Create(
                request,
                plan,
                exploration);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "task-packet.json",
                taskPacket,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata,
                WorkflowState.PacketReady,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata,
                WorkflowState.Implementing,
                cancellationToken);

            var implementation =
                await ExecuteCodexAsync(
                    metadata,
                    taskPacket,
                    CodexRunKind.Implementation,
                    cancellationToken);

            metadata = implementation.Metadata;

            if (!implementation.Result.Success)
            {
                return await FinishResultAsync(
                    metadata,
                    WorkflowState.Failed,
                    "Codex implementation process failed.",
                    cancellationToken);
            }

            while (true)
            {
                metadata = await TransitionAsync(
                    metadata,
                    WorkflowState.Building,
                    cancellationToken);

                var buildResult =
                    await _dotnetRunner.BuildAsync(
                        request.RepositoryPath,
                        cancellationToken);

                await SaveProcessResultAsync(
                    metadata.Id,
                    $"build-{metadata.CodexRuns:00}",
                    buildResult,
                    cancellationToken);

                ProcessResult? failure = null;
                ProcessResult? testResult = null;

                if (buildResult.Success)
                {
                    metadata = await TransitionAsync(
                        metadata,
                        WorkflowState.Testing,
                        cancellationToken);

                    testResult =
                        await _dotnetRunner.TestAsync(
                            request.RepositoryPath,
                            taskPacket.TestTargets,
                            cancellationToken);

                    await SaveProcessResultAsync(
                        metadata.Id,
                        $"test-{metadata.CodexRuns:00}",
                        testResult,
                        cancellationToken);

                    if (!testResult.Success)
                    {
                        failure = testResult;
                    }
                }
                else
                {
                    failure = buildResult;
                }

                if (failure is null)
                {
                    metadata = await TransitionAsync(
                        metadata,
                        WorkflowState.Reviewing,
                        cancellationToken);

                    var snapshot =
                        await _gitClient.GetWorkingTreeSnapshotAsync(
                            request.RepositoryPath,
                            cancellationToken);

                    await _runStore.SaveTextArtifactAsync(
                        metadata.Id,
                        "git-working-tree.txt",
                        snapshot,
                        cancellationToken);

                    var review =
                        await _reviewAgent.ReviewAsync(
                            taskPacket,
                            snapshot,
                            buildResult,
                            testResult!,
                            cancellationToken);

                    await _runStore.SaveArtifactAsync(
                        metadata.Id,
                        "review.json",
                        review,
                        cancellationToken);

                    var message = review.Acceptable
                        ? "Implementation completed and verified."
                        : "Build/tests passed, but local review requires user attention.";

                    var state = review.Acceptable
                        ? WorkflowState.Completed
                        : WorkflowState.NeedsUser;

                    return await FinishResultAsync(
                        metadata,
                        state,
                        message,
                        cancellationToken);
                }

                metadata = await TransitionAsync(
                    metadata,
                    WorkflowState.Diagnosing,
                    cancellationToken);

                var failedSnapshot =
                    await _gitClient.GetWorkingTreeSnapshotAsync(
                        request.RepositoryPath,
                        cancellationToken);

                var diagnosis =
                    await _diagnosticAgent.DiagnoseAsync(
                        taskPacket,
                        failure,
                        failedSnapshot,
                        cancellationToken);

                await _runStore.SaveArtifactAsync(
                    metadata.Id,
                    $"diagnostic-{metadata.CodexRuns:00}.json",
                    diagnosis,
                    cancellationToken);

                if (diagnosis.NeedsUser
                    || !diagnosis.RequiresCodexCorrection)
                {
                    return await FinishResultAsync(
                        metadata,
                        diagnosis.NeedsUser
                            ? WorkflowState.NeedsUser
                            : WorkflowState.Failed,
                        diagnosis.Summary,
                        cancellationToken);
                }

                metadata = await TransitionAsync(
                    metadata,
                    WorkflowState.Correcting,
                    cancellationToken);

                var correctivePacket = taskPacket with
                {
                    Diagnostics =
                        diagnosis.Summary
                        + Environment.NewLine
                        + failure.CombinedOutput
                };

                CodexExecution correction;

                try
                {
                    correction = await ExecuteCodexAsync(
                        metadata,
                        correctivePacket,
                        CodexRunKind.Correction,
                        cancellationToken);
                }
                catch (CodexBudgetExceededException exception)
                {
                    return await FinishResultAsync(
                        metadata,
                        WorkflowState.BudgetExceeded,
                        exception.Message,
                        cancellationToken);
                }

                metadata = correction.Metadata;

                if (!correction.Result.Success)
                {
                    return await FinishResultAsync(
                        metadata,
                        WorkflowState.Failed,
                        "Corrective Codex process failed.",
                        cancellationToken);
                }
            }
        }
        catch (CodexBudgetExceededException exception)
        {
            if (WorkflowStateMachine.CanTransition(
                    metadata.State,
                    WorkflowState.BudgetExceeded))
            {
                return await FinishResultAsync(
                    metadata,
                    WorkflowState.BudgetExceeded,
                    exception.Message,
                    CancellationToken.None);
            }

            throw;
        }
        catch (OperationCanceledException)
        {
            await TryFinishAsync(
                metadata,
                WorkflowState.Cancelled,
                "Run cancelled.",
                CancellationToken.None);

            throw;
        }
        catch (Exception exception)
        {
            await TryFinishAsync(
                metadata,
                WorkflowState.Failed,
                exception.Message,
                CancellationToken.None);

            throw;
        }
    }

    private async Task<CodexExecution> ExecuteCodexAsync(
        RunMetadata metadata,
        TaskPacket taskPacket,
        CodexRunKind kind,
        CancellationToken cancellationToken)
    {
        await using var lease =
            await _codexRunGate.AcquireAsync(
                metadata.Id,
                metadata.CodexRuns,
                kind,
                cancellationToken);

        var runNumber = metadata.CodexRuns + 1;

        metadata = metadata with
        {
            CodexRuns = runNumber
        };

        await _runStore.SaveMetadataAsync(
            metadata,
            cancellationToken);

        var result =
            await _implementerAgent.ExecuteAsync(
                taskPacket,
                metadata.RepositoryPath,
                kind,
                cancellationToken);

        await _runStore.SaveTextArtifactAsync(
            metadata.Id,
            $"codex-{runNumber:00}.jsonl",
            result.Jsonl,
            cancellationToken);

        await _runStore.SaveTextArtifactAsync(
            metadata.Id,
            $"codex-{runNumber:00}.stderr.log",
            result.StandardError,
            cancellationToken);

        await _runStore.SaveArtifactAsync(
            metadata.Id,
            $"codex-{runNumber:00}.result.json",
            new
            {
                result.ExitCode,
                result.Success,
                Kind = kind.ToString()
            },
            cancellationToken);

        return new CodexExecution(
            metadata,
            result);
    }

    private async Task SaveProcessResultAsync(
        TaskId id,
        string prefix,
        ProcessResult result,
        CancellationToken cancellationToken)
    {
        await _runStore.SaveTextArtifactAsync(
            id,
            $"{prefix}.stdout.log",
            result.StandardOutput,
            cancellationToken);

        await _runStore.SaveTextArtifactAsync(
            id,
            $"{prefix}.stderr.log",
            result.StandardError,
            cancellationToken);

        await _runStore.SaveArtifactAsync(
            id,
            $"{prefix}.result.json",
            new
            {
                result.ExitCode,
                result.Success
            },
            cancellationToken);
    }

    private async Task<RunMetadata> TransitionAsync(
        RunMetadata metadata,
        WorkflowState nextState,
        CancellationToken cancellationToken)
    {
        WorkflowStateMachine.EnsureCanTransition(
            metadata.State,
            nextState);

        metadata = metadata with
        {
            State = nextState
        };

        await _runStore.SaveMetadataAsync(
            metadata,
            cancellationToken);

        return metadata;
    }

    private async Task<TaskRunResult> FinishResultAsync(
        RunMetadata metadata,
        WorkflowState finalState,
        string message,
        CancellationToken cancellationToken)
    {
        metadata = await FinishAsync(
            metadata,
            finalState,
            message,
            cancellationToken);

        return new TaskRunResult(
            metadata.Id,
            metadata.State,
            message);
    }

    private async Task<RunMetadata> FinishAsync(
        RunMetadata metadata,
        WorkflowState finalState,
        string message,
        CancellationToken cancellationToken)
    {
        WorkflowStateMachine.EnsureCanTransition(
            metadata.State,
            finalState);

        metadata = metadata with
        {
            State = finalState,
            FinishedAt = _timeProvider.GetUtcNow(),
            Message = message
        };

        await _runStore.SaveMetadataAsync(
            metadata,
            cancellationToken);

        return metadata;
    }

    private async Task TryFinishAsync(
        RunMetadata metadata,
        WorkflowState finalState,
        string message,
        CancellationToken cancellationToken)
    {
        if (!WorkflowStateMachine.CanTransition(
                metadata.State,
                finalState))
        {
            return;
        }

        try
        {
            await FinishAsync(
                metadata,
                finalState,
                message,
                cancellationToken);
        }
        catch
        {
            // Preserve the original workflow exception.
        }
    }

    private sealed record CodexExecution(
        RunMetadata Metadata,
        CodexRunResult Result);
}
