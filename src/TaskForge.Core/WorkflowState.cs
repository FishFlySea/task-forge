namespace TaskForge.Core;

public enum WorkflowState
{
    Created,
    Planning,
    Exploring,
    PacketReady,
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
