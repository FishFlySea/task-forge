using System.Text;
using TaskForge.Core;

namespace TaskForge.Application;

public sealed class ImplementerAgent(
    ICodexClient codexClient) : IImplementerAgent
{
    private readonly ICodexClient _codexClient = codexClient;

    public Task<CodexRunResult> ExecuteAsync(
        TaskPacket taskPacket,
        string workspace,
        CodexRunKind kind,
        CancellationToken cancellationToken)
    {
        var request = new CodexRunRequest(
            workspace,
            BuildPrompt(taskPacket, kind),
            kind);

        return _codexClient.ExecuteAsync(
            request,
            cancellationToken);
    }

    internal static string BuildPrompt(
        TaskPacket taskPacket,
        CodexRunKind kind)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are the implementation worker for TaskForge.");
        builder.AppendLine();
        builder.AppendLine("Hard rules:");
        builder.AppendLine("- Do not spawn subagents.");
        builder.AppendLine("- Do not delegate work to other agents.");
        builder.AppendLine("- Work only in the current repository.");
        builder.AppendLine("- Do not perform broad repository exploration.");
        builder.AppendLine("- Prefer the supplied relevant files.");
        builder.AppendLine(
            "- Inspect additional files only when necessary.");
        builder.AppendLine(
            "- Do not commit, push, create branches, or create pull requests.");
        builder.AppendLine(
            "- Keep changes limited to the requested task.");
        builder.AppendLine(
            "- Do not modify files outside the supplied write scope.");
        builder.AppendLine(
            "- Do not modify TaskForge orchestration or budget rules unless the task explicitly asks for it.");
        builder.AppendLine();

        builder.AppendLine(
            kind == CodexRunKind.Implementation
                ? "Mode: implementation"
                : "Mode: correction of a failed implementation");
        builder.AppendLine();

        AppendSection(
            builder,
            "Goal",
            [taskPacket.Goal]);

        AppendSection(
            builder,
            "Acceptance criteria",
            taskPacket.AcceptanceCriteria);

        AppendSection(
            builder,
            "Relevant files",
            taskPacket.RelevantFiles);

        AppendSection(
            builder,
            "Allowed write scope",
            taskPacket.WriteScope);

        AppendSection(
            builder,
            "Observations",
            taskPacket.Observations);

        if (!string.IsNullOrWhiteSpace(
                taskPacket.Diagnostics))
        {
            AppendSection(
                builder,
                "Diagnostics from build/test",
                [taskPacket.Diagnostics]);
        }

        builder.AppendLine("When finished:");
        builder.AppendLine("1. summarize changed files;");
        builder.AppendLine(
            "2. report tests you ran, if any;");
        builder.AppendLine(
            "3. report unresolved issues;");
        builder.AppendLine("4. stop.");

        return builder.ToString();
    }

    private static void AppendSection(
        StringBuilder builder,
        string title,
        IEnumerable<string> items)
    {
        builder.AppendLine($"{title}:");

        foreach (var item in items
                     .Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            builder.AppendLine($"- {item}");
        }

        builder.AppendLine();
    }
}
