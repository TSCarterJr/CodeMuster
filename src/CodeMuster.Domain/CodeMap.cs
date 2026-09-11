namespace CodeMuster.Domain;

/// <summary>A 1-based, inclusive range of lines within a file.</summary>
/// <param name="StartLine">First line of the range.</param>
/// <param name="EndLine">Last line of the range.</param>
public sealed record LineRange(int StartLine, int EndLine);

/// <summary>A named code unit found by a mapper.</summary>
/// <param name="Id">Stable identifier unique within the map.</param>
/// <param name="Path">Repo-relative file path with forward slashes.</param>
/// <param name="Range">Lines the symbol spans.</param>
/// <param name="Kind">Free-form kind chosen by the mapper, such as "method" or "class".</param>
/// <param name="Signature">Declaration text as the mapper presents it.</param>
/// <param name="BodyHash">Hash of the symbol's body used to detect change.</param>
public sealed record Symbol(string Id, string Path, LineRange Range, string Kind, string Signature, string BodyHash);

/// <summary>The relationship an edge expresses between two symbols.</summary>
public enum EdgeKind
{
    /// <summary>The source invokes the target.</summary>
    Call,

    /// <summary>The source implements the target interface or contract.</summary>
    Implements,

    /// <summary>The source overrides the target member.</summary>
    Overrides,

    /// <summary>The source imports the target module or symbol.</summary>
    Imports,
}

/// <summary>A directed relationship between two symbols.</summary>
/// <param name="From">Id of the source symbol.</param>
/// <param name="To">Id of the target symbol.</param>
/// <param name="Kind">The relationship kind.</param>
public sealed record Edge(string From, string To, EdgeKind Kind);

/// <summary>A symbol where execution enters the codebase from outside.</summary>
/// <param name="SymbolId">Id of the entry symbol.</param>
/// <param name="Kind">Free-form kind chosen by the mapper, such as "http".</param>
/// <param name="Display">Human-readable label, such as "GET /quotes".</param>
public sealed record EntryPoint(string SymbolId, string Kind, string Display);

/// <summary>How many references a mapper resolved to symbols and which names it could not.</summary>
/// <param name="Resolved">Count of references resolved to a symbol.</param>
/// <param name="Unresolved">Count of references left unresolved.</param>
/// <param name="TopUnresolvedNames">Most frequent unresolved names.</param>
public sealed record ResolutionStats(int Resolved, int Unresolved, IReadOnlyList<string> TopUnresolvedNames);

/// <summary>The flat map every language mapper emits: symbols, edges, entry points, and resolution stats.</summary>
/// <param name="Symbols">Every symbol found.</param>
/// <param name="Edges">Every relationship found between symbols.</param>
/// <param name="EntryPoints">Every external entry into the codebase.</param>
/// <param name="Resolution">Reference resolution statistics.</param>
public sealed record CodeMap(IReadOnlyList<Symbol> Symbols, IReadOnlyList<Edge> Edges, IReadOnlyList<EntryPoint> EntryPoints, ResolutionStats Resolution);
