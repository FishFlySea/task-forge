using TaskForge.Core;

namespace TaskForge.Application;

public sealed class PlannerAgent(ILocalLlmClient localLlmClient) : IPlannerAgent
{
    private readonly ILocalLlmClient _localLlmClient = localLlmClient;

    public Task<PlanResult> PlanAsync(
        TaskRequest request,
        CancellationToken cancellationToken)
    {
        var prompt = new LocalLlmRequest(
            """
            You are the planning agent for TaskForge.
            Convert a software-development task into a compact repository search plan.

            Rules:
            - Do not write implementation code.
            - Do not assume files that have not been inspected.
            - Search terms should be concrete symbols, concepts, error fragments, or filenames.
            - Acceptance criteria must be testable.
            - Keep the result concise.
            """,
            $"""
            Goal:
            {request.Goal}

            Repository:
            {request.RepositoryPath}

            Produce the structured planning result.
            """);

        return _localLlmClient.CompleteStructuredAsync<PlanResult>(
            prompt,
            cancellationToken);
    }
}
