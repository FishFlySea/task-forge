namespace TaskForge.Application;

public sealed record RepositoryFileCandidate(
    string Path,
    string Content,
    int Score);
