using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class ModelsTests
{
    [Fact]
    public void Records_CompareByValue()
    {
        var a = new FileRecord("src/A.cs", "csharp", "h1", 10, "2026-09-10T00:00:00.0000000Z", "2026-09-10T00:00:00.0000000Z", "2026-09-10T00:00:00.0000000Z", null, null, null, null, null, null);
        var b = a with { };

        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { ContentHash = "h2" });
        Assert.Equal(new Unit("file:src/A.cs", UnitKind.File, "src/A.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null),
            new Unit("file:src/A.cs", UnitKind.File, "src/A.cs", "fp", UnitStatus.Pending, Fidelity.Full, null, null, null));
    }

    [Fact]
    public void Fingerprint_IsDeterministicOverMemberOrder()
    {
        var first = new UnitMember("u", "src/A.cs", null, "aaa", 0);
        var second = new UnitMember("u", "src/B.cs", "B.Run", "bbb", 1);

        var forward = Fingerprints.Compute([first, second]);
        var reversed = Fingerprints.Compute([second, first]);

        Assert.Equal(forward, reversed);
        Assert.Equal(64, forward.Length);
        Assert.NotEqual(forward, Fingerprints.Compute([first, second with { MemberHash = "ccc" }]));
        Assert.NotEqual(forward, Fingerprints.Compute([first, second with { Symbol = null }]));
    }

    [Fact]
    public void UnitIds_AreKindPrefixed()
    {
        Assert.Equal("file:src/A.cs", UnitIds.File("src/A.cs"));
        Assert.Equal("orphan:src/A.cs", UnitIds.Orphan("src/A.cs"));
        Assert.Equal("slice:M:Api.QuotesController.Get(System.Int32)", UnitIds.Slice("M:Api.QuotesController.Get(System.Int32)"));
    }

    [Fact]
    public void Timestamps_FormatAsUtcIso8601()
    {
        var value = new DateTimeOffset(2026, 9, 10, 23, 20, 43, 657, TimeSpan.FromHours(-5));

        Assert.Equal("2026-09-11T04:20:43.6570000Z", Timestamps.Format(value));
    }
}
