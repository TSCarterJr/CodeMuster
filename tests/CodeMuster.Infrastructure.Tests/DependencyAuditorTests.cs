using CodeMuster.Domain;
using CodeMuster.Infrastructure.Audits;

namespace CodeMuster.Infrastructure.Tests;

public class DependencyAuditorTests
{
    private const string Root = "/repo";

    private readonly List<(string File, string Arguments, string WorkingDirectory)> _calls = [];
    private readonly Dictionary<string, (int Code, string Output)> _replies = new(StringComparer.Ordinal);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "audits", name));

    private DependencyAuditor Auditor() => new((file, arguments, directory, _) =>
    {
        _calls.Add((file, string.Join(' ', arguments), directory));
        var key = Path.GetFileNameWithoutExtension(file);
        return Task.FromResult(_replies.TryGetValue(key, out var reply)
            ? new ProcessResult(reply.Code, reply.Output, "")
            : new ProcessResult(127, "", $"{file} not found"));
    });

    [Fact]
    public async Task Npm_lockfile_is_audited_in_its_own_folder()
    {
        _replies["npm"] = (1, Fixture("npm-audit.json"));

        var audit = await Auditor().AuditAsync(Root, ["web/package-lock.json", "web/package.json", "src/A.cs"], null, CancellationToken.None);

        var manifest = Assert.Single(audit.Manifests);
        Assert.Equal("web/package.json", manifest.Manifest);
        Assert.Equal("npm audit", manifest.Tool);
        Assert.Equal(7, manifest.Packages.Count);
        Assert.Contains(manifest.Packages, package => package.Severity == Severity.Critical && package.Package == "next");
        var call = Assert.Single(_calls);
        Assert.Equal("audit --json", call.Arguments);
        Assert.EndsWith("web", call.WorkingDirectory.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Empty(audit.Diagnostics);
    }

    [Fact]
    public async Task Each_ecosystem_uses_its_own_tool()
    {
        _replies["pnpm"] = (1, Fixture("pnpm-audit.json"));
        _replies["yarn"] = (1, Fixture("yarn-audit.json"));
        _replies["dotnet"] = (0, Fixture("dotnet-vulnerable.json"));

        var audit = await Auditor().AuditAsync(
            Root,
            ["app/pnpm-lock.yaml", "app/package.json", "site/yarn.lock", "site/package.json", "api/Api.slnx"],
            null,
            CancellationToken.None);

        Assert.Equal(
            ["api/Api.slnx", "app/package.json", "site/package.json"],
            audit.Manifests.Select(m => m.Manifest).Order(StringComparer.Ordinal));
        Assert.Equal(["dotnet list package", "pnpm audit", "yarn audit"], audit.Manifests.Select(m => m.Tool).Order(StringComparer.Ordinal));
        Assert.Equal(3, audit.Manifests.Single(m => m.Tool == "dotnet list package").Packages.Count);
    }

    [Fact]
    public async Task A_missing_tool_becomes_a_diagnostic_and_leaves_the_rest_alone()
    {
        _replies["npm"] = (1, Fixture("npm-audit.json"));

        var audit = await Auditor().AuditAsync(
            Root,
            ["web/package-lock.json", "web/package.json", "app/pnpm-lock.yaml", "app/package.json"],
            null,
            CancellationToken.None);

        Assert.Equal(["web/package.json"], audit.Manifests.Select(m => m.Manifest));
        var diagnostic = Assert.Single(audit.Diagnostics);
        Assert.Contains("app/package.json", diagnostic, StringComparison.Ordinal);
        Assert.Contains("pnpm", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreadable_output_becomes_a_diagnostic_rather_than_an_exception()
    {
        _replies["npm"] = (1, "not json");

        var audit = await Auditor().AuditAsync(Root, ["web/package-lock.json", "web/package.json"], null, CancellationToken.None);

        Assert.Empty(audit.Manifests);
        Assert.Contains("web/package.json", Assert.Single(audit.Diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_any_manifest_nothing_runs()
    {
        var audit = await Auditor().AuditAsync(Root, ["src/A.cs", "README.md"], null, CancellationToken.None);

        Assert.Empty(audit.Manifests);
        Assert.Empty(audit.Diagnostics);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task A_solution_is_audited_once_rather_than_every_project()
    {
        _replies["dotnet"] = (0, Fixture("dotnet-vulnerable.json"));

        var audit = await Auditor().AuditAsync(
            Root,
            ["api/Api.slnx", "api/src/One/One.csproj", "api/src/Two/Two.csproj"],
            null,
            CancellationToken.None);

        Assert.Equal(["api/Api.slnx"], audit.Manifests.Select(m => m.Manifest));
        var call = Assert.Single(_calls);
        Assert.Equal("list api/Api.slnx package --vulnerable --include-transitive --format json", call.Arguments);
    }

    [Fact]
    public async Task Progress_names_each_manifest_as_it_starts()
    {
        _replies["npm"] = (1, Fixture("npm-audit.json"));
        var notes = new List<string>();

        await Auditor().AuditAsync(Root, ["web/package-lock.json", "web/package.json"], new ListProgress(notes), CancellationToken.None);

        Assert.Equal(["auditing web/package.json with npm"], notes);
    }

    private sealed class ListProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }
}
