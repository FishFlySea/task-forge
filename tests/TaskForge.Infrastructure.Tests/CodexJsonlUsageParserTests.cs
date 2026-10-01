using TaskForge.Infrastructure;

namespace TaskForge.Infrastructure.Tests;

public sealed class CodexJsonlUsageParserTests
{
    [Fact]
    public void Parses_last_completed_turn_usage()
    {
        const string jsonl = """
            {"type":"thread.started","thread_id":"abc"}
            not-json
            {"type":"turn.completed","usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":10}}
            {"type":"turn.completed","usage":{"input_tokens":1200,"cached_input_tokens":800,"cache_write_input_tokens":100,"output_tokens":250,"reasoning_output_tokens":90}}
            """;

        var usage =
            CodexJsonlUsageParser.Parse(
                jsonl);

        Assert.NotNull(
            usage);

        Assert.Equal(
            1200,
            usage.InputTokens);

        Assert.Equal(
            800,
            usage.CachedInputTokens);

        Assert.Equal(
            100,
            usage.CacheWriteInputTokens);

        Assert.Equal(
            250,
            usage.OutputTokens);

        Assert.Equal(
            90,
            usage.ReasoningOutputTokens);
    }

    [Fact]
    public void Missing_optional_usage_fields_are_zero()
    {
        const string jsonl = """
            {"type":"turn.completed","usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":10}}
            """;

        var usage =
            CodexJsonlUsageParser.Parse(
                jsonl);

        Assert.NotNull(
            usage);

        Assert.Equal(
            0,
            usage.CacheWriteInputTokens);

        Assert.Equal(
            0,
            usage.ReasoningOutputTokens);
    }

    [Fact]
    public void Failed_turn_without_usage_returns_null()
    {
        const string jsonl = """
            {"type":"error","message":"shared rollout token budget exhausted"}
            {"type":"turn.failed","error":{"message":"shared rollout token budget exhausted"}}
            """;

        Assert.Null(
            CodexJsonlUsageParser.Parse(
                jsonl));
    }
}
