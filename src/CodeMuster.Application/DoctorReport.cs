using System.Globalization;

namespace CodeMuster.Application;

/// <summary>How one probe went.</summary>
public enum ProbeState
{
    /// <summary>The probe did its job: git listed files, or the mapper returned symbols with no diagnostics.</summary>
    Working,

    /// <summary>The mapper ran without error but returned no symbols, the silent failure D09 guards against.</summary>
    LoadedButEmpty,

    /// <summary>The probe threw or the mapper reported diagnostics.</summary>
    Failed,

    /// <summary>The mapper left a language unmapped on purpose, such as loose JavaScript with no compiler to read it; its files are reviewed whole and the repository is still ready.</summary>
    NotMapped,
}

/// <summary>One probe's outcome.</summary>
/// <param name="Name"><c>git</c> or a mapper's language.</param>
/// <param name="State">How it went.</param>
/// <param name="Elapsed">Time the mapper took to be ready, or null for git.</param>
/// <param name="Symbols">Symbols the mapper returned.</param>
/// <param name="Problems">What went wrong, each naming its fix.</param>
public sealed record DoctorProbe(string Name, ProbeState State, TimeSpan? Elapsed, int Symbols, IReadOnlyList<string> Problems);

/// <summary>Commands that fix what a probe found, run one after another in <paramref name="Folder"/> by <c>doctor --fix</c> (D70).</summary>
/// <param name="Folder">Where they run, relative to the repository root with forward slashes; empty for the root itself.</param>
/// <param name="Commands">Each command as the program and its arguments.</param>
public sealed record DoctorFix(string Folder, IReadOnlyList<IReadOnlyList<string>> Commands)
{
    /// <summary>The commands as a user would type them, joined with <c>&amp;&amp;</c> and led by a <c>cd</c> outside the root.</summary>
    public string Render()
    {
        var commands = Commands.Select(Line).ToList();
        return string.Join(" && ", Folder.Length == 0 ? commands : ["cd " + Quote(Folder), .. commands]);
    }

    /// <summary>One command as a user would type it.</summary>
    public static string Line(IReadOnlyList<string> command) => string.Join(' ', command.Select(Quote));

    private static string Quote(string argument) => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
}

/// <summary>What <see cref="Doctor"/> found.</summary>
/// <param name="Probes">Git first, then each mapper that ran.</param>
public sealed record DoctorReport(IReadOnlyList<DoctorProbe> Probes)
{
    /// <summary>The commands that fix what the probes found, in the order to run them; empty when doctor knows none.</summary>
    public IReadOnlyList<DoctorFix> Fixes { get; init; } = [];

    /// <summary>True when every probe is working or left its language unmapped on purpose.</summary>
    public bool Ready => Probes.All(p => p.State is ProbeState.Working or ProbeState.NotMapped);

    /// <summary>The commands that make a folder a repository with its files in a first commit.</summary>
    public static DoctorFix FirstCommit(bool init)
    {
        IReadOnlyList<string>[] commit = [["git", "add", "-A"], ["git", "commit", "-m", "Initial commit"]];
        return new("", init ? [["git", "init"], .. commit] : commit);
    }

    /// <summary>The report for a repository git could not open.</summary>
    public static DoctorReport GitFailed(string message) => new([new DoctorProbe("git", ProbeState.Failed, null, 0, [message])]);

    /// <summary>The report for a folder that is not inside a git repository, with the fix that makes it one.</summary>
    public static DoctorReport NotARepository(string message) => GitFailed(message) with { Fixes = [FirstCommit(init: true)] };

    /// <summary>One line per probe with its problems indented under it (a probe that left its language unmapped is its note alone), then <c>ready</c> or <c>not ready</c>; lines joined with LF and no trailing newline.</summary>
    public string Render()
    {
        var lines = new List<string>();
        foreach (var probe in Probes)
        {
            if (probe.State == ProbeState.NotMapped)
            {
                lines.AddRange(probe.Problems);
                continue;
            }

            var state = probe.State switch
            {
                ProbeState.Working => "working",
                ProbeState.LoadedButEmpty => "loaded-but-empty",
                _ => "failed",
            };
            var elapsed = probe.Elapsed is { } time ? string.Create(CultureInfo.InvariantCulture, $" in {time.TotalSeconds:0.0} s") : "";
            var symbols = probe.State == ProbeState.Working && probe.Elapsed is not null ? string.Create(CultureInfo.InvariantCulture, $", {probe.Symbols} symbol(s)") : "";
            lines.Add($"{probe.Name}: {state}{elapsed}{symbols}");
            lines.AddRange(probe.Problems.Select(problem => "  " + problem));
        }

        lines.Add(Ready ? "ready" : "not ready");
        return string.Join('\n', lines);
    }
}
