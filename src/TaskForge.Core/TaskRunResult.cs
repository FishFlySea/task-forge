namespace TaskForge.Core;

public sealed record TaskRunResult(TaskId Id, WorkflowState State, string Message);
