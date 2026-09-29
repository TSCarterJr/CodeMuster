using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;
using static CodeMuster.Application.Tests.Fakes.MixedRepo;

namespace CodeMuster.Application.Tests;

public class ArchitectureTests
{
    private const string Root = "/repos/mixed-repo";
    private const string Settings = "site/app/settings/page.tsx";
    private const string Sidebar = "site/components/Sidebar.jsx";
    private const string Billing = "src/Billing/BillingController.cs";

    private readonly FakeLedger ledger = new();
    private readonly FakeSourceTree tree = new();
    private readonly FakeClock clock = new();
    private readonly FakeCodeMapper csharp = new(Languages.CSharp, CSharp());
    private readonly FakeCodeMapper typescript = new(Languages.TypeScript, TypeScript());

    public ArchitectureTests() => AddTo(tree);

    // Tim's example: a payment setting sits under General while a Payments section exists.
    private static readonly IReadOnlyList<UiElement> SettingsUi =
    [
        new("route", "/settings", Settings, 3, Route: "/settings"),
        new("heading", "Settings", Settings, 6, Route: "/settings"),
        new("heading", "General", Settings, 8, Section: "Settings", Route: "/settings"),
        new("control", "Company name", Settings, 10, Control: "text", Section: "General", Route: "/settings"),
        new("control", "Auto charge customer", Settings, 11, Control: "Switch", Section: "General", Route: "/settings"),
        new("heading", "Payments", Settings, 14, Section: "Settings", Route: "/settings"),
        new("control", "Default currency", Settings, 17, Control: "select", Section: "Payments", Route: "/settings"),
        new("nav", "Invoices", Sidebar, 4, Target: "/invoices"),
        new("nav", "Settings", Sidebar, 5, Target: "/settings"),
    ];

    private static FileRecord Included(string path, string hash = "") => new(path, Languages.FromPath(path), "content:" + path + hash, 1, "", "", "", null, null, null, null, null, null);

    private static CodeMap UiMap(IReadOnlyList<UiElement> elements) => new CodeMap([], [], [], new ResolutionStats(0, 0, []), []) { UiElements = elements };

    private static CodeMap ApiMap() => new(
        [
            new Symbol("M:Billing.BillingController.List", Billing, new LineRange(10, 14), "method", "[Authorize] public sealed class BillingController : ControllerBase\n[HttpGet(\"invoices\")] public IReadOnlyList<Invoice> List(int page)", "b1"),
            new Symbol("M:Billing.BillingController.Refund(System.Int32)", Billing, new LineRange(16, 22), "method", "[Authorize] public sealed class BillingController : ControllerBase\n[AllowAnonymous] [HttpPost(\"invoices/{id}/refund\")] public IActionResult Refund(int id)", "b2"),
            new Symbol("M:Billing.CustomersController.Get(System.Int32)", "src/Billing/CustomersController.cs", new LineRange(8, 12), "method", "public sealed class CustomersController : ControllerBase\n[HttpGet(\"customer/{id}\")] public Customer Get(int id)", "b3"),
            new Symbol("M:Billing.Helper.Unrouted", "src/Billing/Helper.cs", new LineRange(1, 3), "method", "static class Helper\nstatic void Unrouted()", "b4"),
        ],
        [],
        [
            new EntryPoint("M:Billing.BillingController.List", "http", "GET /invoices"),
            new EntryPoint("M:Billing.BillingController.Refund(System.Int32)", "http", "POST /invoices/{id}/refund"),
            new EntryPoint("M:Billing.CustomersController.Get(System.Int32)", "http", "GET /customer/{id}"),
            new EntryPoint("M:Billing.Helper.Unrouted", "background", "Helper"),
        ],
        new ResolutionStats(0, 0, []),
        []);

    private Task<ScanResult> ScanAsync(Config? config = null) =>
        new Scan(ledger, tree, new FakeContentHasher(), clock, config ?? Config.Default, [csharp, typescript], Root).RunAsync(CancellationToken.None);

    [Fact]
    public void UiUnit_HoldsTheFilesThatDefineElements_AndRendersTheStructureAsATreeByRouteAndSection()
    {
        var unit = Assert.Single(ArchitectureReview.PlanUi(UiMap(SettingsUi), [Included(Settings), Included(Sidebar)]));
        var tree = ArchitectureReview.RenderUi(SettingsUi);

        Assert.Equal((UnitIds.ArchitectureUi, UnitKind.Architecture, "UI structure"), (unit.Id, unit.Kind, unit.Key));
        Assert.Equal([Settings, Sidebar], unit.Members.Select(m => m.Path));
        Assert.All(unit.Members, m => Assert.Null(m.Symbol));
        Assert.Equal(
            [
                "### /settings",
                "",
                $"- route /settings ({Settings}:3)",
                $"- heading \"Settings\" ({Settings}:6)",
                $"  - heading \"General\" ({Settings}:8)",
                $"    - control text \"Company name\" ({Settings}:10)",
                $"    - control Switch \"Auto charge customer\" ({Settings}:11)",
                $"  - heading \"Payments\" ({Settings}:14)",
                $"    - control select \"Default currency\" ({Settings}:17)",
                "",
                $"### {Sidebar} (no route)",
                "",
                $"- nav \"Invoices\" -> /invoices ({Sidebar}:4)",
                $"- nav \"Settings\" -> /settings ({Sidebar}:5)",
            ],
            tree);
    }

