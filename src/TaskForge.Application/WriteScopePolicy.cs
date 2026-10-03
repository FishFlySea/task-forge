using TaskForge.Core;

namespace TaskForge.Application;

public static class WriteScopePolicy
{
    public static IReadOnlyList<string> Derive(
        IReadOnlyList<string> relevantFiles,
        IReadOnlyList<TestTarget> testTargets)
    {
        var scopes =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var path in relevantFiles)
        {
            AddScopeForPath(
                scopes,
                path);
        }

        foreach (var target in testTargets)
        {
            AddDirectoryScope(
                scopes,
                target.ProjectPath);
        }

        return scopes
            .OrderBy(
                x => x,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> FindViolations(
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<string> writeScope)
    {
        var scopes =
            writeScope
                .Select(NormalizeScope)
                .Where(x => x is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var violations =
            new List<string>();

        foreach (var changedFile in changedFiles)
        {
            var path =
                NormalizePath(
                    changedFile);

            if (path is null
                || !scopes.Any(
                    scope => Matches(
                        path,
                        scope)))
            {
                violations.Add(
                    changedFile);
            }
        }

        return violations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddScopeForPath(
        ISet<string> scopes,
        string path)
    {
        var normalized =
            NormalizePath(
                path);

        if (normalized is null)
        {
            return;
        }

        var directory =
            GetDirectory(
                normalized);

        if (directory is null)
        {
            scopes.Add(
                normalized);

            return;
        }

        var segmentCount =
            directory.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
                .Length;

        if (segmentCount >= 2)
        {
            scopes.Add(
                $"{directory}/**");
        }
        else
        {
            scopes.Add(
                normalized);
        }
    }

    private static void AddDirectoryScope(
        ISet<string> scopes,
        string projectPath)
    {
        var normalized =
            NormalizePath(
                projectPath);

        if (normalized is null)
        {
            return;
        }

        var directory =
            GetDirectory(
                normalized);

        if (directory is null)
        {
            scopes.Add(
                normalized);

            return;
        }

        scopes.Add(
            $"{directory}/**");
    }

    private static string? GetDirectory(
        string path)
    {
        var separator =
            path.LastIndexOf('/');

        return separator <= 0
            ? null
            : path[..separator];
    }

    private static string? NormalizeScope(
        string scope)
    {
        if (string.IsNullOrWhiteSpace(
                scope))
        {
            return null;
        }

        var value =
            scope.Replace(
                    '\\',
                    '/')
                .Trim();

        if (value.EndsWith(
                "/**",
                StringComparison.Ordinal))
        {
            var prefix =
                NormalizePath(
                    value[..^3]);

            return prefix is null
                ? null
                : $"{prefix}/**";
        }

        return NormalizePath(
            value);
    }

    private static string? NormalizePath(
        string path)
    {
        if (string.IsNullOrWhiteSpace(
                path)
            || Path.IsPathRooted(
                path))
        {
            return null;
        }

        var value =
            path.Replace(
                    '\\',
                    '/')
                .Trim()
                .TrimStart(
                    '/');

        var segments =
            value.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0
            || segments.Any(
                x => x is "." or ".."))
        {
            return null;
        }

        return string.Join(
            '/',
            segments);
    }

    private static bool Matches(
        string path,
        string scope)
    {
        var comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (scope.EndsWith(
                "/**",
                StringComparison.Ordinal))
        {
            var prefix =
                scope[..^3];

            return string.Equals(
                       path,
                       prefix,
                       comparison)
                   || path.StartsWith(
                       prefix + "/",
                       comparison);
        }

        return string.Equals(
            path,
            scope,
            comparison);
    }
}
