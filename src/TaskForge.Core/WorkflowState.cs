namespace TaskForge.Core;

public enum WorkflowState
{
    Created,
    Planning,
    Exploring,
    PacketReady,
    ReadyToApply,
    Implementing,
    Building,
    Testing,
    Diagnosing,
    Correcting,
    Reviewing,
    Completed,
    Failed,
    BudgetExceeded,
    Cancelled,
    NeedsUser
}
