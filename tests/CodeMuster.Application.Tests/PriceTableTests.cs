using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application.Tests;

public class PriceTableTests
{
    private static readonly AgentIdentity Claude = new("claude");

    private static PriceTable Table => PriceTable.Parse("""
        {
          "checked": "2026-09-27",
          "prices": [
            {"model": "claude-opus-5-5", "provider": "anthropic", "input": 4, "output": 20, "cache_read": 0.2, "cache_write": 5, "source": "https://example.test/pricing"},
            {"model": "claude-opus-5", "provider": "anthropic", "input": 5, "output": 25, "cache_read": 0.5, "cache_write": 6.25, "source": "https://example.test/pricing"},
            {"model": "gpt-5", "provider": "openai", "input": 1.25, "output": 10, "cache_read": 0.125, "source": "https://example.test/pricing"}
          ]
        }
        """);

    [Fact]
    public void BundledTable_IsDated_AndHasTheAnthropicRatesD63Names()
    {
        var table = PriceTable.Default;

        Assert.Equal("2026-09-27", table.Checked);
        Assert.All(table.Bundled, price => Assert.StartsWith("https://", price.Source));
        Assert.Equal((10m, 50m), Rates("claude-fable-5-1"));
        Assert.Equal((10m, 50m), Rates("claude-fable-5"));
        Assert.Equal((4m, 20m), Rates("claude-opus-5-5"));
        Assert.Equal(0.2m, table.Find("claude-opus-5-5")!.Value.Price.CacheRead);
        foreach (var model in new[] { "claude-opus-5", "claude-opus-4-8", "claude-opus-4-7", "claude-opus-4-6" }) Assert.Equal((5m, 25m), Rates(model));
        Assert.Equal((2m, 10m), Rates("claude-sonnet-5"));
        Assert.Equal((3m, 15m), Rates("claude-sonnet-4-6"));
        Assert.Equal((1m, 5m), Rates("claude-haiku-4-5"));

        static (decimal, decimal) Rates(string model) =>
            PriceTable.Default.Find(model) is { } found ? (found.Price.Input, found.Price.Output) : throw new InvalidOperationException(model + " is not in the table");
    }

    [Fact]
    public void ExactId_ComputesEveryTokenKindAtItsOwnRate_WithTheTableAsSource()
    {
        var (cost, source) = Table.Cost(Claude, new AgentUsage(1_000_000, 100_000, 2_000_000, 400_000, "claude-opus-5-5", null));

        Assert.Equal(4m + 2m + 0.4m + 2m, cost);
        Assert.Equal("table 2026-09-27", source);
    }

    [Fact]
    public void BundledTable_ReproducesTheRealClaudeCallCost_WithItsOneHourCacheWritesAtTwiceInput()
    {
        // The recorded claude-real.json call: Claude Code's 45K-token harness prompt written to the one-hour cache; the harness reported $0.361592.
        var usage = new AgentUsage(2, 4, 0, 45188, "claude-opus-5-5", null) { CacheWrite1hTokens = 45188 };

        Assert.Equal((0.361592m, "table " + PriceTable.Default.Checked), PriceTable.Default.Cost(Claude, usage));
        Assert.Equal(0.361592m - 45188 * 3m / 1_000_000m, PriceTable.Default.Cost(Claude, usage with { CacheWrite1hTokens = null }).CostUsd);
    }

    [Theory]
    [InlineData("claude-opus-5-5-20260901", 4)]
    [InlineData("claude-opus-5-5[1m]", 4)]
    [InlineData("anthropic/claude-opus-5-5", 4)]
    [InlineData("Claude-Opus-5", 5)]
    [InlineData("gpt-5-2025-08-07", 1.25)]
    public void DatedTaggedPrefixedOrCasedIds_TakeTheLongestKnownId(string model, decimal input) =>
        Assert.Equal(input, Table.Find(model)!.Value.Price.Input);