    [Fact]
    public void UiUnit_IsPlannedOnlyForElementsInIncludedFiles()
    {
        Assert.Empty(ArchitectureReview.PlanUi(UiMap([]), [Included(Settings)]));
        Assert.Empty(ArchitectureReview.PlanUi(UiMap(SettingsUi), [Included("other.tsx")]));
        Assert.Equal([Sidebar], Assert.Single(ArchitectureReview.PlanUi(UiMap(SettingsUi), [Included(Sidebar)])).Members.Select(m => m.Path));
    }

    [Fact]
    public void UiFingerprint_ChangesWithTheStructureOrTheFiles_AndNotWithAnythingElse()
    {
        string Fingerprint(IReadOnlyList<UiElement> elements, string hash = "", string unrelated = "") =>
            Fingerprints.Compute(Assert.Single(ArchitectureReview.PlanUi(UiMap(elements), [Included(Settings, hash), Included(Sidebar), Included("src/Other.cs", unrelated)])).Members);

        var original = Fingerprint(SettingsUi);

        Assert.Equal(original, Fingerprint(SettingsUi.Reverse().ToList()));
        Assert.Equal(original, Fingerprint(SettingsUi, unrelated: "edited"));
        Assert.NotEqual(original, Fingerprint(SettingsUi, hash: "edited"));
        Assert.NotEqual(original, Fingerprint([.. SettingsUi.Select(e => e.Text == "Auto charge customer" ? e with { Section = "Payments" } : e)]));
    }

    [Fact]
    public void ApiUnit_ListsEveryHttpEndpoint_GroupedByRoutePrefix_WithHandlerSignatureAndAttributes()
    {
        var map = ApiMap();
        var unit = Assert.Single(ArchitectureReview.PlanApi(map, [Included(Billing), Included("src/Billing/CustomersController.cs"), Included("src/Billing/Helper.cs")]));
        var list = ArchitectureReview.RenderApi(map, [Billing, "src/Billing/CustomersController.cs"]);

        Assert.Equal((UnitIds.ArchitectureApi, UnitKind.Api, "API endpoints"), (unit.Id, unit.Kind, unit.Key));
        Assert.Equal([Billing, "src/Billing/CustomersController.cs"], unit.Members.Select(m => m.Path));
        Assert.Equal(
            [
                "### /customer",
                "",
                "- GET /customer/{id}: CustomersController.Get (src/Billing/CustomersController.cs:8)",
                "  - `public sealed class CustomersController : ControllerBase`",
                "  - `[HttpGet(\"customer/{id}\")] public Customer Get(int id)`",
                "",
                "### /invoices",
                "",
                $"- GET /invoices: BillingController.List ({Billing}:10)",
                "  - `[Authorize] public sealed class BillingController : ControllerBase`",
                "  - `[HttpGet(\"invoices\")] public IReadOnlyList<Invoice> List(int page)`",
                $"- POST /invoices/{{id}}/refund: BillingController.Refund ({Billing}:16)",
                "  - `[Authorize] public sealed class BillingController : ControllerBase`",
                "  - `[AllowAnonymous] [HttpPost(\"invoices/{id}/refund\")] public IActionResult Refund(int id)`",
            ],
            list);
    }

    [Fact]
    public void ApiUnit_NeedsAnHttpEntryPointInAnIncludedFile_AndItsFingerprintFollowsTheEndpointList()
    {
        var map = ApiMap();
        FileRecord[] files = [Included(Billing), Included("src/Billing/CustomersController.cs"), Included("src/Billing/Helper.cs")];
        string Fingerprint(CodeMap m) => Fingerprints.Compute(Assert.Single(ArchitectureReview.PlanApi(m, files)).Members);

        Assert.Empty(ArchitectureReview.PlanApi(map with { EntryPoints = map.EntryPoints.Where(e => e.Kind != "http").ToList() }, files));
        Assert.Empty(ArchitectureReview.PlanApi(map, [Included("src/Billing/Helper.cs")]));
        Assert.Equal(Fingerprint(map), Fingerprint(map with { Symbols = [.. map.Symbols.Reverse()], EntryPoints = [.. map.EntryPoints.Reverse()] }));
        Assert.NotEqual(Fingerprint(map), Fingerprint(map with { EntryPoints = map.EntryPoints.Select(e => e.Display == "GET /customer/{id}" ? e with { Display = "GET /customers/{id}" } : e).ToList() }));
    }

