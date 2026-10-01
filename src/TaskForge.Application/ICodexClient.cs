using TaskForge.Core;

namespace TaskForge.Application;

public interface ICodexClient
{
    Task<CodexRunResult> ExecuteAsync(
        CodexRunRequest request,
        CancellationToken cancellationToken);
}
