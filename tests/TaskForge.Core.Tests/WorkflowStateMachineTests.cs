using TaskForge.Core;

namespace TaskForge.Core.Tests;

public sealed class WorkflowStateMachineTests
{
    [Theory]
    [InlineData(WorkflowState.Created, WorkflowState.Planning)]
    [InlineData(WorkflowState.Planning, WorkflowState.Exploring)]
    [InlineData(WorkflowState.Exploring, WorkflowState.PacketReady)]
    [InlineData(WorkflowState.PacketReady, WorkflowState.Implementing)]
    [InlineData(WorkflowState.Implementing, WorkflowState.Building)]
    [InlineData(WorkflowState.Building, WorkflowState.Testing)]
    [InlineData(WorkflowState.Testing, WorkflowState.Reviewing)]
    [InlineData(WorkflowState.Reviewing, WorkflowState.Completed)]
    [InlineData(WorkflowState.Testing, WorkflowState.Diagnosing)]
    [InlineData(WorkflowState.Diagnosing, WorkflowState.Correcting)]
    [InlineData(WorkflowState.Correcting, WorkflowState.Building)]
    public void Expected_transitions_are_allowed(WorkflowState from, WorkflowState to)
    {
        Assert.True(WorkflowStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(WorkflowState.Created, WorkflowState.Completed)]
    [InlineData(WorkflowState.Planning, WorkflowState.Implementing)]
    [InlineData(WorkflowState.Completed, WorkflowState.Planning)]
    [InlineData(WorkflowState.Failed, WorkflowState.Planning)]
    [InlineData(WorkflowState.NeedsUser, WorkflowState.Planning)]
    public void Invalid_transitions_are_rejected(WorkflowState from, WorkflowState to)
    {
        Assert.False(WorkflowStateMachine.CanTransition(from, to));
        Assert.Throws<InvalidOperationException>(
            () => WorkflowStateMachine.EnsureCanTransition(from, to));
    }
}
