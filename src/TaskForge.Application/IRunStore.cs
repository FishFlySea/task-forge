using TaskForge.Core;

namespace TaskForge.Application;

public interface IRunStore
{
    Task CreateAsync(
        TaskRequest request,
        RunMetadata metadata,
        CancellationToken cancellationToken);

    Task SaveMetadataAsync(
        RunMetadata metadata,
        CancellationToken cancellationToken);

    Task SaveArtifactAsync<T>(
        TaskId id,
        string fileName,
        T value,
        CancellationToken cancellationToken);

    Task<RunMetadata?> GetAsync(
        TaskId id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RunMetadata>> ListAsync(
        int limit,
        CancellationToken cancellationToken);
}
