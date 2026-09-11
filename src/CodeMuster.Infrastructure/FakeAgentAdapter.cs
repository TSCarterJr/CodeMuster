using CodeMuster.Domain;

namespace CodeMuster.Infrastructure;

public sealed class FakeAgentAdapter : IAgentAdapter
{
    public static string DefaultTemplate { get; } = AnalysisResponseJson.Serialize(new AnalysisResponse("fake analysis", []));

    private readonly AnalysisResponse _template;

    public FakeAgentAdapter(string templateJson)
    {
        _template = AnalysisResponseJson.Parse(templateJson);
    }

    public Task<string> RunAsync(string pack, CancellationToken cancellationToken)
    {
        var path = FirstFilePath(pack);
        var findings = _template.Findings.Select(finding => finding with { Path = path }).ToList();
        return Task.FromResult(AnalysisResponseJson.Serialize(_template with { Findings = findings }));
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
