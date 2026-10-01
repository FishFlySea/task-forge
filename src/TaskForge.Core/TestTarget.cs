namespace TaskForge.Core;

public sealed record TestTarget(
    string ProjectPath,
    string? Filter = null);
