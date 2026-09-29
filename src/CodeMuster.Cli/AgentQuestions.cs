using System.Globalization;
using CodeMuster.Infrastructure;

namespace CodeMuster.Cli;

/// <summary>The agent settings a person chose at the terminal (D84); a null model or effort is the provider default.</summary>
public sealed record AgentAnswers(string Agent, string? Model, string? Effort, int Jobs);

/// <summary>Asks for the agent, model, thinking level and jobs a command was not given (D84), offering what was remembered for this repository (D86).</summary>
public sealed class AgentQuestions(TextReader input, TextWriter output)
{
    public const int DefaultJobs = 4;

    public AgentAnswers Ask(IReadOnlyList<string> installed, RememberedChoices? remembered, IReadOnlyDictionary<string, string> given)
    {
        if (installed.Count == 0)
            throw new InvalidOperationException("no agent CLI was found on PATH. Install one, for example: npm install -g @anthropic-ai/claude-code, and log in to it; or pass --agent.");

        var agent = given.GetValueOrDefault("agent") ?? AskAgent(installed, remembered?.Agent);
        var same = remembered?.Agent == agent;
        var model = given.GetValueOrDefault("model") ?? AskText("Model", same ? remembered!.Model : null);
        var effort = given.GetValueOrDefault("effort") ?? (agent == "gemini" ? null : AskText("Thinking", same ? remembered!.Effort : null));
        var jobs = given.TryGetValue("jobs", out var typed) ? int.Parse(typed, CultureInfo.InvariantCulture) : AskJobs(remembered?.Jobs ?? DefaultJobs);
        return new AgentAnswers(agent, model, effort, jobs);
    }

    private string AskAgent(IReadOnlyList<string> installed, string? remembered)
    {
        var preselected = remembered is not null && installed.Contains(remembered) ? installed.ToList().IndexOf(remembered) : 0;
        output.WriteLine("Agent:");
        for (var i = 0; i < installed.Count; i++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {i + 1}) {installed[i]}"));
        }

        while (true)
        {
            output.Write(string.Create(CultureInfo.InvariantCulture, $"Choose [{preselected + 1}]: "));
            var answer = Read();
            if (answer.Length == 0) return installed[preselected];
            if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= installed.Count) return installed[number - 1];
            if (installed.FirstOrDefault(name => string.Equals(name, answer, StringComparison.OrdinalIgnoreCase)) is { } named) return named;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"choose 1-{installed.Count} or an agent name"));
        }
    }

    private string? AskText(string label, string? preselected)
    {
        output.Write($"{label} [{preselected ?? "provider default"}]: ");
        var answer = Read();
        return answer.Length == 0 ? preselected : answer;
    }

    private int AskJobs(int preselected)
    {
        while (true)
        {
            output.Write(string.Create(CultureInfo.InvariantCulture, $"Jobs [{preselected}]: "));
            var answer = Read();
            if (answer.Length == 0) return preselected;
            if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var jobs) && jobs > 0) return jobs;
            output.WriteLine("jobs must be a positive whole number");
        }
    }

    private string Read() => input.ReadLine()?.Trim() ?? throw new OperationCanceledException("no answer; nothing started");
}
