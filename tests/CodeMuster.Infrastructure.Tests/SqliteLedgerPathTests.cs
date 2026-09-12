using CodeMuster.Domain;
using CodeMuster.Infrastructure;

namespace CodeMuster.Infrastructure.Tests;

public class SqliteLedgerPathTests
{
    [Fact]
    public async Task Next_WithAPath_ReturnsOnlyUnitsWithAMemberUnderIt()
    {
        using var temp = new TempDirectory();
        using var ledger = await SqliteLedger.OpenAsync(temp.DatabasePath, CancellationToken.None);
        var api = Unit("slice:api", "src/Api/Program.cs", "src/Core/Service.cs");
        var web = Unit("file:web", "web/lib/api.ts");
        var both = Unit("slice:both", "web/app/page.tsx", "src/Api/Program.cs");
        var named = Unit("file:webbing", "webbing.md");
        await ledger.UpsertUnitsAsync(
            [api.Unit, web.Unit, both.Unit, named.Unit],
            [.. api.Members, .. web.Members, .. both.Members, .. named.Members],
            CancellationToken.None);

        var scoped = await ledger.NextAsync(10, null, "web", CancellationToken.None);
        var nested = await ledger.NextAsync(10, null, "src/Core", CancellationToken.None);
        var all = await ledger.NextAsync(10, null, null, CancellationToken.None);

        Assert.Equal(["file:web", "slice:both"], scoped.Select(u => u.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["slice:api"], nested.Select(u => u.Id));
        Assert.Equal(4, all.Count);
    }

    private static (Unit Unit, IReadOnlyList<UnitMember> Members) Unit(string id, params string[] paths)
    {
        var members = paths.Select(path => new UnitMember(id, path, null, "hash-" + path, 0)).ToList();
        return (new Unit(id, UnitKind.File, id, Fingerprints.Compute(members), UnitStatus.Pending, Fidelity.Full, null, null, null), members);
    }
}
