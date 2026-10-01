using TaskForge.Core;

namespace TaskForge.Application;

public interface IImplementerAgent
{
    Task<CodexRunResult> ExecuteAsync(
        TaskPacket taskPacket,
        string workspace,
        CodexRunKind kind,
        CancellationToken cancellationToken);
}