    [Theory]
    [InlineData("gpt-5-mini")]
    [InlineData("claude-opus-5-9")]
    [InlineData("opus")]
    [InlineData("")]
    public void AnotherModelSharingAPrefix_OrAnAlias_IsNotPriced(string model) => Assert.Null(Table.Find(model));

    [Fact]
    public void RequestedModel_PricesTheCall_WhenTheHarnessDidNotSayWhichAnswered()
    {
        var (cost, _) = Table.Cost(new AgentIdentity("codex", "gpt-5"), new AgentUsage(1_000_000, 0, null, null, null, null));

        Assert.Equal(1.25m, cost);
    }

    [Fact]
    public void MissingCacheWriteRate_IsChargedAtTheInputRate()
    {
        var (cost, _) = Table.Cost(Claude, new AgentUsage(0, 0, 0, 1_000_000, "gpt-5", null));

        Assert.Equal(1.25m, cost);
    }

    [Fact]
    public void HarnessReportedCost_WinsOverTheTable()
    {
        Assert.Equal((0.42m, "harness"), Table.Cost(Claude, new AgentUsage(1_000_000, 0, 0, 0, "claude-opus-5-5", 0.42m)));
        Assert.Equal((0.42m, "harness"), Table.Cost(Claude, new AgentUsage(null, null, null, null, "unknown-model", 0.42m)));
    }

    [Fact]
    public void UnknownModelOrUnknownUsage_IsUnpriced_NeverZero()
    {
        Assert.Equal((null, null), Table.Cost(Claude, new AgentUsage(1000, 10, 0, 0, "mystery-1", null)));
        Assert.Equal((null, null), Table.Cost(new AgentIdentity("claude", "claude-opus-5-5"), AgentUsage.Unknown));
    }

    [Fact]
    public void ConfigOverride_ReplacesAndExtendsTheTable_WithConfigAsSource()
    {
        var table = Table.With([new ModelPrice("claude-opus-5-5", 1, 2, 0.1m, 1.25m), new ModelPrice("in-house-7", 0.5m, 1)]);

        Assert.Equal((1m + 2m, "config"), table.Cost(Claude, new AgentUsage(1_000_000, 1_000_000, 0, 0, "claude-opus-5-5-20260901", null)));
        Assert.Equal((0.5m, "config"), table.Cost(Claude, new AgentUsage(1_000_000, 0, 0, 0, "in-house-7", null)));
        Assert.Equal("table 2026-09-27", table.Find("claude-opus-5")!.Value.Source);
    }

    [Fact]
    public void ConfigPrices_AreReadFromConfigJson_AndLeftOutOfTheInitDefault()
    {
        var config = ConfigJson.Parse("""
            {"lenses": [{"id": "default", "instructions": "x", "globs": [], "languages": []}],
             "prices": [{"model": "in-house-7", "input": 1.0, "output": 5.0, "cache_read": 0.1, "cache_write": 1.25}]}
            """);

        Assert.Equal([new ModelPrice("in-house-7", 1.0m, 5.0m, 0.1m, 1.25m)], config.Prices);
        Assert.Equal(0.5m, PriceTable.For(config).Cost(Claude, new AgentUsage(500_000, 0, 0, 0, "in-house-7", null)).CostUsd);
        Assert.DoesNotContain("prices", ConfigJson.Serialize(Config.Default), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""[{"model": "", "input": 1, "output": 1}]""")]
    [InlineData("""[{"model": "m", "input": -1, "output": 1}]""")]
    [InlineData("""[{"model": "m", "input": 1, "output": 1, "cache_read": -0.1}]""")]
    [InlineData("""[{"model": "m", "output": 1}]""")]
    public void InvalidConfigPrices_AreRejected(string prices) =>
        Assert.ThrowsAny<JsonException>(() => ConfigJson.Parse($$"""{"lenses": [], "prices": {{prices}}}"""));
}
