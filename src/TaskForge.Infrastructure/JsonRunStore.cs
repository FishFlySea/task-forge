using System.Text.Json;
using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class JsonRunStore : IRunStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private readonly string _runsDirectory;

    public JsonRunStore(string runsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runsDirectory);
        _runsDirectory = Path.GetFullPath(runsDirectory);
        Directory.CreateDirectory(_runsDirectory);
    }

    public static string GetDefaultRunsDirectory()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".taskforge");
        }

        return Path.Combine(localData, "TaskForge", "runs");
    }

    public async Task CreateAsync(
        TaskRequest request,
        RunMetadata metadata,
        CancellationToken cancellationToken)
    {
        var runDirectory = GetRunDirectory(metadata.Id);
        Directory.CreateDirectory(runDirectory);

        await WriteJsonAsync(
            Path.Combine(runDirectory, "request.json"),
            request,
            cancellationToken);

        await SaveMetadataAsync(
            metadata,
            cancellationToken);
    }

    public Task SaveMetadataAsync(
        RunMetadata metadata,
        CancellationToken cancellationToken) =>
        WriteJsonAsync(
            Path.Combine(
                GetRunDirectory(metadata.Id),
                "state.json"),
            metadata,
            cancellationToken);

    public Task SaveArtifactAsync<T>(
        TaskId id,
        string fileName,
        T value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!string.Equals(
                fileName,
                Path.GetFileName(fileName),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Artifact file name must not contain a directory path.",
                nameof(fileName));
        }

        return WriteJsonAsync(
            Path.Combine(
                GetRunDirectory(id),
                fileName),
            value,
            cancellationToken);
    }

    public async Task<RunMetadata?> GetAsync(
        TaskId id,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            GetRunDirectory(id),
            "state.json");

        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);

        return await JsonSerializer.DeserializeAsync<RunMetadata>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<RunMetadata>> ListAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var result = new List<RunMetadata>();

        foreach (var directory in Directory.EnumerateDirectories(
                     _runsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(
                directory,
                "state.json");

            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(path);

                var metadata =
                    await JsonSerializer.DeserializeAsync<RunMetadata>(
                        stream,
                        JsonOptions,
                        cancellationToken);

                if (metadata is not null)
                {
                    result.Add(metadata);
                }
            }
            catch (JsonException)
            {
                // Keep listing usable if a previous run was interrupted mid-write.
            }
        }

        return result
            .OrderByDescending(x => x.StartedAt)
            .Take(limit)
            .ToArray();
    }

    private string GetRunDirectory(TaskId id) =>
        Path.Combine(
            _runsDirectory,
            id.Value);

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Unable to resolve run directory."));

        await using var stream = File.Create(path);

        await JsonSerializer.SerializeAsync(
            stream,
            value,
            JsonOptions,
            cancellationToken);
    }
}
