using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;

namespace CodeMuster.Application.Tests;

public class UxSourceSnapshotTests
{
    [Theory]
    [InlineData("backend")]
    [InlineData("new_file")]
    [InlineData("deleted")]
    [InlineData("settings")]
    [InlineData("disabled")]
    public async Task ChangesAfterScanCannotBeRecordedAsCurrentBrowserEvidence(string change)
    {
        var tree = new FakeSourceTree().Add("web/Invoice.tsx", "export default () => <p>Balance</p>;").Add("api/Payment.cs", "class Payment { }");
        var ledger = new FakeLedger();
        var hasher = new FakeContentHasher();
        var config = Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true } };
        await new Scan(ledger, tree, hasher, new FakeClock(), config).RunAsync(CancellationToken.None);
        if (change == "backend") tree.Add("api/Payment.cs", "class Payment { void Charge() {} }");
        if (change == "new_file") tree.Add("api/New.cs", "class New { }");
        if (change == "deleted") tree.Files.RemoveAll(f => f.Path == "api/Payment.cs");
        if (change == "settings") config = config with { UserExperience = config.UserExperience with { BaseUrl = "https://example.test" } };
        if (change == "disabled") config = config with { UserExperience = config.UserExperience with { Enabled = false } };
        await Assert.ThrowsAsync<JsonException>(() => UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config, "web/Invoice.tsx", CancellationToken.None));
    }

    [Fact]
    public async Task CurrentIncludedSnapshotCanBeReviewed()
    {
        var tree = new FakeSourceTree().Add("web/Invoice.tsx", "export default () => <p>Balance</p>;");
        var ledger = new FakeLedger();
        var hasher = new FakeContentHasher();
        var config = Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true } };
        await new Scan(ledger, tree, hasher, new FakeClock(), config).RunAsync(CancellationToken.None);
        await UxSourceSnapshot.CheckAsync(ledger, tree, hasher, config, "web/Invoice.tsx", CancellationToken.None);
    }
}
