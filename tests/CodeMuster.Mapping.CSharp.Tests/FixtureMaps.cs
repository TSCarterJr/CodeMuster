using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public sealed class FixtureMaps : IAsyncLifetime
{
    private readonly Dictionary<string, CodeMap> maps = new(StringComparer.Ordinal);

    public CodeMap this[string fixture] => maps[fixture];

    public async Task InitializeAsync()
    {
        var mixed = Fixtures.MapInPlaceAsync("mixed-repo", "MixedRepo.sln");
        var minimal = Fixtures.MapInPlaceAsync("minimal-api", "MinimalApi.sln");
        maps["mixed-repo"] = await mixed;
        maps["minimal-api"] = await minimal;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
