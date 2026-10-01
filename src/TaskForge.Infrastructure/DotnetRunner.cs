using System.Text;
using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class DotnetRunner(
    DotnetRunnerOptions options) : IDotnetRunner
{
    private static readonly HashSet<string> ProjectExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".csproj",
            ".fsproj",
            ".vbproj"
        };

    private readonly DotnetRunnerOptions _options =
        options ?? throw new ArgumentNullException(nameof(options));

    public Task<ProcessResult> BuildAsync(
        string workspace,
        CancellationToken cancellationToken) =>
        ExternalProcessRunner.RunAsync(
            _options.Executable,
            ["build", "--nologo"],
            workspace,
            _options.BuildTimeout,
            cancellationToken);

    public async Task<ProcessResult> TestAsync(
        string workspace,
        IReadOnlyList<TestTarget> targets,
        CancellationToken cancellationToken)
    {
        var validTargets = targets
            .Select(target => new
            {
                Target = target,
                ProjectPath = ResolveProjectPath(
                    workspace,
                    target.ProjectPath)
            })
            .Where(x => x.ProjectPath is not null)
            .GroupBy(
                x => (x.ProjectPath, x.Target.Filter),
                StringTupleComparer.Instance)
            .Select(x => x.First())
            .ToArray();

        if (validTargets.Length == 0)
        {
            return await ExternalProcessRunner.RunAsync(
                _options.Executable,
                ["test", "--no-build", "--nologo"],
                workspace,
                _options.TestTimeout,
                cancellationToken);
        }

        var output = new StringBuilder();
        var errors = new StringBuilder();

        foreach (var item in validTargets)
        {
            var arguments = new List<string>
            {
                "test",
                item.ProjectPath!,
                "--no-build",
                "--nologo"
            };

            if (!string.IsNullOrWhiteSpace(
                    item.Target.Filter))
            {
                arguments.Add("--filter");
                arguments.Add(item.Target.Filter);
            }

            var result =
                await ExternalProcessRunner.RunAsync(
                    _options.Executable,
                    arguments,
                    workspace,
                    _options.TestTimeout,
                    cancellationToken);

            output.AppendLine(
                $"# {item.Target.ProjectPath}");
            output.AppendLine(
                result.StandardOutput);

            errors.AppendLine(
                result.StandardError);

            if (!result.Success)
            {
                return new ProcessResult(
                    result.ExitCode,
                    output.ToString(),
                    errors.ToString());
            }
        }

        return new ProcessResult(
            0,
            output.ToString(),
            errors.ToString());
    }

    internal static string? ResolveProjectPath(
        string workspace,
        string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)
            || Path.IsPathRooted(projectPath))
        {
            return null;
        }

        var root = Path.GetFullPath(workspace);
        var fullPath = Path.GetFullPath(
            Path.Combine(root, projectPath));

        var rootPrefix = root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(
                rootPrefix,
                comparison))
        {
            return null;
        }

        if (!ProjectExtensions.Contains(
                Path.GetExtension(fullPath)))
        {
            return null;
        }

        return File.Exists(fullPath)
            ? fullPath
            : null;
    }

    private sealed class StringTupleComparer :
        IEqualityComparer<(string? ProjectPath, string? Filter)>
    {
        public static StringTupleComparer Instance { get; } =
            new();

        public bool Equals(
            (string? ProjectPath, string? Filter) x,
            (string? ProjectPath, string? Filter) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(
                x.ProjectPath,
                y.ProjectPath)
            && StringComparer.Ordinal.Equals(
                x.Filter,
                y.Filter);

        public int GetHashCode(
            (string? ProjectPath, string? Filter) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(
                    obj.ProjectPath ?? string.Empty),
                StringComparer.Ordinal.GetHashCode(
                    obj.Filter ?? string.Empty));
    }
}
