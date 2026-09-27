using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>How <c>doctor --fix</c> treats the fixes it found (D70).</summary>
public enum DoctorFixMode
{
    /// <summary>Only print them, as for redirected or CI runs.</summary>
    PrintOnly,

    /// <summary>Ask before each one, as on a terminal.</summary>
    Ask,

    /// <summary>Run them all, as with <c>--yes</c>.</summary>
    All,
}

/// <summary>Runs the commands a <see cref="DoctorReport"/> offers, each fix's commands in order in its folder under <paramref name="root"/>, and says what happened on <paramref name="output"/>.</summary>
public sealed class DoctorFixer(ICommandRunner commands, string root, IProgress<string> output)
{
    private const int ReasonLines = 20;

    /// <summary>Applies <paramref name="fixes"/> as <paramref name="mode"/> says, asking <paramref name="ask"/> before each in <see cref="DoctorFixMode.Ask"/>; a failing command stops the rest of its fix only. Returns how many fixes were started.</summary>
    public async Task<int> ApplyAsync(IReadOnlyList<DoctorFix> fixes, DoctorFixMode mode, Func<DoctorFix, bool> ask, CancellationToken cancellationToken)
    {
        if (mode == DoctorFixMode.PrintOnly)
        {
            foreach (var fix in fixes)
            {
                output.Report("would run: " + fix.Render());
            }

            if (fixes.Count > 0)
            {
                output.Report("nothing was run because this is not an interactive terminal; run codemuster doctor --fix in a terminal, or add --yes");
            }

            return 0;
        }

        var started = 0;
        foreach (var fix in fixes)
        {
            if (mode == DoctorFixMode.Ask && !ask(fix))
            {
                output.Report("skipped: " + fix.Render());
                continue;
            }

            started++;
            output.Report("running: " + fix.Render());
            if (await RunAsync(fix, cancellationToken))
            {
                output.Report("done: " + fix.Render());
            }
        }

        return started;
    }

    private async Task<bool> RunAsync(DoctorFix fix, CancellationToken cancellationToken)
    {
        var directory = fix.Folder.Length == 0 ? root : Path.GetFullPath(Path.Combine(root, fix.Folder));
        foreach (var command in fix.Commands)
        {
            var result = await commands.RunAsync(directory, command, cancellationToken);
            if (!result.Succeeded)
            {
                output.Report($"failed: {DoctorFix.Line(command)} exited with code {result.ExitCode}: {Reason(result)}");
                return false;
            }
        }

        return true;
    }

    private static string Reason(CommandResult result)
    {
        var text = (result.Error.Trim().Length > 0 ? result.Error : result.Output).Trim();
        return text.Length == 0 ? "it printed nothing" : string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').TakeLast(ReasonLines));
    }
}
