using TaskForge.Core;

namespace TaskForge.Application;

public interface IDiagnosticAgent
{
    Task<DiagnosticResult> DiagnoseAsync(
        TaskPacket taskPacket,
        ProcessResult failure,
        string gitSnapshot,
        CancellationToken cancellationToken);
}
