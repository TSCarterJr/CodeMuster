namespace CodeMuster.Domain;

/// <summary>A 1-based, inclusive range of lines within a file.</summary>
/// <param name="StartLine">First line of the range.</param>
/// <param name="EndLine">Last line of the range.</param>
public sealed record LineRange(int StartLine, int EndLine);

/// <summary>A piece of code with a body, found by a mapper (D26).</summary>
/// <param name="Id">Stable id unique within the map: the Roslyn documentation comment id for C#, <c>path#name</c> for TypeScript.</param>
/// <param name="Path">Repo-relative file path with forward slashes.</param>
/// <param name="Range">Lines the declaration spans, attributes included, leading comments excluded.</param>
/// <param name="Kind">Free-form kind chosen by the mapper, such as "method" or "function".</param>
/// <param name="Signature">The declaration without its body, whitespace collapsed; a type member's signature starts with the type's header line.</param>
/// <param name="BodyHash">Hash of the declaration that whitespace-only edits leave unchanged.</param>
public sealed record Symbol(string Id, string Path, LineRange Range, string Kind, string Signature, string BodyHash);

/// <summary>How a call site reaches the symbol an edge points at (D26).</summary>
public enum EdgeKind
{
    /// <summary>A direct call to a symbol with a body.</summary>
    Call,

    /// <summary>A call to an interface member, resolved to the implementation in the type a DI registration binds that interface to.</summary>
    Bound,

    /// <summary>A call to an interface member with no DI binding; one edge per implementation.</summary>
    Implements,

    /// <summary>A call to a virtual or abstract member; one edge per override.</summary>
    Overrides,
}

/// <summary>A directed edge from the symbol that contains a call site to a symbol that call can reach.</summary>
/// <param name="From">Id of the calling symbol.</param>
/// <param name="To">Id of the reached symbol.</param>
/// <param name="Kind">How the call reaches it.</param>
public sealed record Edge(string From, string To, EdgeKind Kind);

/// <summary>A symbol where execution enters the codebase from outside.</summary>
/// <param name="SymbolId">Id of the entry symbol.</param>
/// <param name="Kind">One of "http", "background", or "page".</param>
/// <param name="Display">Human-readable label, such as "GET /quotes".</param>
public sealed record EntryPoint(string SymbolId, string Kind, string Display);

/// <summary>How many call sites a mapper resolved to a symbol, inside or outside the map, and which names it could not.</summary>
/// <param name="Resolved">Count of call sites resolved to a symbol.</param>
/// <param name="Unresolved">Count of call sites left unresolved.</param>
/// <param name="TopUnresolvedNames">Most frequent unresolved names, most frequent first.</param>
public sealed record ResolutionStats(int Resolved, int Unresolved, IReadOnlyList<string> TopUnresolvedNames);

/// <summary>The flat map every language mapper emits: symbols, edges, entry points, resolution stats, and diagnostics.</summary>
/// <param name="Symbols">Every symbol found.</param>
/// <param name="Edges">Every edge found between symbols.</param>
/// <param name="EntryPoints">Every external entry into the codebase.</param>
/// <param name="Resolution">Call-site resolution statistics.</param>
/// <param name="Diagnostics">Why the map may be incomplete, such as a missing restore; empty when nothing went wrong.</param>
public sealed record CodeMap(IReadOnlyList<Symbol> Symbols, IReadOnlyList<Edge> Edges, IReadOnlyList<EntryPoint> EntryPoints, ResolutionStats Resolution, IReadOnlyList<string> Diagnostics);
