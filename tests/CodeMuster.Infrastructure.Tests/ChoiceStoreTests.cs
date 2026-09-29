using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class ChoiceStoreTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "codemuster-choices-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    [Fact]
    public void Choices_RoundTripPerRepository_UnderTheStateDirectory()
    {
        var store = new ChoiceStore(home);
        var first = Path.Combine(home, "repos", "a");
        var second = Path.Combine(home, "repos", "b");
        var choices = new RememberedChoices("claude", "claude-opus-5-5", "high", 4, ["scan", "run", "report"]);

        store.Save(first, choices);

        Assert.Equal(choices.Agent, store.Load(first)!.Agent);
        Assert.Equal(["scan", "run", "report"], store.Load(first)!.Steps);
        Assert.Null(store.Load(second));
        Assert.Single(Directory.GetFiles(Path.Combine(home, "choices"), "*.json"));
    }

    [Fact]
    public void AMissingOrUnreadableFile_IsNoChoice()
    {
        var store = new ChoiceStore(home);
        var repo = Path.Combine(home, "repo");
        store.Save(repo, new RememberedChoices("codex", null, null, 2, null));
        File.WriteAllText(Directory.GetFiles(Path.Combine(home, "choices"))[0], "{ not json");

        Assert.Null(store.Load(repo));
        Assert.Null(new ChoiceStore(Path.Combine(home, "elsewhere")).Load(repo));
    }

    [Fact]
    public void TryResolve_IsNullForAMissingProgram_AndInstalledListsOnlyWhatIsOnPath()
    {
        Assert.Null(ExecutableResolver.TryResolve("codemuster-no-such-program-" + Guid.NewGuid().ToString("N")));
        Assert.All(AgentAdapters.Installed(), name => Assert.Contains(name, AgentAdapters.Names));
    }
}
