namespace CodeMuster.Domain;

/// <summary>Derives the type, class or file a symbol belongs to from its id (D26, D60).</summary>
public static class SymbolContainer
{
    /// <summary>
    /// The container of <paramref name="symbol"/>. A TypeScript id <c>path#Class.method</c> belongs to <c>path#Class</c> and <c>path#name</c> to the file path.
    /// A C# documentation comment id belongs to its type: the part before the member name, after the <c>M:</c> prefix and before the parameter list or conversion return type,
    /// so nested and generic types keep their full name (<c>Ns.Outer`1.Inner</c>). An id with no type part has an empty container.
    /// </summary>
    public static string Of(Symbol symbol)
    {
        var id = symbol.Id;
        var file = symbol.Path + "#";
        if (id.StartsWith(file, StringComparison.Ordinal))
        {
            var name = id.AsSpan(file.Length);
            var dot = name.IndexOf('.');
            return dot < 0 ? symbol.Path : id[..(file.Length + dot)];
        }

        var member = id.Length > 1 && id[1] == ':' ? id.AsSpan(2) : id.AsSpan();
        // Explicit interface names in documentation ids write their dots as '#', so the last '.' before the parameters always ends the type name.
        var end = member.IndexOfAny('(', '~');
        if (end >= 0)
        {
            member = member[..end];
        }

        var last = member.LastIndexOf('.');
        return last < 0 ? "" : member[..last].ToString();
    }
}
