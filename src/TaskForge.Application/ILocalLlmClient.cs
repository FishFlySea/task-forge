namespace TaskForge.Application;

public interface ILocalLlmClient
{
    Task<T> CompleteStructuredAsync<T>(
        LocalLlmRequest request,
        CancellationToken cancellationToken)
        where T : class;
}
