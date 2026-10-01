using System.Text;
using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

public sealed class DotnetRunner(
    DotnetRunnerOptions options) : IDotnetRunner
{
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
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ProjectPath)
                && File.Exists(
                    Path.Combine(
                        workspace,
                        x.ProjectPath)))
            .Distinct()
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

        foreach (var target in validTargets)
        {
            var arguments = new List<string>
            {
                "test",
                target.ProjectPath,
                "--no-build",
                "--nologo"
            };

            if (!string.IsNullOrWhiteSpace(
                    target.Filter))
            {
                arguments.Add("--filter");
                arguments.Add(target.Filter);
            }

            var result =
                await ExternalProcessRunner.RunAsync(
                    _options.Executable,
                    arguments,
                    workspace,
                    _options.TestTimeout,
                    cancellationToken);

            output.AppendLine(
                $"# {target.ProjectPath}");
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
}
