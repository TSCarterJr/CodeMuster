using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class FakeAgentAdapter : IAgentAdapter
{
    private const double RefutesBelowConfidence = 0.5;

    /// <summary>What every fake call reports using, so the whole spend path runs without a model (D63).</summary>
    public static AgentUsage Usage { get; } = new(1200, 300, 0, 0, "fake", null);

    public static string DefaultTemplate { get; } = AnalysisResponseJson.Serialize(new AnalysisResponse("fake analysis", []));

    private readonly AnalysisResponse _template;
    private readonly string? _rawResponse;
    private readonly string? _workingDirectory;
    private readonly TimeSpan _delay;

    // delay holds every call open that long, so tests can act while units are in flight.
    public FakeAgentAdapter(string templateJson, string? model = null, string? effort = null, bool rawResponse = false, string? workingDirectory = null, TimeSpan delay = default)
    {
        _delay = delay;
        _workingDirectory = workingDirectory;
        _rawResponse = rawResponse ? templateJson : null;
        _template = AnalysisResponseJson.Parse(rawResponse ? DefaultTemplate : templateJson);
        Identity = new AgentIdentity("fake", model, effort);
    }

    public AgentIdentity Identity { get; }

    public async Task<AgentReply> RunAsync(string pack, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_delay > TimeSpan.Zero) await Task.Delay(_delay, cancellationToken);
        return new AgentReply(Respond(pack), Usage);
    }

    private string Respond(string pack)
    {
        if (_rawResponse is not null) return _rawResponse;
        if (pack.StartsWith("# CodeMuster batch", StringComparison.Ordinal))
        {
            // A batched pack (D80): each unit's section is its own pack with headings one level down.
            var sections = pack.Split("\n## Unit ").Skip(1).Select(section => (
                Unit: section[(section.IndexOf(": ", StringComparison.Ordinal) + 2)..section.IndexOf('\n')],
                Pack: string.Join('\n', section.Split('\n').Select(line => line.StartsWith("##", StringComparison.Ordinal) ? line[1..] : line))));
            return BatchAnalysisResponseJson.Serialize(new BatchAnalysisResponse([.. sections.Select(section =>
            {
                var path = FirstFilePath(section.Pack);
                return new UnitAnalysis(section.Unit, _template.Summary, [.. _template.Findings.Select(finding => finding with { Path = path })]);
            })]));
        }

        if (FixTargets(pack) is { Count: > 0 } targets)
        {
            if (_workingDirectory is not null)
            {
                var key = pack.Split('\n').Select(line => line.TrimEnd('\r')).First(line => line.StartsWith("- key: ", StringComparison.Ordinal))["- key: ".Length..];
                // A comment keeps C#, TypeScript and JavaScript building; a newline keeps JSON and project files valid.
                var edit = Languages.FromPath(key) is Languages.CSharp or Languages.TypeScript or Languages.JavaScript ? "\n// codemuster fake fix\n" : "\n";
                File.AppendAllText(Path.Combine(_workingDirectory, key), edit);
            }

            return FixResponseJson.Serialize(new FixResponse("fake fix", targets, []));
        }

        if (FindingsUnderTest(pack) is { Count: > 0 } batch)
        {
            return VerifyBatchResponseJson.Serialize(new VerifyBatchResponse([.. batch.Select(f => new FindingVerdict(
                f.Id, f.Finding.Confidence < RefutesBelowConfidence ? Verdict.Refuted : Verdict.Confirmed, "fake verification"))]));
        }

        if (FindingUnderTest(pack) is { } finding)
        {
            var verdict = finding.Confidence < RefutesBelowConfidence ? Verdict.Refuted : Verdict.Confirmed;
            return VerifyResponseJson.Serialize(new VerifyResponse(verdict, "fake verification"));
        }

        var path = FirstFilePath(pack);
        var findings = _template.Findings.Select(finding => finding with { Path = path }).ToList();
        return AnalysisResponseJson.Serialize(_template with { Findings = findings });
    }

    private static IReadOnlyList<long> FixTargets(string pack)
    {
        var lines = pack.Split('\n').Select(line => line.TrimEnd('\r')).TakeWhile(line => line != "## Files").ToList();
        var heading = lines.IndexOf("## Findings");
        if (!lines.Contains("- kind: fix") || heading < 0)
        {
            return [];
        }

        var json = lines.Skip(heading + 3).TakeWhile(line => line != "```");
        using var document = JsonDocument.Parse(string.Join('\n', json));
        return document.RootElement.EnumerateArray()
            .Select(finding => finding.GetProperty("id").GetInt64())
            .ToList();
    }

    private static IReadOnlyList<(long Id, Finding Finding)> FindingsUnderTest(string pack)
    {
        var lines = pack.Split('\n').Select(line => line.TrimEnd('\r')).TakeWhile(line => line != "## Files").ToList();
        var heading = lines.IndexOf("## Findings");
        if (!lines.Contains("- kind: verify") || heading < 0)
        {
            return [];
        }

        var json = lines.Skip(heading + 3).TakeWhile(line => line != "```");
        using var document = JsonDocument.Parse(string.Join('\n', json));
        return document.RootElement.EnumerateArray()
            .Select(item => (item.GetProperty("id").GetInt64(), item.GetProperty("finding").Deserialize<Finding>(DomainJson.Options)!))
            .ToList();
    }

    private static Finding? FindingUnderTest(string pack)
    {
        var lines = pack.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var heading = lines.IndexOf("## Finding");
        if (!lines.Contains("- kind: verify") || heading < 0)
        {
            return null;
        }

        var json = lines.Skip(heading + 3).TakeWhile(line => line != "```");
        return JsonSerializer.Deserialize<Finding>(string.Join('\n', json), DomainJson.Options);
    }

    private static string FirstFilePath(string pack)
    {
        var lines = pack.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var files = lines.IndexOf("## Files");
        if (files >= 0)
        {
            foreach (var line in lines.Skip(files + 1))
            {
                if (!line.StartsWith("### ", StringComparison.Ordinal))
                {
                    continue;
                }

                var heading = line[4..];
                var language = heading.LastIndexOf(" (", StringComparison.Ordinal);
                if (language < 0)
                {
                    continue;
                }

                var symbol = heading.IndexOf(" :: ", StringComparison.Ordinal);
                return heading[..(symbol >= 0 && symbol < language ? symbol : language)];
            }
        }

        throw new InvalidOperationException("The pack lists no file under ## Files.");
    }
}
