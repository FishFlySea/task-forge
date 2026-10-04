using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class FileSystemContextCollector(
    TaskPacketOptions options,
    int maxFileBytes = 262_144) : IContextCollector
{
    private readonly TaskPacketOptions _options =
        ValidateOptions(options);

    private readonly int _maxFileBytes =
        maxFileBytes > 0
            ? maxFileBytes
            : throw new ArgumentOutOfRangeException(
                nameof(maxFileBytes));

    public async Task<ContextCollectionResult> CollectAsync(
        string repositoryPath,
        IReadOnlyList<string> relevantFiles,
        IReadOnlyCollection<string> searchTerms,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            repositoryPath);

        var root =
            Path.GetFullPath(
                repositoryPath);

        var terms =
            searchTerms
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var spans =
            new List<ContextSpan>();

        var usedCharacters = 0;

        foreach (var relativePath in relevantFiles
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining =
                _options.MaxContextCharacters
                - usedCharacters;

            if (remaining <= 0)
            {
                break;
            }

            var fullPath =
                TryResolveRepositoryPath(
                    root,
                    relativePath);

            if (fullPath is null
                || !File.Exists(fullPath))
            {
                continue;
            }

            var info =
                new FileInfo(
                    fullPath);

            if (info.Length > _maxFileBytes)
            {
                continue;
            }

            string content;

            try
            {
                content =
                    await File.ReadAllTextAsync(
                        fullPath,
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

            var maxSpanCharacters =
                Math.Min(
                    _options.MaxSpanCharacters,
                    remaining);

            var span =
                CreateSpan(
                    relativePath,
                    content,
                    terms,
                    maxSpanCharacters);

            if (span is null)
            {
                continue;
            }

            spans.Add(
                span);

            usedCharacters +=
                span.Content.Length;
        }

        return new ContextCollectionResult
        {
            Spans = spans,
            Budget = new TaskPacketBudget
            {
                MaxContextCharacters =
                    _options.MaxContextCharacters,
                UsedContextCharacters =
                    usedCharacters,
                EstimatedContextTokens =
                    EstimateTokens(
                        usedCharacters,
                        _options.ApproximateCharactersPerToken)
            }
        };
    }

    private static ContextSpan? CreateSpan(
        string path,
        string content,
        IReadOnlyCollection<string> terms,
        int maxCharacters)
    {
        if (maxCharacters <= 0
            || string.IsNullOrEmpty(
                content))
        {
            return null;
        }

        var normalized =
            content
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        var lines =
            normalized.Split(
                '\n');

        if (lines.Length == 0)
        {
            return null;
        }

        var matchLine =
            FindMatchLine(
                lines,
                terms);

        var start =
            Math.Max(
                0,
                matchLine - 20);

        var builder =
            new System.Text.StringBuilder();

        var end = start;

        for (var i = start;
             i < lines.Length;
             i++)
        {
            var line =
                lines[i];

            var additional =
                line.Length
                + (builder.Length == 0 ? 0 : 1);

            if (builder.Length > 0
                && builder.Length + additional
                    > maxCharacters)
            {
                break;
            }

            if (builder.Length == 0
                && line.Length > maxCharacters)
            {
                builder.Append(
                    line.AsSpan(
                        0,
                        maxCharacters));

                end = i;
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(
                line);

            end = i;

            if (builder.Length >= maxCharacters)
            {
                break;
            }
        }

        if (builder.Length == 0)
        {
            return null;
        }

        return new ContextSpan
        {
            Path =
                path.Replace(
                    '\\',
                    '/'),
            StartLine =
                start + 1,
            EndLine =
                end + 1,
            Content =
                builder.ToString()
        };
    }

    private static int FindMatchLine(
        IReadOnlyList<string> lines,
        IReadOnlyCollection<string> terms)
    {
        if (terms.Count == 0)
        {
            return 0;
        }

        for (var i = 0;
             i < lines.Count;
             i++)
        {
            if (terms.Any(
                    term => lines[i].Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        return 0;
    }

    private static string? TryResolveRepositoryPath(
        string root,
        string relativePath)
    {
        var candidate =
            relativePath
                .Replace(
                    '\\',
                    '/')
                .Trim();

        if (candidate.Length == 0
            || candidate.StartsWith(
                "/",
                StringComparison.Ordinal)
            || candidate.StartsWith(
                "\\",
                StringComparison.Ordinal)
            || candidate.Length >= 3
               && char.IsAsciiLetter(
                   candidate[0])
               && candidate[1] == ':'
               && candidate[2] is '/' or '\\')
        {
            return null;
        }

        var segments =
            candidate.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0
            || segments.Any(
                x => x is "." or ".."))
        {
            return null;
        }

        var fullPath =
            Path.GetFullPath(
                Path.Combine(
                    root,
                    Path.Combine(
                        segments)));

        var rootPrefix =
            root.EndsWith(
                Path.DirectorySeparatorChar)
                ? root
                : root
                  + Path.DirectorySeparatorChar;

        var comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return fullPath.StartsWith(
                   rootPrefix,
                   comparison)
            ? fullPath
            : null;
    }

    private static int EstimateTokens(
        int characters,
        int charactersPerToken) =>
        characters == 0
            ? 0
            : (int)Math.Ceiling(
                (double)characters
                / charactersPerToken);

    private static TaskPacketOptions ValidateOptions(
        TaskPacketOptions options)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        if (options.MaxContextCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxContextCharacters));
        }

        if (options.MaxSpanCharacters <= 0
            || options.MaxSpanCharacters
               > options.MaxContextCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaxSpanCharacters));
        }

        if (options.ApproximateCharactersPerToken <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.ApproximateCharactersPerToken));
        }

        return options;
    }
}
