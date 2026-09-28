using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>
/// The <c>impact</c> verb (D67): reads the stored map and never scans or calls a model.
/// Without a ref it lists the impact units the last scan left, each with the callers, entry points and pages its symbol reaches;
/// with <c>--since &lt;ref&gt;</c> it lists the stored symbols in the files committed since that ref, the same way.
/// </summary>
public sealed class ImpactQuery(ILedger ledger, ISourceTree tree)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal sealed record Row(Symbol Symbol, Unit? Unit, ImpactReach Reach);

    /// <summary>Answers one <c>impact</c> invocation; exit code 2 with no map or an unknown ref.</summary>
    public async Task<MapResult> RunAsync(string? since, MapFormat format, CancellationToken cancellationToken)
    {
        var stored = await ledger.GetCodeMapAsync(cancellationToken);
        if (stored is null)
        {
            return new MapResult(2, "", "no code map yet; run codemuster scan");
        }

        var symbols = stored.Map.Symbols.DistinctBy(s => s.Id, StringComparer.Ordinal).ToList();
        var byId = symbols.Concat(stored.Map.Declarations).DistinctBy(s => s.Id, StringComparer.Ordinal).ToDictionary(s => s.Id, StringComparer.Ordinal);
        var impact = (await ledger.GetUnitsAsync(cancellationToken))
            .Where(u => u.Kind == UnitKind.Impact && u.Status != UnitStatus.Retired)
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
        Row RowOf(Symbol symbol) => new(symbol, impact.GetValueOrDefault(UnitIds.Impact(symbol.Id)), ImpactReview.Walk(stored.Map, symbol.Id));

        if (since is null)
        {
            var rows = impact.Values
                .Select(u => byId.GetValueOrDefault(u.Id[UnitIds.Impact("").Length..]))
                .OfType<Symbol>()
                .Select(RowOf)
                .ToList();
            if (format == MapFormat.Json) return Ok(Json(stored, null, rows, []));
            var text = new StringBuilder();
            text.Append(rows.Count == 0
                ? $"no impact units from the scan at {Short(stored.HeadCommit)}; a scan plans one for each symbol whose body or signature changed since the scan before it\n"
                : string.Create(CultureInfo.InvariantCulture, $"{rows.Count} impact unit{(rows.Count == 1 ? "" : "s")} from the scan at {Short(stored.HeadCommit)} ({rows.Count(r => r.Unit!.Status == UnitStatus.Done)} done)\n"));
            foreach (var row in rows)
            {
                Append(text, row, row.Unit!.Status.ToString().ToLowerInvariant());
            }

            return Ok(text.ToString());
        }

        IReadOnlyList<string> changed;
        try
        {
            changed = await tree.ChangedSinceAsync(since, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new MapResult(2, "", ex.Message);
        }

        var inFiles = changed.SelectMany(path => symbols.Where(s => s.Path == path).OrderBy(s => s.Range.StartLine)).Select(RowOf).ToList();
        var unmapped = changed.Where(path => symbols.All(s => s.Path != path)).ToList();
        if (format == MapFormat.Json) return Ok(Json(stored, since, inFiles, unmapped));
        var output = new StringBuilder();
        output.Append(CultureInfo.InvariantCulture, $"{changed.Count} file{(changed.Count == 1 ? "" : "s")} changed since {since}; {inFiles.Count} mapped symbol{(inFiles.Count == 1 ? "" : "s")} in them (map at {Short(stored.HeadCommit)}; uncommitted edits are not included)\n");
        foreach (var row in inFiles)
        {
            Append(output, row, row.Unit is { } unit ? "impact unit " + unit.Status.ToString().ToLowerInvariant() : null);
        }

        foreach (var path in unmapped)
        {
            output.Append(path).Append(": no mapped symbols\n");
        }

        return Ok(output.ToString());
    }

    private static MapResult Ok(string output) => new(0, output, "");

    private static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;

    internal static void Append(StringBuilder text, Row row, string? status)
    {
        text.Append(CultureInfo.InvariantCulture, $"{CodeMapQuery.ShortName(row.Symbol)}  {row.Symbol.Path}:{row.Symbol.Range.StartLine}");
        text.Append(status is null ? "\n" : $"  {status}\n");
        text.Append("  callers: ").Append(List(row.Reach.Upward.Select(c => CodeMapQuery.ShortName(c.Symbol)))).Append('\n');
        text.Append("  entry points: ").Append(List(row.Reach.EntryPoints.Where(e => e.Entry.Kind != "page").Select(e => $"{e.Entry.Display} ({e.Entry.Kind})"))).Append('\n');
        text.Append("  pages: ").Append(List(row.Reach.EntryPoints.Where(e => e.Entry.Kind == "page").Select(e => e.Entry.Display))).Append('\n');
    }

    private static string List(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static string Json(StoredCodeMap stored, string? since, IReadOnlyList<Row> rows, IReadOnlyList<string> unmapped)
    {
        var data = new JsonObject
        {
            ["commit"] = stored.HeadCommit,
            ["since"] = since,
            ["symbols"] = new JsonArray([.. rows.Select(row => (JsonNode?)new JsonObject
            {
                ["id"] = row.Symbol.Id,
                ["name"] = CodeMapQuery.ShortName(row.Symbol),
                ["path"] = row.Symbol.Path,
                ["line"] = row.Symbol.Range.StartLine,
                ["impactUnit"] = row.Unit?.Status.ToString().ToLowerInvariant(),
                ["callers"] = new JsonArray([.. row.Reach.Upward.Select(c => (JsonNode?)new JsonObject { ["id"] = c.Symbol.Id, ["name"] = CodeMapQuery.ShortName(c.Symbol), ["depth"] = c.Depth })]),
                ["entryPoints"] = new JsonArray([.. row.Reach.EntryPoints.Select(e => (JsonNode?)new JsonObject { ["display"] = e.Entry.Display, ["kind"] = e.Entry.Kind, ["depth"] = e.Depth })]),
            })]),
            ["unmappedFiles"] = new JsonArray([.. unmapped.Select(path => (JsonNode?)JsonValue.Create(path))]),
        };
        return data.ToJsonString(Indented) + "\n";
    }
}
