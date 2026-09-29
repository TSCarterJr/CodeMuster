using System.Globalization;
using CodeMuster.Application;

namespace CodeMuster.Cli;

/// <summary>The step list of <c>codemuster auto</c> at a terminal (D85): numbers toggle steps, Enter starts, q cancels.</summary>
public sealed class StepMenu(TextReader input, TextWriter output)
{
    private static readonly Dictionary<AutoStep, string> Descriptions = new()
    {
        [AutoStep.Doctor] = "check the SDKs, node and a logged-in agent",
        [AutoStep.Scan] = "map the repository and plan the work",
        [AutoStep.Estimate] = "show calls and cost, and ask before spending",
        [AutoStep.Run] = "audit the pending units",
        [AutoStep.Verify] = "check each finding (fix needs this)",
        [AutoStep.Report] = "print the findings",
        [AutoStep.Fix] = "repair confirmed findings, one local commit per file",
        [AutoStep.Validate] = "run test_command on the final tree",
    };

    public IReadOnlyList<AutoStep> Choose(IReadOnlyList<AutoStep> preselected)
    {
        var ticked = preselected.ToHashSet();
        while (true)
        {
            output.WriteLine("Steps to run (type numbers to toggle, Enter to start, q to cancel):");
            for (var i = 0; i < AutoSteps.All.Count; i++)
            {
                var step = AutoSteps.All[i];
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {i + 1} [{(ticked.Contains(step) ? 'x' : ' ')}] {AutoSteps.Name(step),-9} {Descriptions[step]}"));
            }

            output.Write("> ");
            var answer = input.ReadLine()?.Trim() ?? throw new OperationCanceledException("no answer; nothing started");
            if (answer.Equals("q", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException("cancelled; nothing started");
            if (answer.Length == 0)
            {
                if (ticked.Count > 0) return [.. AutoSteps.All.Where(ticked.Contains)];
                output.WriteLine("tick at least one step, or q to cancel");
                continue;
            }

            var numbers = answer.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Any(n => !int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var k) || k < 1 || k > AutoSteps.All.Count))
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"type step numbers 1-{AutoSteps.All.Count} to toggle them"));
                continue;
            }

            foreach (var step in numbers.Select(n => AutoSteps.All[int.Parse(n, CultureInfo.InvariantCulture) - 1]))
            {
                if (!ticked.Remove(step)) ticked.Add(step);
            }
        }
    }
}
