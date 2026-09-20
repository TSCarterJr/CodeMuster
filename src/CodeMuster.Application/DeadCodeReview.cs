using System.Globalization;
using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>What the available map can establish about a symbol's usage.</summary>
public enum DeadCodeState
{
    /// <summary>A supported external or framework entry protects this symbol.</summary>
    ProtectedEntryPoint,
    /// <summary>A mapped call path reaches this symbol from a protected entry.</summary>
    Reachable,
    /// <summary>No mapped entry reaches an internal symbol; this remains a report-only candidate.</summary>
    Candidate,
    /// <summary>Missing or dynamic invocation evidence prevents a useful unused-code inference.</summary>
    Unknown,
}

/// <summary>Conservative usage evidence for one mapped declaration, never authority to remove it.</summary>
public sealed record DeadCodeAssessment(string SymbolId, string Path, LineRange Range, DeadCodeState State, string Reason, IReadOnlyList<string> UsageEvidence);

/// <summary>Protects entry points and distinguishes unused candidates from incomplete usage evidence.</summary>
public static partial class DeadCodeReview
{
    /// <summary>The reserved finding category and lens for unused-code candidates.</summary>
    public const string Id = "dead_code";

    /// <summary>The required scope and evidence boundaries for unused-code review.</summary>
    public const string Instructions =
        "Review unused-code candidates across the complete application: UI action to HTTP request to endpoint to service. "
        + "HTTP endpoints, public library APIs, dependency injection, reflection, generated clients, framework callbacks, scheduled jobs, and configuration-driven registrations can be invoked without a direct code caller. "
        + "An orphan unit or a missing reference is not proof that code is dead. Follow existing entry points and their transitive calls, including groups that only call each other. "
        + "Cite positive usage evidence and unresolved dynamic or external consumers. Report only bounded internal candidates as category dead_code and lens_id dead_code; explain the checked scope and why usage remains uncertain. "
        + "These findings are report-only: do not delete code, endpoints, exports, registrations, or tests based on this analysis. Runtime inactivity and AI confidence do not establish safe removal.";

