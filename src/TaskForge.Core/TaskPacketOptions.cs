namespace TaskForge.Core;

public sealed record TaskPacketOptions
{
    public int MaxContextCharacters { get; init; } =
        32_000;

    public int MaxSpanCharacters { get; init; } =
        8_000;

    public int ApproximateCharactersPerToken { get; init; } =
        4;
}
