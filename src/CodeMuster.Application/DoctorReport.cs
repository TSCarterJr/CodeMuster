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
}

/// <summary>One probe's outcome.</summary>
/// <param name="Name"><c>git</c> or a mapper's language.</param>
/// <param name="State">How it went.</param>
/// <param name="Elapsed">Time the mapper took to be ready, or null for git.</param>
/// <param name="Symbols">Symbols the mapper returned.</param>
/// <param name="Problems">What went wrong, each naming its fix.</param>
public sealed record DoctorProbe(string Name, ProbeState State, TimeSpan? Elapsed, int Symbols, IReadOnlyList<string> Problems);

/// <summary>What <see cref="Doctor"/> found.</summary>
/// <param name="Probes">Git first, then each mapper that ran.</param>
public sealed record DoctorReport(IReadOnlyList<DoctorProbe> Probes)
{
    /// <summary>True when every probe is working.</summary>
    public bool Ready => Probes.All(p => p.State == ProbeState.Working);

    /// <summary>The report for a repository git could not open.</summary>
    public static DoctorReport GitFailed(string message) => new([new DoctorProbe("git", ProbeState.Failed, null, 0, [message])]);

    /// <summary>One line per probe with its problems indented under it, then <c>ready</c> or <c>not ready</c>; lines joined with LF and no trailing newline.</summary>
    public string Render()
    {
        var lines = new List<string>();
        foreach (var probe in Probes)
        {
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
