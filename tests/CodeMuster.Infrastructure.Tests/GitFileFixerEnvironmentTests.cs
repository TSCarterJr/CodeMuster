using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

/// <summary>Tests that change this process's environment, run on their own after every parallel test so no other git call sees it.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    public const string Name = "process environment";
}

[Collection(ProcessEnvironment.Name)]
public sealed class GitFileFixerEnvironmentTests : IDisposable
{
    private readonly TempRepo repo = new();

    [Theory]
    [InlineData("--unified=0")]
    [InlineData("-u0")]
    public async Task GIT_DIFF_OPTS_in_the_environment_cannot_strip_the_patch_context(string options)
    {
        repo.WriteFile("src/c.cs", "class C\n{\n    int a;\n    int b;\n    int c;\n    int d;\n    int e;\n}\n");
        repo.Commit("multi-line file");
        using var fixer = new GitFileFixer(repo.Root, dir => new Agent(() =>
            File.WriteAllText(Path.Combine(dir, "src", "c.cs"), "class C\n{\n    int a;\n    int b;\n    int repaired;\n    int d;\n    int e;\n}\n")));
        var previous = Environment.GetEnvironmentVariable("GIT_DIFF_OPTS");
        FileFixEdit edit;
        Environment.SetEnvironmentVariable("GIT_DIFF_OPTS", options);
        try
        {
            edit = await fixer.RunAsync("src/c.cs", "pack", CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_DIFF_OPTS", previous);
        }

        await fixer.ReleaseAsync(edit, CancellationToken.None);
        await new GitWorkspace(repo.Root).ApplyPatchAsync(edit.Patch, CancellationToken.None);

        Assert.Contains("int repaired;", File.ReadAllText(Path.Combine(repo.Root, "src", "c.cs")));
    }

    public void Dispose() => repo.Dispose();

    private sealed class Agent(Action edit) : IAgentAdapter
    {
        public AgentIdentity Identity { get; } = new("fake", null, null);

        public Task<string> RunAsync(string pack, CancellationToken cancellationToken)
        {
            edit();
            return Task.FromResult("response");
        }
    }
}
