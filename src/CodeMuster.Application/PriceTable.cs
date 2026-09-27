using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>The bundled, dated price table with the repository's <c>prices</c> overrides merged over it (D63). A call's cost is computed from it when the call is recorded and never recomputed.</summary>
public sealed partial class PriceTable
{
    private static readonly Lazy<PriceTable> BundledTable = new(LoadBundled);

    private PriceTable(string @checked, IReadOnlyList<ModelPrice> bundled, IReadOnlyList<ModelPrice> overrides)
    {
        Checked = @checked;
        Bundled = bundled;
        Overrides = overrides;
    }

    /// <summary>The date the bundled prices were checked against the providers' pages, as yyyy-MM-dd.</summary>
    public string Checked { get; }

    /// <summary>The bundled prices, in table order.</summary>
    public IReadOnlyList<ModelPrice> Bundled { get; }

    /// <summary>The repository's overrides, in config order.</summary>
    public IReadOnlyList<ModelPrice> Overrides { get; }

    /// <summary>The table shipped with this build.</summary>
    public static PriceTable Default => BundledTable.Value;

    /// <summary>The bundled table with <paramref name="config"/>'s <c>prices</c> merged over it.</summary>
    public static PriceTable For(Config config) => Default.With(config.Prices ?? []);

    /// <summary>Parses a table: <c>{"checked": "yyyy-MM-dd", "prices": [...]}</c>.</summary>
    public static PriceTable Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var @checked = root.GetProperty("checked").GetString() ?? throw new JsonException("price table needs a checked date");
        var prices = root.GetProperty("prices").Deserialize<List<ModelPrice>>(DomainJson.Options) ?? throw new JsonException("price table needs prices");
        Validate(prices);
        return new PriceTable(@checked, prices, []);
    }

    /// <summary>This table with <paramref name="prices"/> taking precedence over the bundled entries.</summary>
    public PriceTable With(IReadOnlyList<ModelPrice> prices) => new(Checked, Bundled, prices);

    /// <summary>The price for <paramref name="model"/> and where it came from (<c>config</c> or <c>table &lt;date&gt;</c>), or null when neither the overrides nor the table know it. An exact id wins over a dated one, and an override wins over the table.</summary>
    public (ModelPrice Price, string Source)? Find(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var id = Normalize(model);
        var candidates = Overrides.Select(price => (Price: price, Source: "config"))
            .Concat(Bundled.Select(price => (Price: price, Source: "table " + Checked)))
            .Select(entry => (entry.Price, entry.Source, Key: Normalize(entry.Price.Model)))
            .ToList();
        var exact = candidates.FirstOrDefault(entry => entry.Key == id);
        if (exact.Price is not null) return (exact.Price, exact.Source);
        var dated = candidates
            .Where(entry => id.StartsWith(entry.Key, StringComparison.Ordinal) && VersionSuffix().IsMatch(id[entry.Key.Length..]))
            .OrderByDescending(entry => entry.Key.Length)
            .FirstOrDefault();
        return dated.Price is null ? null : (dated.Price, dated.Source);
    }

    /// <summary>The cost of one call and its source: the harness's own figure when it reported one, otherwise its tokens at the answering model's price, or the requested model's when the harness did not say which answered. Null, never zero, when the call reported no tokens or its model has no price.</summary>
    public (decimal? CostUsd, string? Source) Cost(AgentIdentity by, AgentUsage usage)
    {
        if (usage.ReportedCostUsd is { } reported) return (reported, "harness");
        if (!usage.HasTokens || Find(usage.Model ?? by.Model) is not { } found) return (null, null);
        var price = found.Price;
        var writes = usage.CacheWriteTokens ?? 0;
        var longWrites = Math.Min(usage.CacheWrite1hTokens ?? 0, writes);
        var cost = (usage.InputTokens ?? 0) * price.Input
            + (usage.OutputTokens ?? 0) * price.Output
            + (usage.CacheReadTokens ?? 0) * (price.CacheRead ?? price.Input)
            + (writes - longWrites) * (price.CacheWrite ?? price.Input)
            + longWrites * (price.CacheWrite1h ?? price.CacheWrite ?? price.Input);
        return (cost / 1_000_000m, found.Source);
    }

    /// <summary>Checks price entries, throwing <see cref="JsonException"/> for a missing model or a negative rate.</summary>
    public static void Validate(IReadOnlyList<ModelPrice> prices)
    {
        foreach (var price in prices)
        {
            if (price is null || string.IsNullOrWhiteSpace(price.Model))
                throw new JsonException("each prices entry needs a model");
            if (price.Input < 0 || price.Output < 0 || price.CacheRead < 0 || price.CacheWrite < 0 || price.CacheWrite1h < 0)
                throw new JsonException(string.Create(CultureInfo.InvariantCulture, $"prices for {price.Model} must not be negative"));
        }
    }

    // Harnesses name models in any case, some with a provider prefix such as "anthropic/".
    private static string Normalize(string model)
    {
        var trimmed = model.Trim().ToLowerInvariant();
        var slash = trimmed.LastIndexOf('/');
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    // Only a release date or a context tag may follow a known id, so gpt-5-mini never takes gpt-5's price and an unknown claude-opus-5-9 stays unpriced rather than wrongly priced.
    [GeneratedRegex(@"^(?:[-@](?:\d{8}|\d{4}-\d{2}-\d{2}))?(?:\[[^\]]*\])?$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffix();

    private static PriceTable LoadBundled()
    {
        using var stream = typeof(PriceTable).Assembly.GetManifestResourceStream("CodeMuster.Application.prices.json")
            ?? throw new InvalidOperationException("the bundled price table is missing from this build");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
