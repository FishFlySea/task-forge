namespace TaskForge.Application;

public sealed class CodexBudgetExceededException(
    string message) : InvalidOperationException(message);
