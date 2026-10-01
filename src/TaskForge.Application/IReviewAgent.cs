using TaskForge.Core;

namespace TaskForge.Application;

public interface IReviewAgent
{
    Task<ReviewResult> ReviewAsync(
        TaskPacket taskPacket,
        string gitSnapshot,
        ProcessResult buildResult,
        ProcessResult testResult,
        CancellationToken cancellationToken);
}
