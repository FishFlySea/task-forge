using System.ComponentModel;
using System.Diagnostics;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

internal static class ExternalProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Unable to start '{executable}'.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Unable to start '{executable}'. Ensure it is installed and available on PATH.",
                exception);
        }

        var stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        var stderrTask =
            process.StandardError.ReadToEndAsync();

        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeoutCancellation.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(
                timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            await Task.WhenAll(
                stdoutTask,
                stderrTask);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException(
                $"Process '{executable}' exceeded timeout {timeout}.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new ProcessResult(
            process.ExitCode,
            stdout,
            stderr);
    }

    private static void TryKill(
        Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(
                    entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup during cancellation/timeout.
        }
    }
}
