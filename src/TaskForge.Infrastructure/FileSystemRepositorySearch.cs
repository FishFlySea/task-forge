using TaskForge.Application;

namespace TaskForge.Infrastructure;

public sealed class FileSystemRepositorySearch(
    int maxInspectedFiles = 5000,
    int maxFileBytes = 131072,
    int maxContentChars = 16000) : IRepositorySearch
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            ".idea",
            "bin",
            "obj",
            "node_modules",
            "packages",
            "artifacts"
        };

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".csproj",
            ".props",
            ".targets",
            ".sln",
            ".slnx",
            ".json",
            ".yaml",
            ".yml",
            ".xml",
            ".config",
            ".md",
            ".sql",
            ".ps1",
            ".sh"
        };

    private readonly int _maxInspectedFiles = maxInspectedFiles;
    private readonly int _maxFileBytes = maxFileBytes;
    private readonly int _maxContentChars = maxContentChars;

    public async Task<IReadOnlyList<RepositoryFileCandidate>> SearchAsync(
        string repositoryPath,
        IReadOnlyCollection<string> searchTerms,
        int maxCandidates,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (maxCandidates <= 0)
        {
            return [];
        }

        var root = Path.GetFullPath(repositoryPath);
        var terms = searchTerms
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (terms.Length == 0)
        {
            return [];
        }

        var matches = new List<RepositoryFileCandidate>();
        var inspected = 0;

        foreach (var file in EnumerateFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++inspected > _maxInspectedFiles)
            {
                break;
            }

            var extension = Path.GetExtension(file);

            if (!AllowedExtensions.Contains(extension))
            {
                continue;
            }

            var fileInfo = new FileInfo(file);

            if (fileInfo.Length > _maxFileBytes)
            {
                continue;
            }

            string content;

            try
            {
                content = await File.ReadAllTextAsync(
                    file,
                    cancellationToken);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            var relativePath = Path
                .GetRelativePath(root, file)
                .Replace('\\', '/');

            var score = Score(
                relativePath,
                content,
                terms);

            if (score <= 0)
            {
                continue;
            }

            if (content.Length > _maxContentChars)
            {
                content = content[.._maxContentChars];
            }

            matches.Add(
                new RepositoryFileCandidate(
                    relativePath,
                    content,
                    score));
        }

        return matches
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates)
            .ToArray();
    }

    private static int Score(
        string relativePath,
        string content,
        IReadOnlyCollection<string> terms)
    {
        var score = 0;

        foreach (var term in terms)
        {
            if (relativePath.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (content.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
        }

        return score;
    }

    private static IEnumerable<string> EnumerateFiles(
        string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            string[] directories;
            string[] files;

            try
            {
                directories = Directory.GetDirectories(current);
                files = Directory.GetFiles(current);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);

                if (!ExcludedDirectories.Contains(name))
                {
                    pending.Push(directory);
                }
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}
