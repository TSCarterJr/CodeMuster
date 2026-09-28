using System.Text.Json.Serialization;

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
/// <param name="NormalizedHash">Hash of the body with identifiers and literals replaced, so copies that differ only in names match (D68); null when the mapper does not compute one.</param>
public sealed record Symbol(string Id, string Path, LineRange Range, string Kind, string Signature, string BodyHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NormalizedHash = null);

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

    /// <summary>A static HTTP call in UI code matched by method and route to the C# <c>http</c> entry point that serves it (D61).</summary>
    Http,
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
public sealed record CodeMap(IReadOnlyList<Symbol> Symbols, IReadOnlyList<Edge> Edges, IReadOnlyList<EntryPoint> EntryPoints, ResolutionStats Resolution, IReadOnlyList<string> Diagnostics)
{
    /// <summary>Every outgoing HTTP call the mapper read statically (D61); empty when the mapper records none. Scan joins them to entry points as <see cref="EdgeKind.Http"/> edges and the ledger does not store them.</summary>
    public IReadOnlyList<HttpCall> HttpCalls { get; init; } = [];

    /// <summary>The UI's structure the mapper read from markup (D69): page routes, navigation entries, section headings and form controls; empty when it read none. The ledger stores it with the map, so the architecture pack can render it after the scan.</summary>
    [JsonIgnore]
    public IReadOnlyList<UiElement> UiElements { get; init; } = [];

    /// <summary>Languages the mapper chose not to map because nothing asked for mapping and a tool it needs is missing, each with the note to show; their files stay whole-file units and count as neither mapped nor failed.</summary>
    [JsonIgnore]
    public IReadOnlyList<SkippedLanguage> SkippedLanguages { get; init; } = [];

    [JsonInclude]
    [JsonPropertyName("skipped_languages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private IReadOnlyList<SkippedLanguage>? SerializedSkippedLanguages
    {
        get => SkippedLanguages.Count == 0 ? null : SkippedLanguages;
        init => SkippedLanguages = value ?? [];
    }

    /// <summary>Declarations without a body that references can point at (D72): types, fields, properties, constants, enum members, and TypeScript interfaces, type aliases and exported constants. Slices never read them.</summary>
    [JsonIgnore]
    public IReadOnlyList<Symbol> Declarations { get; init; } = [];

    /// <summary>Every use of a symbol or declaration the mapper resolved (D72): calls, reads, writes, type uses, inheritance, attributes and imports, each at its position.</summary>
    [JsonIgnore]
    public IReadOnlyList<Reference> References { get; init; } = [];

    [JsonInclude]
    [JsonPropertyName("declarations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private IReadOnlyList<Symbol>? SerializedDeclarations
    {
        get => Declarations.Count == 0 ? null : Declarations;
        init => Declarations = value ?? [];
    }

    [JsonInclude]
    [JsonPropertyName("references")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private IReadOnlyList<Reference>? SerializedReferences
    {
        get => References.Count == 0 ? null : References;
        init => References = value ?? [];
    }

    // Written only when there is at least one element, so a map without UI reads and writes as before.
    [JsonInclude]
    [JsonPropertyName("ui_elements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    private IReadOnlyList<UiElement>? SerializedUiElements
    {
        get => UiElements.Count == 0 ? null : UiElements;
        init => UiElements = value ?? [];
    }
}

/// <summary>A language a mapper left unmapped on purpose, such as loose JavaScript with no config and no compiler to read it.</summary>
/// <param name="Language">One of the <see cref="Languages"/> constants.</param>
/// <param name="Note">One line to show the user, starting with the language, saying why and how to have it mapped.</param>
public sealed record SkippedLanguage(string Language, string Note);

/// <summary>One piece of the UI's structure, read from markup by a mapper (D69).</summary>
/// <param name="Kind">One of "route" (a page), "nav" (a navigation or menu entry), "heading" (a section heading) or "control" (a form control or setting).</param>
/// <param name="Text">The route, the entry's or heading's text, or the control's label; empty when a control has none.</param>
/// <param name="Path">Repo-relative path of the file that defines it.</param>
/// <param name="Line">1-based line where it is defined.</param>
/// <param name="Control">For a control, what it is: an input type such as "text" or "checkbox", "select", "textarea", or the component's name such as "Switch".</param>
/// <param name="Target">For a navigation entry, the route or URL it links to.</param>
/// <param name="Section">The nearest section heading the element sits under, when there is one.</param>
/// <param name="Route">The page route of the file, when the file is a page.</param>
public sealed record UiElement(string Kind, string Text, string Path, int Line,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Control = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Section = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Route = null);

/// <summary>An outgoing HTTP request that UI code makes, read statically by a mapper (D61).</summary>
/// <param name="From">Id of the symbol whose body contains the call.</param>
/// <param name="Method">Upper-case HTTP method: the literal the call names, GET when it names none, or ANY when it is chosen at runtime.</param>
/// <param name="Url">Path template with the origin, query string and fragment removed and each runtime part written as a <c>{name}</c> parameter; null when the URL is built at runtime.</param>
/// <param name="Text">Source text of the URL argument, whitespace collapsed.</param>
/// <param name="Path">Repo-relative path of the file containing the call.</param>
/// <param name="Line">1-based line of the call.</param>
public sealed record HttpCall(string From, string Method, string? Url, string Text, string Path, int Line)
{
    /// <summary>Starts every map diagnostic the UI-to-API join records, so a reader can tell them from mapper diagnostics.</summary>
    public const string DiagnosticPrefix = "http: ";
}

/// <summary>How a reference uses what it points at (D72).</summary>
public enum ReferenceKind
{
    /// <summary>A call or constructor invocation.</summary>
    Call,

    /// <summary>A read of a field, property, constant, enum member or variable.</summary>
    Read,

    /// <summary>An assignment to a field, property or variable.</summary>
    Write,

    /// <summary>A use of a type: parameter, return, variable, generic argument, cast, <c>typeof</c> or <c>new</c>.</summary>
    Type,

    /// <summary>A class deriving from a class.</summary>
    Inherit,

    /// <summary>A type implementing an interface.</summary>
    Implement,

    /// <summary>An attribute or decorator applied to a declaration.</summary>
    Attribute,

    /// <summary>A TypeScript or JavaScript import of a symbol.</summary>
    Import,
}

/// <summary>One resolved use of a symbol or declaration (D72).</summary>
/// <param name="From">Id of the symbol or declaration that contains the use, or the file's path when nothing contains it.</param>
/// <param name="To">Id of the symbol or declaration used.</param>
/// <param name="Kind">How it is used.</param>
/// <param name="Path">Repo-relative path of the file holding the use.</param>
/// <param name="Line">1-based line of the use.</param>
/// <param name="Column">1-based column of the use.</param>
public sealed record Reference(string From, string To, ReferenceKind Kind, string Path, int Line, int Column);
