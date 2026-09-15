using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class DeadCodeReviewTests
{
    [Fact]
    public void HttpEntryPointAndItsCalleesStayReachableWithoutAnyCodeCaller()
    {
        var endpoint = Symbol("endpoint", "public Task Get()");
        var service = Symbol("service", "private Task Load()");
        var map = Map([endpoint, service], [new Edge("endpoint", "service", EdgeKind.Call)], [new EntryPoint("endpoint", "http", "GET /customers")]);

        var result = DeadCodeReview.Analyze(map, Sources());

        Assert.Equal(DeadCodeState.ProtectedEntryPoint, result.Single(x => x.SymbolId == "endpoint").State);
        Assert.Equal(DeadCodeState.Reachable, result.Single(x => x.SymbolId == "service").State);
        Assert.Contains("HTTP", result.Single(x => x.SymbolId == "endpoint").Reason);
    }

    [Theory]
    [InlineData("public class Api\npublic void Run()")]
    [InlineData("internal class Api\nprotected void Run()")]
    [InlineData("internal class Api\nprivate override void Run()")]
    [InlineData("internal class Api\n[Handler] private void Run()")]
    [InlineData("internal class Api\nvoid IHandler.Run()")]
    public void ExposedAndFrameworkMembersAreNeverCandidates(string signature)
    {
        var result = Assert.Single(DeadCodeReview.Analyze(Map([Symbol("a", signature)]), Sources()));

        Assert.NotEqual(DeadCodeState.Candidate, result.State);
    }

    [Theory]
    [InlineData("export function execute()", "function")]
    [InlineData("export default function execute()", "function")]
    [InlineData("class Work\nexecute()", "method")]
    [InlineData("class Work\nconstructor()", "constructor")]
    [InlineData("@Get('/customers') execute()", "method")]
    public void TypeScriptExportsAndFrameworkCallbacksAreProtected(string signature, string kind)
    {
        var symbol = Symbol("a", signature) with { Path = "src/work.ts", Kind = kind };

        var result = Assert.Single(DeadCodeReview.Analyze(Map([symbol]), Sources()));

        Assert.NotEqual(DeadCodeState.Candidate, result.State);
    }

    [Fact]
    public void AnUnreachableInternalCycleIsCandidateEvidenceNotProofOfDeadness()
    {
        var a = Symbol("a", "public class A\nprivate void A()");
        var b = Symbol("b", "internal class B\ninternal void B()");
        var map = Map([a, b], [new Edge("a", "b", EdgeKind.Call), new Edge("b", "a", EdgeKind.Call)]);

        var result = DeadCodeReview.Analyze(map, Sources());

        Assert.All(result, x => Assert.Equal(DeadCodeState.Candidate, x.State));
        Assert.All(result, x => Assert.Contains("not proof", x.Reason));
    }

    [Fact]
    public void AnExportedEntryProtectsBoundAndVirtualCallees()
    {
        var map = Map([Symbol("a", "public void A()"), Symbol("b", "private void B()"), Symbol("c", "private void C()")],
            [new Edge("a", "b", EdgeKind.Bound), new Edge("b", "c", EdgeKind.Overrides)]);

        var result = DeadCodeReview.Analyze(map, Sources());

        Assert.Equal(DeadCodeState.Reachable, result.Single(x => x.SymbolId == "b").State);
        Assert.Equal(DeadCodeState.Reachable, result.Single(x => x.SymbolId == "c").State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncompleteMappingCannotProduceUnusedCandidates(bool unresolved)
    {
        var map = Map([Symbol("a", "private void A()")]) with
        {
            Resolution = new ResolutionStats(1, unresolved ? 1 : 0, unresolved ? ["DynamicCall"] : []),
            Diagnostics = unresolved ? [] : ["mapper failed for generated.ts"]
        };

        var result = Assert.Single(DeadCodeReview.Analyze(map, Sources()));

        Assert.Equal(DeadCodeState.Unknown, result.State);
    }

    [Theory]
    [InlineData("var target = typeof(Worker).GetMethod(name);")]
    [InlineData("var target = Type.GetType(configuredType);")]
    [InlineData("Assembly.LoadFrom(path);")]
    [InlineData("services.AddScoped<IWorker, Worker>();")]
    [InlineData("[assembly: InternalsVisibleTo(\"Other\")]")]
    [InlineData("const plugin = require(name);")]
    [InlineData("const plugin = import(name);")]
    [InlineData("window[handlerName]();")]
    [InlineData("globalThis \n [handlerName]();")]
    [InlineData("Assembly \t . \n Load(name);")]
    [InlineData("eval \n (source);")]
    [InlineData("services.TryAddSingleton<IWorker, Worker>();")]
    public void DynamicInvocationMakesOtherwiseUnreachableCodeUnknown(string source)
    {
        var sources = Sources();
        sources["src/bootstrap.cs"] = source;

        var result = Assert.Single(DeadCodeReview.Analyze(Map([Symbol("a", "private void A()")]), sources));

        Assert.Equal(DeadCodeState.Unknown, result.State);
        Assert.Contains("dynamic", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("const imported = importSource(name);")]
    [InlineData("const plugin = requireValue;")]
    [InlineData("const value = window.location;")]
    [InlineData("const value = globalThis.document;")]
    [InlineData("Assembly.Loaded();")]
    [InlineData("AssemblyLoader.Load(name);")]
    [InlineData("GetMethodName();")]
    [InlineData("otherGetMethod();")]
    [InlineData("GetMethod\u0301();")]
    [InlineData("\u203fGetMethod();")]
    [InlineData("GetMethod\u200c();")]
    [InlineData("\u200dGetMethod();")]
    public void SimilarIdentifiersDoNotCreateDynamicInvocationEvidence(string source)
    {
        var sources = Sources();
        sources["src/bootstrap.cs"] = source;

        var result = Assert.Single(DeadCodeReview.Analyze(Map([Symbol("a", "private void A()")]), sources));

        Assert.Equal(DeadCodeState.Candidate, result.State);
    }

    [Theory]
    [InlineData("private\u200c")]
    [InlineData("internal\u200d")]
    [InlineData("private\u0301")]
    [InlineData("\u203finternal")]
    public void VisibilityKeywordsInsideUnicodeIdentifiersCannotEstablishInternalDeclarations(string identifier)
    {
        var result = Assert.Single(DeadCodeReview.Analyze(Map([Symbol("a", identifier + " void A()")]), Sources()));

        Assert.Equal(DeadCodeState.Unknown, result.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdversarialRequestSourceDoesNotInterruptAnalysisOrLoseKnownEntryProtection(bool incompleteMap)
    {
        var sources = Sources();
        sources["web/Invoice.tsx"] = "const label = " + string.Concat(Enumerable.Repeat("a.", 50_000)) + ";";
        var map = Map([Symbol("endpoint", "public void Endpoint()"), Symbol("helper", "private void Helper()")],
            entryPoints: [new EntryPoint("endpoint", "http", "GET /invoices")]);
        if (incompleteMap) map = map with { Diagnostics = ["Mapping is incomplete."] };

        var result = DeadCodeReview.Analyze(map, sources);

        Assert.Equal(DeadCodeState.ProtectedEntryPoint, result.Single(item => item.SymbolId == "endpoint").State);
        var helper = result.Single(item => item.SymbolId == "helper");
        Assert.Equal(incompleteMap ? DeadCodeState.Unknown : DeadCodeState.Candidate, helper.State);
        Assert.All(result, item => Assert.Empty(item.UsageEvidence));
    }

    [Theory]
    [InlineData("fetch('/customers')", "GET /customers")]
    [InlineData("axios.get('/customers/123')", "GET /customers/{id}")]
    [InlineData("api.post('/customers/123/charge', body)", "POST /customers/{id}/charge")]
    [InlineData("fetch('/customers', { method: 'POST' })", "POST /customers")]
    [InlineData("fetch('/customers', { METHOD: 'post' })", "POST /customers")]
    [InlineData("services.customers.delete('/customers/123')", "DELETE /customers/{id}")]
    public void LiteralFrontendRequestsArePositiveEndpointUsageEvidence(string request, string route)
    {
        var endpoint = Symbol("endpoint", "public void Endpoint()");
        var sources = Sources();
        sources["web/Invoice.tsx"] = "export function Invoice() {\n  return " + request + ";\n}";
        var map = Map([endpoint], entryPoints: [new EntryPoint("endpoint", "http", route)]);

        var result = Assert.Single(DeadCodeReview.Analyze(map, sources));

        Assert.Equal(DeadCodeState.ProtectedEntryPoint, result.State);
        Assert.Contains(result.UsageEvidence, evidence => evidence.Contains("web/Invoice.tsx:2", StringComparison.Ordinal));
        Assert.Contains("literal", DeadCodeReview.RenderEvidence(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TextMentionsAndDifferentVerbsDoNotPretendToBeObservedRequests()
    {
        var sources = Sources();
        sources["web/Invoice.tsx"] = "const label = '/customers';\naxios.post('/customers', body);";
        var map = Map([Symbol("endpoint", "public void Endpoint()")], entryPoints: [new EntryPoint("endpoint", "http", "GET /customers")]);

        var result = Assert.Single(DeadCodeReview.Analyze(map, sources));

        Assert.Empty(result.UsageEvidence);
        Assert.Equal(DeadCodeState.ProtectedEntryPoint, result.State);
    }

    [Fact]
    public void UnknownLanguagesNeverBecomeCandidatesFromAnEmptyCallGraph()
    {
        var symbol = Symbol("a", "private def A()") with { Path = "src/work.py" };

        Assert.Equal(DeadCodeState.Unknown, Assert.Single(DeadCodeReview.Analyze(Map([symbol]), Sources())).State);
    }

    [Theory]
    [InlineData("dead_code", "dead_code", false)]
    [InlineData("default", "dead_code", false)]
    [InlineData("dead_code", "unused", false)]
    [InlineData("default", "security", true)]
    public void DeadCodeCannotEnterAutomaticRepairsEvenWhenConfirmedOrMislabeled(string lens, string category, bool expected)
    {
        var finding = new Finding("src/A.cs", 1, 1, Severity.Low, category, "Claim", "Evidence", 1, lens);

        Assert.Equal(expected, DeadCodeReview.CanAutoFix(finding));
        Assert.Equal(!expected, DeadCodeReview.IsFinding(finding));
    }

    [Fact]
    public void InstructionsRequireCrossApplicationEvidenceAndKeepOrphansDistinctFromDeadCode()
    {
        Assert.Contains("HTTP", DeadCodeReview.Instructions);
        Assert.Contains("orphan", DeadCodeReview.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reflection", DeadCodeReview.Instructions);
        Assert.Contains("report-only", DeadCodeReview.Instructions);
    }

    private static Symbol Symbol(string id, string signature) => new(id, "src/A.cs", new LineRange(1, 3), "method", signature, "hash");

    private static Dictionary<string, string> Sources() => new(StringComparer.Ordinal) { ["src/A.cs"] = "class A { }" };

    private static CodeMap Map(IReadOnlyList<Symbol> symbols, IReadOnlyList<Edge>? edges = null, IReadOnlyList<EntryPoint>? entryPoints = null) =>
        new(symbols, edges ?? [], entryPoints ?? [], new ResolutionStats(0, 0, []), []);
}
