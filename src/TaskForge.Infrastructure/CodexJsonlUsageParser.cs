using System.Text.Json;
using TaskForge.Core;

namespace TaskForge.Infrastructure;

internal static class CodexJsonlUsageParser
{
    public static CodexUsage? Parse(
        string jsonl)
    {
        if (string.IsNullOrWhiteSpace(jsonl))
        {
            return null;
        }

        CodexUsage? lastUsage = null;

        using var reader =
            new StringReader(jsonl);

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document =
                    JsonDocument.Parse(line);

                var root =
                    document.RootElement;

                if (!root.TryGetProperty(
                        "type",
                        out var type)
                    || !string.Equals(
                        type.GetString(),
                        "turn.completed",
                        StringComparison.Ordinal)
                    || !root.TryGetProperty(
                        "usage",
                        out var usage)
                    || usage.ValueKind
                        != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetInt64(
                        usage,
                        "input_tokens",
                        out var inputTokens)
                    || !TryGetInt64(
                        usage,
                        "output_tokens",
                        out var outputTokens))
                {
                    continue;
                }

                lastUsage =
                    new CodexUsage(
                        InputTokens:
                            inputTokens,
                        CachedInputTokens:
                            GetInt64OrZero(
                                usage,
                                "cached_input_tokens"),
                        CacheWriteInputTokens:
                            GetInt64OrZero(
                                usage,
                                "cache_write_input_tokens"),
                        OutputTokens:
                            outputTokens,
                        ReasoningOutputTokens:
                            GetInt64OrZero(
                                usage,
                                "reasoning_output_tokens"));
            }
            catch (JsonException)
            {
                // Keep the run usable when stdout contains
                // a malformed or non-JSON line.
            }
        }

        return lastUsage;
    }

    private static long GetInt64OrZero(
        JsonElement element,
        string propertyName) =>
        TryGetInt64(
            element,
            propertyName,
            out var value)
            ? value
            : 0;

    private static bool TryGetInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;

        return element.TryGetProperty(
                   propertyName,
                   out var property)
               && property.ValueKind
                   == JsonValueKind.Number
               && property.TryGetInt64(
                   out value);
    }
}
