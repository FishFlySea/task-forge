namespace TaskForge.Core;

public static class WorkflowStateMachine
{
    public static bool CanTransition(
        WorkflowState from,
        WorkflowState to) =>
        from switch
        {
            WorkflowState.Created =>
                to is WorkflowState.Planning
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Planning =>
                to is WorkflowState.Exploring
                    or WorkflowState.NeedsUser
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Exploring =>
                to is WorkflowState.PacketReady
                    or WorkflowState.ReadyToApply
                    or WorkflowState.NeedsUser
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.PacketReady =>
                to is WorkflowState.Implementing
                    or WorkflowState.NeedsUser
                    or WorkflowState.BudgetExceeded
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.ReadyToApply =>
                to is WorkflowState.Implementing
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Implementing =>
                to is WorkflowState.Building
                    or WorkflowState.BudgetExceeded
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Building =>
                to is WorkflowState.Testing
                    or WorkflowState.Diagnosing
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Testing =>
                to is WorkflowState.Reviewing
                    or WorkflowState.Diagnosing
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Diagnosing =>
                to is WorkflowState.Correcting
                    or WorkflowState.NeedsUser
                    or WorkflowState.BudgetExceeded
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Correcting =>
                to is WorkflowState.Building
                    or WorkflowState.BudgetExceeded
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            WorkflowState.Reviewing =>
                to is WorkflowState.Completed
                    or WorkflowState.NeedsUser
                    or WorkflowState.Cancelled
                    or WorkflowState.Failed,

            _ => false
        };

    public static void EnsureCanTransition(
        WorkflowState from,
        WorkflowState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Workflow transition '{from}' -> '{to}' is not allowed.");
        }
    }
}
