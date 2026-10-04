using TaskForge.Core;

namespace TaskForge.Application;

public static class TaskCommandPolicy
{
    public static IReadOnlyList<TaskCommandPolicyEntry> Derive(
        IReadOnlyList<TestTarget> testTargets)
    {
        var commands =
            new List<TaskCommandPolicyEntry>
            {
                new()
                {
                    Tool =
                        TaskCommandTool.DotnetBuild
                }
            };

        foreach (var target in testTargets
                     .Where(x => !string.IsNullOrWhiteSpace(
                         x.ProjectPath))
                     .Distinct())
        {
            commands.Add(
                new TaskCommandPolicyEntry
                {
                    Tool =
                        TaskCommandTool.DotnetTest,
                    ProjectPath =
                        NormalizeRepositoryPath(
                            target.ProjectPath),
                    Filter =
                        string.IsNullOrWhiteSpace(
                            target.Filter)
                            ? null
                            : target.Filter
                });
        }

        return commands;
    }

    public static IReadOnlyList<string> Validate(
        TaskPacket packet)
    {
        ArgumentNullException.ThrowIfNull(
            packet);

        var errors =
            new List<string>();

        if (!packet.AllowedCommands.Any(
                x => x.Tool
                     == TaskCommandTool.DotnetBuild))
        {
            errors.Add(
                "Command policy does not allow the authoritative dotnet build.");
        }

        foreach (var command in packet.AllowedCommands)
        {
            switch (command.Tool)
            {
                case TaskCommandTool.DotnetBuild:
                    if (!string.IsNullOrWhiteSpace(
                            command.ProjectPath)
                        || !string.IsNullOrWhiteSpace(
                            command.Filter))
                    {
                        errors.Add(
                            "DotnetBuild policy entry must not specify project/filter.");
                    }

                    break;

                case TaskCommandTool.DotnetTest:
                    var normalized =
                        NormalizeRepositoryPath(
                            command.ProjectPath);

                    if (normalized is null)
                    {
                        errors.Add(
                            "DotnetTest policy entry has an invalid project path.");
                    }

                    break;

                default:
                    errors.Add(
                        $"Unsupported command policy tool: {command.Tool}.");
                    break;
            }
        }

        foreach (var target in packet.TestTargets)
        {
            var project =
                NormalizeRepositoryPath(
                    target.ProjectPath);

            if (project is null)
            {
                errors.Add(
                    $"Test target has an invalid project path: '{target.ProjectPath}'.");
                continue;
            }

            var filter =
                string.IsNullOrWhiteSpace(
                    target.Filter)
                    ? null
                    : target.Filter;

            var allowed =
                packet.AllowedCommands.Any(
                    command =>
                        command.Tool
                            == TaskCommandTool.DotnetTest
                        && string.Equals(
                            NormalizeRepositoryPath(
                                command.ProjectPath),
                            project,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            NormalizeFilter(
                                command.Filter),
                            NormalizeFilter(
                                filter),
                            StringComparison.Ordinal));

            if (!allowed)
            {
                errors.Add(
                    $"Command policy does not allow test target '{target.ProjectPath}'.");
            }
        }

        return errors
            .Distinct(
                StringComparer.Ordinal)
            .ToArray();
    }

    private static string? NormalizeRepositoryPath(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return null;
        }

        var value =
            path.Replace(
                    '\\',
                    '/')
                .Trim();

        if (value.StartsWith(
                "/",
                StringComparison.Ordinal)
            || value.StartsWith(
                "\\",
                StringComparison.Ordinal)
            || value.Length >= 3
               && char.IsAsciiLetter(
                   value[0])
               && value[1] == ':'
               && value[2] is '/' or '\\')
        {
            return null;
        }

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

    private static string? NormalizeFilter(
        string? filter) =>
        string.IsNullOrWhiteSpace(
            filter)
            ? null
            : filter.Trim();
}
