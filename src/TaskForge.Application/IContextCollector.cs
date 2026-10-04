namespace TaskForge.Application;

public interface IContextCollector
{
    Task<ContextCollectionResult> CollectAsync(
        string repositoryPath,
        IReadOnlyList<string> relevantFiles,
        IReadOnlyCollection<string> searchTerms,
        CancellationToken cancellationToken);
}
