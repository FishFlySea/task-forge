namespace TaskForge.Core;

public readonly record struct TaskId(string Value)
{
    public static TaskId New(DateTimeOffset timestamp)
    {
        var suffix = Guid.NewGuid().ToString("N")[..4];
        return new TaskId($"tf-{timestamp:yyyyMMdd-HHmmss}-{suffix}");
    }

    public override string ToString() => Value;
}