    [Fact]
    public async Task Scan_PlansTheUiAndApiUnits_UnlessArchitectureReviewIsOff()
    {
        typescript.Map = TypeScript() with { UiElements = [new UiElement("route", "/quotes", QuotesPagePath, 4, Route: "/quotes"), new UiElement("heading", "Quotes", QuotesPagePath, 8, Route: "/quotes")] };

        await ScanAsync(Config.Default with { ArchitectureReview = false });
        Assert.DoesNotContain(ledger.Units, u => u.Kind is UnitKind.Architecture or UnitKind.Api);

        await ScanAsync();
        Assert.Equal([UnitIds.ArchitectureApi, UnitIds.ArchitectureUi], ledger.Units.Where(u => u.Kind is UnitKind.Architecture or UnitKind.Api).Select(u => u.Id).Order(StringComparer.Ordinal));
        Assert.Equal([ControllerPath], ledger.Members.Where(m => m.UnitId == UnitIds.ArchitectureApi).Select(m => m.Path));
        Assert.Equal([QuotesPagePath], ledger.Members.Where(m => m.UnitId == UnitIds.ArchitectureUi).Select(m => m.Path));

        var fingerprints = ledger.Units.Where(u => u.Kind is UnitKind.Architecture or UnitKind.Api).Select(u => u.Fingerprint).ToList();
        await ScanAsync();
        Assert.Equal(fingerprints, ledger.Units.Where(u => u.Kind is UnitKind.Architecture or UnitKind.Api).Select(u => u.Fingerprint));
    }

    [Fact]
    public async Task UiPack_ShowsTheTree_AsksTheCrossScreenQuestions_AndListsTheFiles()
    {
        csharp.Map = CSharp() with { EntryPoints = [] };
        typescript.Map = TypeScript() with { UiElements = SettingsUi };
        tree.Add(Settings, "export default function Settings() {}\n");
        tree.Add(Sidebar, "export function Sidebar() {}\n");
        await ScanAsync();

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Architecture).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains("- kind: architecture\n", pack);
        Assert.Contains("- lenses: architecture\n", pack);
        Assert.Contains("a payment setting under General while a Payments section exists", pack);
        Assert.Contains("orphan pages", pack);
        Assert.Contains($"    - control Switch \"Auto charge customer\" ({Settings}:11)", pack);
        Assert.Contains($"### {Settings} (typescript)\n", pack);
        Assert.Contains($"### {Sidebar} (javascript)\n", pack);
        Assert.DoesNotContain("export default function Settings", pack);
    }

    [Fact]
    public async Task ApiPack_ListsTheEndpoints_AndAsksAboutConsistencyAndAuthorization()
    {
        await ScanAsync();

        var pack = Assert.Single(await new Next(ledger, tree, Config.Default, kind: UnitKind.Api).RunAsync(1, CancellationToken.None)).Markdown;

        Assert.Contains("- kind: api\n", pack);
        Assert.Contains("- lenses: api\n", pack);
        Assert.Contains("missing authorization", pack);
        Assert.Contains($"- GET /quotes: QuotesController.ListQuotes ({ControllerPath}:9)", pack);
        Assert.Contains($"- GET /quotes/{{id}}: QuotesController.GetQuote ({ControllerPath}:15)", pack);
        Assert.Contains($"### {ControllerPath} (csharp)\n", pack);
    }

    [Fact]
    public async Task RunWithAnAgent_RecordsBothCalls_AndTheirEvents_UnderTheirKinds()
    {
        typescript.Map = TypeScript() with { UiElements = SettingsUi };
        tree.Add(Settings, "export default function Settings() {}\n");
        tree.Add(Sidebar, "export function Sidebar() {}\n");
        await ScanAsync();
        var events = new RecordingEvents();
        var adapter = new FakeAgentAdapter((pack, _) => Task.FromResult(pack.Contains("- kind: architecture\n", StringComparison.Ordinal)
            ? AnalysisResponseJson.Serialize(new AnalysisResponse("Settings", [new Finding(Settings, 11, 11, Severity.Medium, "architecture", "Auto charge customer is a payment setting under General.", "A Payments section exists at line 14.", 0.8, "architecture")]))
            : """{ "summary": "Consistent.", "findings": [] }"""))
        { Usage = new AgentUsage(10, 2, 0, 0, "fake", null) };

        var result = await new Run(ledger, tree, clock, Config.Default with { BatchUnits = 1, Verify = false }, adapter, new Progress<RunProgress>(), null, events)
            .RunAsync(new RunOptions(1, 1, false, UnitKind.Architecture), CancellationToken.None);
        await new Run(ledger, tree, clock, Config.Default with { BatchUnits = 1, Verify = false }, adapter, new Progress<RunProgress>(), null, events)
            .RunAsync(new RunOptions(1, 1, false, UnitKind.Api), CancellationToken.None);

        Assert.Equal(1, result.Completed);
        Assert.Equal([UnitKind.Architecture, UnitKind.Api], ledger.Calls.Select(c => c.Kind));
        Assert.Equal(["architecture", "api"], events.OfType("unit_finished").Select(e => e.Fields["kind"]));
        Assert.Equal(Settings, Assert.Single(await ledger.GetCurrentFindingsAsync(CancellationToken.None)).Finding.Path);
    }
}
