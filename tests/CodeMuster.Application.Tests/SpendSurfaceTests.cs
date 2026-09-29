using CodeMuster.Application.Tests.Fakes;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class SpendSurfaceTests
{
    private const string At = "2026-09-10T12:00:00.0000000Z";
    private readonly FakeLedger ledger = new();

    private void Call(string run, UnitKind? kind, string? model, long input, long output, decimal? cost, string? source, bool succeeded = true, string agent = "claude") =>
        ledger.Calls.Add(new AgentCall(At, run, kind is null ? null : kind + ":x", kind, new AgentIdentity(agent),
            input + output == 0 ? AgentUsage.Unknown : new AgentUsage(input, output, 100, 10, model, null), succeeded, cost, source));

    private void Seed()
    {
        Call("run 2026-09-10T11:00:00.0000000Z", UnitKind.File, "claude-opus-5-5", 1000, 200, 0.36m, "harness");
        Call("run 2026-09-10T11:00:00.0000000Z", UnitKind.Slice, "claude-opus-5-5", 3000, 400, 1.04m, "harness", succeeded: false);
        Call("verify 2026-09-10T11:30:00.0000000Z", UnitKind.Verify, "gpt-5.5", 500, 50, 0.004m, "table 2026-09-27", agent: "codex");
        Call("verify 2026-09-10T11:30:00.0000000Z", UnitKind.Verify, "mystery-1", 10, 5, null, null, agent: "opencode");
        Call("fix 2026-09-10T12:00:00.0000000Z", UnitKind.Fix, null, 0, 0, null, null, succeeded: false, agent: "gemini");
    }

    [Fact]
    public async Task Status_AddsOneSpendLine_CountingUnpricedCallsSeparately()
    {
        Seed();

        var status = await new Status(ledger, Config.Default).RunAsync(CancellationToken.None);

        Assert.Equal("spend $1.40 API-equivalent across 5 calls; 2 calls unpriced", status.SpendLine);
        Assert.Contains(status.SpendLine, status.Render().Split('\n'));
    }

    [Fact]
    public async Task Status_WhenNoCallIsPriced_SaysUnpricedRatherThanZero()
    {
        Call("run r", UnitKind.File, "mystery-1", 10, 5, null, null);

        var status = await new Status(ledger, Config.Default).RunAsync(CancellationToken.None);

        Assert.Equal("spend unpriced across 1 call; none had a price when recorded", status.SpendLine);
    }

    [Fact]
    public async Task Status_WithoutCalls_PrintsNoSpendLine()
    {
        var status = await new Status(ledger, Config.Default).RunAsync(CancellationToken.None);

        Assert.DoesNotContain("spend", status.Render());
    }

    [Fact]
    public async Task Report_SpendSection_ShowsTotalsTokensAndCostByModelKindAndRun_AndWhyCallsAreUnpriced()
    {
        Seed();

        var report = await new Report(ledger, Config.Default).RunAsync(CancellationToken.None);

        var section = report[report.IndexOf("## Spend", StringComparison.Ordinal)..report.IndexOf("## Units", StringComparison.Ordinal)];
        Assert.Equal($"""
            ## Spend

            $1.40 API-equivalent across 5 agent call(s), 2 of them failed or rejected; 2 unpriced.
            API-equivalent is what the calls cost at the provider's API prices, whether or not the harness ran on a subscription. Each cost was fixed when its call was recorded, so a later price change does not alter it.

            tokens: 4510 input, 655 output, 400 cache read, 40 cache write

            | model | calls | input | output | cache read | cache write | cost |
            |---|---|---|---|---|---|---|
            | claude-opus-5-5 | 2 | 4000 | 600 | 200 | 20 | $1.40 |
            | gemini default | 1 | 0 | 0 | 0 | 0 | unpriced |
            | gpt-5.5 | 1 | 500 | 50 | 100 | 10 | $0.0040 |
            | mystery-1 | 1 | 10 | 5 | 100 | 10 | unpriced |

            | unit kind | calls | cost |
            |---|---|---|
            | file | 1 | $0.36 |
            | fix | 1 | unpriced |
            | slice | 1 | $1.04 |
            | verify | 2 | $0.0040 + 1 unpriced |

            | run | calls | cost |
            |---|---|---|
            | run 2026-09-10T11:00:00.0000000Z | 2 | $1.40 |
            | verify 2026-09-10T11:30:00.0000000Z | 2 | $0.0040 + 1 unpriced |
            | fix 2026-09-10T12:00:00.0000000Z | 1 | unpriced |

            prices: bundled table checked {PriceTable.Default.Checked}; priced calls by source: harness 2, table 2026-09-27 1
            unpriced: 2 call(s); 1 reported no usage; 1 used a model that had no price when recorded (mystery-1); add it under "prices" in .codemuster/config.json to price later calls


            """.Replace("\r\n", "\n"), section);
    }

    [Fact]
    public async Task Report_WithoutCalls_HasNoSpendSection()
    {
        Assert.DoesNotContain("## Spend", await new Report(ledger, Config.Default).RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Estimate_PricesPendingInputPerRepresentativeModel_WithTheDefaultOutputAssumption()
    {
        AddPendingFile("src/a.cs", 1_197_200);

        var report = await new Estimate(ledger, Config.Default).RunAsync(CancellationToken.None);

        var models = PriceTable.Default.Bundled.Where(p => p.Estimate).OrderBy(p => p.Model, StringComparer.Ordinal).ToList();
        Assert.Equal(300_000, report.TotalTokens);
        Assert.Equal(30_000, report.OutputTokens);
        Assert.Equal(models.Select(p => p.Model), report.Costs.Select(c => c.Model));
        var lines = report.Render().Split('\n');
        Assert.Equal("file 1 units ~300000 tokens", lines[0]);
        Assert.Equal("total ~300000 tokens", lines[1]);
        Assert.Equal("API-equivalent cost of ~300000 input and ~30000 output tokens (output assumed 10% of input, the default until 20 calls with usage are recorded; harness prompts and tool calls not counted):", lines[2]);
        Assert.Contains("claude-opus-5-5 ~$1.80", lines);
        Assert.Equal(3 + models.Count, lines.Length);
    }

    [Fact]
    public async Task Estimate_MeasuresTheOutputRatioFromHistory_PricesTheModelsUsed_AndShowsTheRecordedAverage()
    {
        AddPendingFile("src/a.cs", 1_197_200);
        for (var i = 0; i < 20; i++) Call("run r", UnitKind.File, "claude-sonnet-4-6", 1000, 100, 0.5m, "harness");
        var config = Config.Default with { Prices = [new ModelPrice("in-house-7", 1, 2)] };

        var report = await new Estimate(ledger, config).RunAsync(CancellationToken.None);

        var lines = report.Render().Split('\n');
        Assert.Equal("API-equivalent cost of ~300000 input and ~27027 output tokens (output assumed 9% of input, measured from 20 recorded calls; harness prompts and tool calls not counted):", lines[2]);
        Assert.Equal(["claude-sonnet-4-6 ~$1.31", "in-house-7 ~$0.35"], lines[3..5]);
        Assert.Equal(
        [
            "recorded cost per call with claude, harness overhead included:",
            "file 1 call(s) at $0.50 each, the mean of 20 file calls ~$0.50",
            "total 1 call(s) ~$0.50 at recorded rates",
        ], lines[5..]);
    }

    [Fact]
    public async Task Estimate_WithNothingPending_KeepsItsExistingOutput()
    {
        Assert.Equal("total ~0 tokens", (await new Estimate(ledger, Config.Default).RunAsync(CancellationToken.None)).Render());
    }

    private void AddPendingFile(string path, long size)
    {
        ledger.Files[path] = new FileRecord(path, Languages.FromPath(path), "hash", size, At, At, At, null, null, null, null, null, null);
        ledger.Units.Add(new Unit(UnitIds.File(path), UnitKind.File, path, "fp", UnitStatus.Pending, Fidelity.Full, null, null, null));
        ledger.Members.Add(new UnitMember(UnitIds.File(path), path, null, "h", 0));
    }
}
