namespace TaskForge.Application;

public interface IRepositorySearch
{
    Task<IReadOnlyList<RepositoryFileCandidate>> SearchAsync(
        string repositoryPath,
        IReadOnlyCollection<string> searchTerms,
        int maxCandidates,
        CancellationToken cancellationToken);
}