    /// <summary>True when either identifying field places a finding under the unused-code safeguards.</summary>
    public static bool IsFinding(Finding finding) =>
        string.Equals(finding.Category.Trim(), Id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(finding.LensId.Trim(), Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>False for every unused-code finding, including historical findings marked confirmed.</summary>
    public static bool CanAutoFix(Finding finding) => !IsFinding(finding);

    /// <summary>Follows all mapped edges from known external entries; incomplete or dynamic maps cannot establish unused candidates.</summary>
    public static IReadOnlyList<DeadCodeAssessment> Analyze(CodeMap map, IReadOnlyDictionary<string, string> sources)
    {
        var symbols = map.Symbols.DistinctBy(symbol => symbol.Id, StringComparer.Ordinal).ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var entries = map.EntryPoints.ToLookup(entry => entry.SymbolId, StringComparer.Ordinal);
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols.Values)
        {
            var entry = entries[symbol.Id].FirstOrDefault();
            var reason = entry is null ? ProtectedReason(symbol) : entry.Kind == "http"
                ? $"HTTP endpoint {entry.Display} is externally callable even when repository code has no caller."
                : $"Mapped {entry.Kind} entry point {entry.Display} can be invoked externally or by its framework.";
            if (reason is not null) roots[symbol.Id] = reason;
        }

        var reachable = roots.Keys.ToHashSet(StringComparer.Ordinal);
        var calls = map.Edges.ToLookup(edge => edge.From, edge => edge.To, StringComparer.Ordinal);
        var queue = new Queue<string>(roots.Keys);
        while (queue.TryDequeue(out var id))
        {
            foreach (var target in calls[id])
            {
                if (symbols.ContainsKey(target) && reachable.Add(target)) queue.Enqueue(target);
            }
        }

        var uncertainty = map.Diagnostics.Count > 0 || map.Resolution.Unresolved > 0
            ? "Mapping has diagnostics or unresolved calls; absent mapped reachability cannot establish unused code."
            : sources.Values.Any(HasDynamicInvocation)
                ? "Repository source contains dynamic invocation or runtime registration; its consumers are not fully established."
                : null;
        var requests = Requests(sources);
        return symbols.Values.OrderBy(symbol => symbol.Path, StringComparer.Ordinal).ThenBy(symbol => symbol.Range.StartLine).ThenBy(symbol => symbol.Id, StringComparer.Ordinal)
            .Select(symbol => Assessment(symbol, entries[symbol.Id], roots, reachable, uncertainty, requests, sources.ContainsKey(symbol.Path)))
            .ToList();
    }

    /// <summary>Formats evidence for a finding or a review pack without implying runtime observation or safe deletion.</summary>
    public static string RenderEvidence(DeadCodeAssessment assessment) =>
        assessment.Reason + (assessment.UsageEvidence.Count == 0 ? "" : "\n" + string.Join('\n', assessment.UsageEvidence));

    private static DeadCodeAssessment Assessment(Symbol symbol, IEnumerable<EntryPoint> entries, IReadOnlyDictionary<string, string> roots,
        HashSet<string> reachable, string? uncertainty, IReadOnlyList<Request> requests, bool sourcePresent)
    {
        var usage = entries.Where(entry => entry.Kind == "http")
            .SelectMany(entry => requests.Where(request => Matches(entry.Display, request)).Select(request =>
                string.Create(CultureInfo.InvariantCulture, $"{request.Path}:{request.Line}: literal request-shaped source call {request.Method} {request.Url} matches {entry.Display}; this is source evidence, not a runtime observation.")))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (roots.TryGetValue(symbol.Id, out var reason))
            return new(symbol.Id, symbol.Path, symbol.Range, DeadCodeState.ProtectedEntryPoint, reason, usage);
        if (reachable.Contains(symbol.Id))
            return new(symbol.Id, symbol.Path, symbol.Range, DeadCodeState.Reachable, "A mapped call path reaches this declaration from an external, public, or framework entry point.", usage);
        if (uncertainty is not null || !sourcePresent || !InternalDeclaration(symbol))
            return new(symbol.Id, symbol.Path, symbol.Range, DeadCodeState.Unknown,
                uncertainty ?? (!sourcePresent ? "The declaration's source is unavailable; usage evidence is incomplete." : "The mapper does not establish an internal declaration with bounded consumers."), usage);
        return new(symbol.Id, symbol.Path, symbol.Range, DeadCodeState.Candidate,
            "No mapped path from the known entry points reaches this internal declaration. This is an unused candidate within the supplied map, not proof that nothing uses it; external, generated, configuration-driven, and unmapped consumers still require review. Automatic removal is disabled.", usage);
    }

    private static string? ProtectedReason(Symbol symbol)
    {
        var declaration = symbol.Signature.Split('\n')[^1];
        if (symbol.Kind is "constructor" or "destructor" || declaration.Contains('[', StringComparison.Ordinal) || declaration.Contains('@', StringComparison.Ordinal))
            return "Constructor, attributed, or decorated code may be invoked by a framework or runtime callback.";
        if (HasPublicDeclaration(declaration))
            return "Public, exported, protected, or virtual declarations may have consumers outside the mapped call graph.";
        if (Languages.FromPath(symbol.Path) is Languages.TypeScript or Languages.JavaScript)
        {
            if (HasPublicDeclaration(symbol.Signature) || symbol.Kind == "method" && !HasPrivateDeclaration(declaration))
                return "Exported declarations and externally accessible JavaScript or TypeScript members may be called by consumers or frameworks.";
        }
        return null;
    }

    private static bool InternalDeclaration(Symbol symbol)
    {
        var declaration = symbol.Signature.Split('\n')[^1];
        return Languages.FromPath(symbol.Path) switch
        {
            Languages.CSharp => HasInternalVisibility(declaration),
            Languages.TypeScript or Languages.JavaScript => symbol.Kind == "function" || HasPrivateDeclaration(declaration),
            _ => false,
        };
    }

    private sealed record Request(string Path, int Line, string Method, string Url);

    private static IReadOnlyList<Request> Requests(IReadOnlyDictionary<string, string> sources)
    {
        var requests = new List<Request>();
        foreach (var (path, source) in sources.Where(pair => Languages.FromPath(pair.Key) is Languages.TypeScript or Languages.JavaScript or Languages.Html or Languages.Razor))
        {
            foreach (Match match in LiteralRequest().Matches(source))
            {
                var tail = match.Groups["tail"].Value.TrimStart();
                if (tail.StartsWith('+')) continue;
                var method = match.Groups["method"].Value.ToUpperInvariant();
                if (method.Length == 0)
                {
                    if (tail.StartsWith(')')) method = "GET";
                    else if (FetchMethod().Match(tail) is { Success: true } option) method = option.Groups["method"].Value.ToUpperInvariant();
                    else if (FetchOptions().Match(tail) is { Success: true } options && !UnknownFetchMethod().IsMatch(options.Value)) method = "GET";
                    else continue;
                }
                requests.Add(new Request(path, source.AsSpan(0, match.Index).Count('\n') + 1, method, match.Groups["url"].Value));
            }
        }
        return requests;
    }

    private static bool Matches(string display, Request request)
    {
        var route = display.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (route.Length != 2 || !string.Equals(route[0], request.Method, StringComparison.OrdinalIgnoreCase)) return false;
        var path = request.Url;
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https") path = absolute.AbsolutePath;
        if (!path.StartsWith('/')) return false;
        path = path.Split('?', '#')[0];
        var expected = route[1].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var actual = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return expected.Length == actual.Length && expected.Zip(actual).All(pair =>
            pair.First == pair.Second || pair.First.StartsWith('{') && pair.First.EndsWith('}') || pair.First.StartsWith(':'));
    }

    private static bool HasPublicDeclaration(string source)
    {
        for (var offset = 0; offset < source.Length;)
            if (NextWord(source, ref offset) is "public" or "protected" or "export" or "virtual" or "override" or "abstract" or "extern") return true;
        return false;
    }

    private static bool HasInternalVisibility(string source)
    {
        for (var offset = 0; offset < source.Length;)
            if (NextWord(source, ref offset) is "private" or "internal") return true;
        return false;
    }

    private static bool HasPrivateDeclaration(string source)
    {
        if (source.AsSpan().TrimStart().StartsWith("#", StringComparison.Ordinal)) return true;
        for (var offset = 0; offset < source.Length;)
            if (NextWord(source, ref offset) is "private") return true;
        return false;
    }

    private static bool HasDynamicInvocation(string source)
    {
        for (var offset = 0; offset < source.Length;)
        {
            var word = NextWord(source, ref offset);
            if (word is "GetMethod" or "GetProperty" or "GetField" or "GetType" or "CreateInstance" or "InvokeMember"
                or "LoadFrom" or "LoadFile" or "InternalsVisibleTo" or "AddScoped" or "AddTransient" or "AddSingleton"
                or "TryAddScoped" or "TryAddTransient" or "TryAddSingleton") return true;
            var tail = source.AsSpan(offset).TrimStart();
            if (word is "require" or "import" or "eval" && tail.StartsWith("(", StringComparison.Ordinal)) return true;
            if (word is "window" or "globalThis" && tail.StartsWith("[", StringComparison.Ordinal)) return true;
            if (word is not "Assembly" || !tail.StartsWith(".", StringComparison.Ordinal)) continue;
            tail = tail[1..].TrimStart();
            if (tail.StartsWith("Load", StringComparison.Ordinal) && (tail.Length == 4 || !IsWordCharacter(tail[4]))) return true;
        }
        return false;
    }

    private static ReadOnlySpan<char> NextWord(string source, ref int offset)
    {
        while (offset < source.Length && !IsWordCharacter(source[offset])) offset++;
        var start = offset;
        while (offset < source.Length && IsWordCharacter(source[offset])) offset++;
        return source.AsSpan(start, offset - start);
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value is '\u200c' or '\u200d'
        || char.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or UnicodeCategory.ConnectorPunctuation;

    [GeneratedRegex("(?:\\bfetch|\\b(?:[A-Za-z_$][\\w$]*\\.)+(?<method>get|post|put|patch|delete|head|options))\\s*\\(\\s*(?:\"(?<url>[^\"\\r\\n]*)\"|'(?<url>[^'\\r\\n]*)')(?<tail>[^;\\r\\n]{0,500})", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex LiteralRequest();

    [GeneratedRegex("\\bmethod\\s*:\\s*['\"](?<method>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)['\"]", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FetchMethod();

    [GeneratedRegex("^,\\s*\\{[^;]*\\}\\s*\\)", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex FetchOptions();

    [GeneratedRegex("\\b(?:method|__proto__)\\b|\\.\\.\\.|\\[|\\\\", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnknownFetchMethod();
}
