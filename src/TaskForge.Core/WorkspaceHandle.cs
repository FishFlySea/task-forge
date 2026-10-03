namespace TaskForge.Core;

public sealed record WorkspaceHandle(
    string RepositoryPath,
    string Path,
    string BaseCommit);
