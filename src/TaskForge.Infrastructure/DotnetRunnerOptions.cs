namespace TaskForge.Infrastructure;

public sealed record DotnetRunnerOptions
{
    public string Executable { get; init; } = "dotnet";

    public TimeSpan BuildTimeout { get; init; } =
        TimeSpan.FromMinutes(10);

    public TimeSpan TestTimeout { get; init; } =
        TimeSpan.FromMinutes(20);
}
