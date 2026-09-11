using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>The committed per-repo configuration (D04).</summary>
/// <param name="Lenses">Named lenses; at least one is expected.</param>
public sealed record Config(IReadOnlyList<Lens> Lenses)
{
    /// <summary>Instructions of the lens every repo starts with.</summary>
    public const string DefaultInstructions =
        "Audit every file in this pack for defects a careful reviewer would flag: incorrect logic, unhandled failure paths, "
        + "missing authorization or tenant scoping, injection and unsafe rendering, leaked secrets, and concurrency mistakes. "
        + "Report only what the code shown evidences, cite exact lines, and prefer fewer high-confidence findings over speculation.";

    /// <summary>One lens named <c>default</c> that applies to every file.</summary>
    public static readonly Config Default = new([new Lens("default", DefaultInstructions, [], [])]);

    /// <summary>Every lens that applies to at least one of the files, ordered by id.</summary>
    public IReadOnlyList<Lens> LensesFor(IEnumerable<(string Path, string Language)> files)
    {
        var list = files.ToList();
        return Lenses
            .Where(lens => list.Any(file => lens.Applies(file.Path, file.Language)))
            .OrderBy(lens => lens.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Order-independent hash over a set of lenses; stored on each analysis so a lens edit marks its units stale.</summary>
    public static string HashOf(IEnumerable<Lens> lenses) =>
        Hashing.Sha256Hex(string.Join('\n', lenses.Select(lens => lens.Hash()).Order(StringComparer.Ordinal)));
}
