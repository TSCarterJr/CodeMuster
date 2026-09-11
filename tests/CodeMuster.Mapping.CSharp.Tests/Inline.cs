using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMuster.Mapping.CSharp.Tests;

internal static class Inline
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "codemuster-inline");

    private static readonly IReadOnlyList<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("System.", StringComparison.Ordinal) || name is "netstandard.dll" or "mscorlib.dll"))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();

    public static Task<CodeMap> MapAsync(string source) => MapAsync([("src/Code.cs", source)]);

    public static async Task<CodeMap> MapAsync((string Path, string Source)[] files, IReadOnlyList<string>? included = null)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution
            .AddProject("Inline", "Inline", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
            .WithMetadataReferences(References);
        foreach (var (path, source) in files)
        {
            project = project.AddDocument(System.IO.Path.GetFileName(path), source, filePath: System.IO.Path.Combine(Root, path)).Project;
        }

        var builder = new CodeMapBuilder(Root, included ?? files.Select(file => file.Path).ToList());
        await builder.AddAsync(project.Solution, null, CancellationToken.None);
        return builder.Build();
    }
}
