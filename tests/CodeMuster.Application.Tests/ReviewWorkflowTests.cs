using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class ReviewWorkflowTests
{
    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private static Config Enabled => Config.Default with { UserExperience = new UserExperienceSettings { Enabled = true } };

    private Task<ScanResult> ScanAsync(Config? config = null) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, config ?? Enabled).RunAsync(CancellationToken.None);

    [Fact]
    public void ExistingSettingsDoNotEnableNewReviews()
    {
        var config = ConfigJson.Parse("""{"lenses": []}""");
        Assert.False(config.DeadCode);
        Assert.False(config.UserExperience.Enabled);
    }

    [Theory]
    [InlineData("\"user_experience\":null")]
    [InlineData("\"user_experience\":true")]
    [InlineData("\"user_experience\":{\"include\":null}")]
    [InlineData("\"user_experience\":{\"base_url\":\"file:///private\"}")]
    [InlineData("\"dead_code\":\"yes\"")]
    public void MalformedReviewSettingsFailClearly(string setting) =>
        Assert.Throws<JsonException>(() => ConfigJson.Parse("{\"lenses\":[]," + setting + "}"));

    [Fact]
    public async Task ScanCreatesUxWorkOnlyForUiAndHonorsExclusions()
    {
        tree.Add("web/Invoice.tsx", "export function Invoice() { return <button>Charge</button>; }");
        tree.Add("web/theme.css", "body { color: gray; }");
        tree.Add("api/Invoice.cs", "class Invoice { }");
        tree.Add("api/ui-service.ts", "export function charge() { }");
        tree.Add("web/ignored.tsx", "export const View = () => <p />;");
        await ScanAsync(Enabled with { UserExperience = new UserExperienceSettings { Enabled = true, Exclude = ["web/ignored.tsx"] } });
        Assert.Equal(new[] { "web/Invoice.tsx", "web/theme.css" }, ledger.Units.Where(u => u.Kind == UnitKind.Ux).Select(u => u.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BackendOnlyStatusSaysUxNotApplicable()
    {
        tree.Add("api/Invoice.cs", "class Invoice { }");
        await ScanAsync();
        Assert.Contains("UX: not applicable", (await new Status(ledger, Enabled).RunAsync(CancellationToken.None)).Render());
        Assert.DoesNotContain(ledger.Units, u => u.Kind == UnitKind.Ux);
    }

    [Fact]
    public async Task DisablingUxRetiresItsWork()
    {
        tree.Add("web/Invoice.tsx", "export default () => <p />;");
        await ScanAsync();
        await ScanAsync(Config.Default);
        Assert.All(ledger.Units.Where(u => u.Kind == UnitKind.Ux), u => Assert.Equal(UnitStatus.Retired, u.Status));
    }

    [Fact]
    public async Task SharedStylesAndSiblingScreensInvalidateCompletedUx()
    {
        tree.Add("web/Invoice.tsx", "export default () => <p />;");
        tree.Add("web/Schedule.tsx", "export default () => <button>Charge</button>;");
        tree.Add("web/theme.css", "p { color: black; }");
        await ScanAsync();
        var unit = ledger.Units.Single(u => u.Kind == UnitKind.Ux && u.Key == "web/Invoice.tsx");
        var lensHash = Config.HashOf(Enabled.LensesFor([(unit.Key, Languages.TypeScript)]));
        await ledger.RecordAnalysisAsync(new Analysis(unit.Id, unit.Fingerprint, lensHash, "now", true, "review", null), [], CancellationToken.None);
        tree.Add("web/theme.css", "p { color: #aaa; }");
        await ScanAsync();
        Assert.Equal(UnitStatus.Stale, ledger.Units.Single(u => u.Id == unit.Id).Status);
    }

    [Fact]
    public async Task SourceOnlyResponseCannotCompleteUx()
    {
        tree.Add("web/Invoice.tsx", "export default () => <p />;");
        await ScanAsync();
        var unit = ledger.Units.Single(u => u.Kind == UnitKind.Ux);
        var result = await new Done(ledger, clock, Enabled).RunAsync(unit.Id, unit.Fingerprint, """{"summary":"Looks good","findings":[]}""", CancellationToken.None);
        Assert.NotEqual(DoneOutcome.Recorded, result.Outcome);
        Assert.NotEqual(UnitStatus.Done, ledger.Units.Single(u => u.Id == unit.Id).Status);
        Assert.Contains("UX", (await new Status(ledger, Enabled).RunAsync(CancellationToken.None)).Render());
    }

    [Fact]
    public async Task UxPackRequiresReadabilityAndActionPlacementAndShowsSiblingScreens()
    {
        tree.Add("web/Invoice.tsx", "export default () => <p />;");
        tree.Add("web/Schedule.tsx", "export default () => <button>Charge</button>;");
        await ScanAsync();
        var pack = (await new Next(ledger, tree, Enabled, kind: UnitKind.Ux).RunAsync(1, CancellationToken.None))[0];
        Assert.Contains("readability", pack.Markdown);
        Assert.Contains("expected_location", pack.Markdown);
        Assert.Contains("web/Schedule.tsx", pack.Markdown);
        Assert.Contains("browser", pack.Markdown);
    }
}
