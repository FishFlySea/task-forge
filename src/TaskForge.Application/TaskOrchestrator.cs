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
    IWorkspaceManager workspaceManager,
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
    private readonly IWorkspaceManager _workspaceManager = workspaceManager;
    private readonly IDiagnosticAgent _diagnosticAgent = diagnosticAgent;
    private readonly IReviewAgent _reviewAgent = reviewAgent;
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<TaskRunResult> RunAsync(
        TaskRequest request,
        CancellationToken cancellationToken)
    {
        var planned = await PlanAsync(
            request,
            cancellationToken);

        if (planned.State != WorkflowState.ReadyToApply)
        {
            return planned;
        }

        return await ApplyAsync(
            planned.Id,
            cancellationToken);
    }

    public async Task<TaskRunResult> PlanAsync(
        TaskRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

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
            if (!await _gitClient.IsWorkingTreeCleanAsync(
                    request.RepositoryPath,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "Planning requires a clean Git working tree so TaskPacket context matches its base commit.");
            }

            var baseCommit =
                await _gitClient.GetHeadCommitAsync(
                    request.RepositoryPath,
                    cancellationToken);

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
                exploration,
                baseCommit);

            await _runStore.SaveArtifactAsync(
                metadata.Id,
                "task-packet.json",
                taskPacket,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata,
                WorkflowState.ReadyToApply,
                cancellationToken);

            const string message =
                "Task packet prepared. No Codex run has been used. "
                + "Run 'taskforge apply <run-id>' to execute it.";

            metadata = metadata with
            {
                Message = message
            };

            await _runStore.SaveMetadataAsync(
                metadata,
                cancellationToken);

            return new TaskRunResult(
                metadata.Id,
                metadata.State,
                message);
        }
        catch (OperationCanceledException)
        {
            await TryFinishAsync(
                metadata,
                WorkflowState.Cancelled,
                "Planning cancelled.",
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

    public async Task<TaskRunResult> ApplyAsync(
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        var metadata =
            await _runStore.GetAsync(
                taskId,
                cancellationToken)
            ?? throw new ArgumentException(
                $"Run not found: {taskId}");

        if (metadata.State
            is not WorkflowState.ReadyToApply
            and not WorkflowState.PacketReady)
        {
            throw new InvalidOperationException(
                $"Run {taskId} cannot be applied from state '{metadata.State}'.");
        }

        var request =
            await _runStore.LoadArtifactAsync<TaskRequest>(
                taskId,
                "request.json",
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Run {taskId} does not contain request.json.");

        var taskPacket =
            await _runStore.LoadArtifactAsync<TaskPacket>(
                taskId,
                "task-packet.json",
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"Run {taskId} does not contain task-packet.json.");

        ValidateRequest(request);

        if (!string.Equals(
                Path.GetFullPath(request.RepositoryPath),
                Path.GetFullPath(metadata.RepositoryPath),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Stored request repository does not match run metadata.");
        }

        if (string.IsNullOrWhiteSpace(
                taskPacket.BaseCommit))
        {
            throw new InvalidOperationException(
                $"Run {taskId} does not contain a base Git commit and cannot be safely applied.");
        }

        if (!await _gitClient.CommitExistsAsync(
                request.RepositoryPath,
                taskPacket.BaseCommit,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Base commit '{taskPacket.BaseCommit}' is no longer available in the repository.");
        }

        if (taskPacket.WriteScope.Count == 0)
        {
            throw new InvalidOperationException(
                $"Run {taskId} has an empty write scope and cannot be safely applied.");
        }

        WorkspaceHandle? workspace = null;

        try
        {
            workspace =
                await _workspaceManager.CreateAsync(
                    request.RepositoryPath,
                    taskPacket.BaseCommit,
                    taskId,
                    cancellationToken);

            await _runStore.SaveArtifactAsync(
                taskId,
                "workspace.json",
                workspace,
                cancellationToken);

            metadata = await TransitionAsync(
                metadata with
                {
                    FinishedAt = null,
                    Message = null
                },
                WorkflowState.Implementing,
                cancellationToken);

            var implementation =
                await ExecuteCodexAsync(
                    metadata,
                    taskPacket,
                    workspace.Path,
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

            var implementationPolicyResult =
                await ValidateWriteScopeAsync(
                    metadata,
                    taskPacket,
                    workspace,
                    cancellationToken);

            if (implementationPolicyResult is not null)
            {
                return implementationPolicyResult;
            }

            while (true)
            {
                metadata = await TransitionAsync(
                    metadata,
                    WorkflowState.Building,
                    cancellationToken);

                var buildResult =
                    await _dotnetRunner.BuildAsync(
                        workspace.Path,
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
                            workspace.Path,
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
                        await _workspaceManager.SnapshotAsync(
                            workspace,
                            cancellationToken);

                    await SaveWorkspaceSnapshotAsync(
                        metadata.Id,
                        "workspace-review",
                        snapshot,
                        cancellationToken);

                    var review =
                        await _reviewAgent.ReviewAsync(
                            taskPacket,
                            FormatWorkspaceSnapshot(
                                snapshot),
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
                    await _workspaceManager.SnapshotAsync(
                        workspace,
                        cancellationToken);

                await SaveWorkspaceSnapshotAsync(
                    metadata.Id,
                    $"workspace-diagnostic-{metadata.CodexRuns:00}",
                    failedSnapshot,
                    cancellationToken);

                var diagnosis =
                    await _diagnosticAgent.DiagnoseAsync(
                        taskPacket,
                        failure,
                        FormatWorkspaceSnapshot(
                            failedSnapshot),
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
                        + Truncate(
                            failure.CombinedOutput,
                            24_000)
                };

                CodexExecution correction;

                try
                {
                    correction = await ExecuteCodexAsync(
                        metadata,
                        correctivePacket,
                        workspace.Path,
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

                var correctionPolicyResult =
                    await ValidateWriteScopeAsync(
                        metadata,
                        taskPacket,
                        workspace,
                        cancellationToken);

                if (correctionPolicyResult is not null)
                {
                    return correctionPolicyResult;
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
        finally
        {
            if (workspace is not null)
            {
                await FinalizeWorkspaceAsync(
                    metadata.Id,
                    workspace);
            }
        }
    }

    private async Task<CodexExecution> ExecuteCodexAsync(
        RunMetadata metadata,
        TaskPacket taskPacket,
        string workspacePath,
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
                workspacePath,
                kind,
                cancellationToken);

        if (result.Usage is not null)
        {
            metadata = AccumulateUsage(
                metadata,
                result.Usage);

            await _runStore.SaveMetadataAsync(
                metadata,
                cancellationToken);
        }

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
                result.Usage,
                Kind = kind.ToString()
            },
            cancellationToken);

        return new CodexExecution(
            metadata,
            result);
    }

    private async Task<TaskRunResult?> ValidateWriteScopeAsync(
        RunMetadata metadata,
        TaskPacket taskPacket,
        WorkspaceHandle workspace,
        CancellationToken cancellationToken)
    {
        var snapshot =
            await _workspaceManager.SnapshotAsync(
                workspace,
                cancellationToken);

        await SaveWorkspaceSnapshotAsync(
            metadata.Id,
            $"workspace-worker-{metadata.CodexRuns:00}",
            snapshot,
            cancellationToken);

        var violations =
            WriteScopePolicy.FindViolations(
                snapshot.ChangedFiles,
                taskPacket.WriteScope);

        if (violations.Count == 0)
        {
            return null;
        }

        await _runStore.SaveArtifactAsync(
            metadata.Id,
            $"write-scope-violation-{metadata.CodexRuns:00}.json",
            new
            {
                Allowed = taskPacket.WriteScope,
                Violations = violations
            },
            cancellationToken);

        return await FinishResultAsync(
            metadata,
            WorkflowState.NeedsUser,
            "Coding worker changed files outside the allowed write scope: "
            + string.Join(", ", violations),
            cancellationToken);
    }

    private async Task FinalizeWorkspaceAsync(
        TaskId taskId,
        WorkspaceHandle workspace)
    {
        try
        {
            var snapshot =
                await _workspaceManager.SnapshotAsync(
                    workspace,
                    CancellationToken.None);

            await SaveWorkspaceSnapshotAsync(
                taskId,
                "workspace-final",
                snapshot,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            await _runStore.SaveTextArtifactAsync(
                taskId,
                "workspace-snapshot-error.log",
                exception.ToString(),
                CancellationToken.None);
        }

        try
        {
            await _workspaceManager.CleanupAsync(
                workspace,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            await _runStore.SaveTextArtifactAsync(
                taskId,
                "workspace-cleanup-error.log",
                exception.ToString(),
                CancellationToken.None);
        }
    }

    private async Task SaveWorkspaceSnapshotAsync(
        TaskId taskId,
        string prefix,
        WorkspaceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await _runStore.SaveArtifactAsync(
            taskId,
            $"{prefix}.json",
            snapshot,
            cancellationToken);

        await _runStore.SaveTextArtifactAsync(
            taskId,
            $"{prefix}.diff",
            snapshot.Diff,
            cancellationToken);
    }

    private static string FormatWorkspaceSnapshot(
        WorkspaceSnapshot snapshot) =>
        "## git status"
        + Environment.NewLine
        + snapshot.Status
        + Environment.NewLine
        + "## git diff"
        + Environment.NewLine
        + snapshot.Diff;

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
        try
        {
            var persisted =
                await _runStore.GetAsync(
                    metadata.Id,
                    cancellationToken);

            metadata = persisted ?? metadata;

            if (!WorkflowStateMachine.CanTransition(
                    metadata.State,
                    finalState))
            {
                return;
            }

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

    private static void ValidateRequest(
        TaskRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Goal);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.RepositoryPath);

        if (!Directory.Exists(request.RepositoryPath))
        {
            throw new ArgumentException(
                $"Repository path does not exist: {request.RepositoryPath}");
        }
    }

    private static RunMetadata AccumulateUsage(
        RunMetadata metadata,
        CodexUsage usage) =>
        metadata with
        {
            CodexInputTokens =
                (metadata.CodexInputTokens ?? 0)
                + usage.InputTokens,
            CodexCachedInputTokens =
                (metadata.CodexCachedInputTokens ?? 0)
                + usage.CachedInputTokens,
            CodexCacheWriteInputTokens =
                (metadata.CodexCacheWriteInputTokens ?? 0)
                + usage.CacheWriteInputTokens,
            CodexOutputTokens =
                (metadata.CodexOutputTokens ?? 0)
                + usage.OutputTokens,
            CodexReasoningOutputTokens =
                (metadata.CodexReasoningOutputTokens ?? 0)
                + usage.ReasoningOutputTokens
        };

    private static string Truncate(
        string value,
        int maxChars) =>
        value.Length <= maxChars
            ? value
            : value[..maxChars]
              + Environment.NewLine
              + "[truncated]";

    private sealed record CodexExecution(
        RunMetadata Metadata,
        CodexRunResult Result);
}
