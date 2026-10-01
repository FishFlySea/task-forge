using TaskForge.Core;

namespace TaskForge.Application;

public sealed record CodexRunRequest(
    string Workspace,
    string Prompt,
    CodexRunKind Kind);
