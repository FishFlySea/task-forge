using TaskForge.Application;
using TaskForge.Core;

namespace TaskForge.Application.Tests;

public sealed class WriteScopePolicyTests
{
    [Fact]
    public void Derive_uses_module_directory_and_test_project_directory()
    {
        var scope =
            WriteScopePolicy.Derive(
                [
                    "src/Operations/Services/BindingService.cs",
                    "README.md"
                ],
                [
                    new TestTarget(
                        "tests/Operations.Tests/Operations.Tests.csproj")
                ]);

        Assert.Contains(
            "src/Operations/Services/**",
            scope);

        Assert.Contains(
            "tests/Operations.Tests/**",
            scope);

        Assert.Contains(
            "README.md",
            scope);
    }

    [Fact]
    public void FindViolations_accepts_exact_and_recursive_scopes()
    {
        var violations =
            WriteScopePolicy.FindViolations(
                [
                    "src/Operations/Services/BindingService.cs",
                    "src/Operations/Services/NewHelper.cs",
                    "README.md"
                ],
                [
                    "src/Operations/Services/**",
                    "README.md"
                ]);

        Assert.Empty(
            violations);
    }

    [Fact]
    public void FindViolations_rejects_out_of_scope_and_traversal_paths()
    {
        var violations =
            WriteScopePolicy.FindViolations(
                [
                    "src/Allowed/File.cs",
                    "src/Other/File.cs",
                    "../escape.txt"
                ],
                [
                    "src/Allowed/**"
                ]);

        Assert.Equal(
            [
                "src/Other/File.cs",
                "../escape.txt"
            ],
            violations);
    }
}
