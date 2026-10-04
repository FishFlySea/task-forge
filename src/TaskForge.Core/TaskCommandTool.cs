using System.Text.Json.Serialization;

namespace TaskForge.Core;

[JsonConverter(
    typeof(JsonStringEnumConverter<TaskCommandTool>))]
public enum TaskCommandTool
{
    DotnetBuild,
    DotnetTest
}
