using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class FakeAgentAdapter : IAgentAdapter
{
    private const double RefutesBelowConfidence = 0.5;

    public static string DefaultTemplate { get; } = AnalysisResponseJson.Serialize(new AnalysisResponse("fake analysis", []));

    private readonly AnalysisResponse _template;

    public FakeAgentAdapter(string templateJson, string? model = null, string? effort = null)
    {
        _template = AnalysisResponseJson.Parse(templateJson);
        Identity = new AgentIdentity("fake", model, effort);
    }

    public AgentIdentity Identity { get; }

    public Task<string> RunAsync(string pack, CancellationToken cancellationToken)
    {
        if (FixTargets(pack) is { Count: > 0 } targets)
        {
            return Task.FromResult(FixResponseJson.Serialize(new FixResponse("fake fix", targets, [])));
        }

        if (FindingUnderTest(pack) is { } finding)
        {
            var verdict = finding.Confidence < RefutesBelowConfidence ? Verdict.Refuted : Verdict.Confirmed;
            return Task.FromResult(VerifyResponseJson.Serialize(new VerifyResponse(verdict, "fake verification")));
        }

        var path = FirstFilePath(pack);
        var findings = _template.Findings.Select(finding => finding with { Path = path }).ToList();
        return Task.FromResult(AnalysisResponseJson.Serialize(_template with { Findings = findings }));
    }

    private static IReadOnlyList<long> FixTargets(string pack)
    {
        var lines = pack.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (!lines.Contains("- kind: fix"))
        {
            return [];
        }

        return lines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("\"id\":", StringComparison.Ordinal))
            .Select(line => long.Parse(line["\"id\":".Length..].Trim().TrimEnd(','), System.Globalization.CultureInfo.InvariantCulture))
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
