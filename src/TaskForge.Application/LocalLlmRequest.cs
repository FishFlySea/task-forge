namespace TaskForge.Application;

public sealed record LocalLlmRequest(
    string SystemPrompt,
    string UserPrompt);
