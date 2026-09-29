using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class TestClassificationTests
{
    private const string Root = "/repos/tests-repo";
    private const string TestProject = """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3" /></ItemGroup></Project>""";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new FakeSourceTree()
        .Add("src/App/App.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"></Project>""")
        .Add("src/App/Quote.cs", "class Quote { }")
        .Add("src/Checks/Checks.csproj", TestProject)
        .Add("src/Checks/QuoteChecks.cs", "class QuoteChecks { }")
        .Add("web/page.tsx", "export default function Page() { return null; }")
        .Add("web/page.test.tsx", "test('renders', () => {});");

    private Task<ScanResult> ScanAsync(Config config, IReadOnlyList<ICodeMapper>? mappers = null) =>
        new Scan(ledger, tree, new FakeContentHasher(), new FakeClock(), config, mappers ?? [], Root).RunAsync(CancellationToken.None);

    private string? Reason(string path) => ledger.Files[path].ExcludedReason;

    [Fact]
    public async Task TestFiles_ByNameOrByTestProject_AreExcludedWithReasonTest_AndGetNoUnit()
    {
        await ScanAsync(Config.Default);

        Assert.Equal("test", Reason("web/page.test.tsx"));
        Assert.Equal("test", Reason("src/Checks/QuoteChecks.cs"));
        Assert.Null(Reason("src/App/Quote.cs"));
        Assert.Null(Reason("web/page.tsx"));
        Assert.Equal(["file:src/App/Quote.cs", "file:web/page.tsx"], ledger.Units.Select(u => u.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReviewTests_ReviewsThemAsOrdinaryFiles()
    {
        await ScanAsync(Config.Default with { ReviewTests = true });

        Assert.Null(Reason("web/page.test.tsx"));
        Assert.Null(Reason("src/Checks/QuoteChecks.cs"));
        Assert.Equal(4, ledger.Units.Count);
    }

    [Fact]
    public async Task TheRepositorysOwnExcludeGlob_TakesPrecedence()
    {
        await ScanAsync(Config.Default with { Exclude = ["web/**"] });

        Assert.Equal("exclude:web/**", Reason("web/page.test.tsx"));
        Assert.Equal("test", Reason("src/Checks/QuoteChecks.cs"));
    }

    [Fact]
    public async Task TestFiles_AreStillMapped_SoReferencesFromTestsReachTheMap()
    {
        var csharp = new FakeCodeMapper(Languages.CSharp, new CodeMap(
            [new Symbol("M:Quote.Total", "src/App/Quote.cs", new LineRange(1, 1), "method", "int Total()", "h1")], [], [], new ResolutionStats(0, 0, []), []));

        await ScanAsync(Config.Default, [csharp]);

        var paths = Assert.Single(csharp.Calls).Paths;
        Assert.Contains("src/Checks/QuoteChecks.cs", paths);
        Assert.Contains("src/App/Quote.cs", paths);
        Assert.DoesNotContain(ledger.Units, u => u.Key.Contains("QuoteChecks", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigJson_ReadsReviewTests()
    {
        Assert.True(ConfigJson.Parse("""{ "lenses": [], "review_tests": true }""").ReviewTests);
        Assert.False(ConfigJson.Parse("""{ "lenses": [] }""").ReviewTests);
    }

    [Fact]
    public async Task Status_SaysHowManyExcludedFilesAreTests()
    {
        await ScanAsync(Config.Default);

        var status = await new Status(ledger, Config.Default).RunAsync(CancellationToken.None);

        Assert.Contains("excluded 4 (2 test files)", status.Render().Split('\n'));
    }
}
