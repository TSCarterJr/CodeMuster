namespace CodeMuster.Domain.Tests;

public class StoredCodeMapTests
{
    private static StoredCodeMap With(IReadOnlyList<string> diagnostics, IReadOnlyList<string>? failed = null) =>
        new("abc", "2026-09-27T10:00:00.0000000Z", new CodeMap([], [], [], new ResolutionStats(0, 0, []), diagnostics), ["typescript"], failed ?? []);

    [Fact]
    public void Http_link_diagnostics_do_not_make_the_map_partial()
    {
        var stored = With(["http: web/lib/api.ts:14 GET /customers matches no endpoint", "http: GET /quotes/{id} is not called from the mapped UI"]);

        Assert.False(stored.IsPartial);
    }

    [Fact]
    public void A_mapper_diagnostic_or_a_failed_language_makes_the_map_partial()
    {
        Assert.True(With(["http: GET /quotes/{id} is not called from the mapped UI", "web/a.ts:3: TS1005: ';' expected."]).IsPartial);
        Assert.True(With([], ["csharp"]).IsPartial);
    }
}
