using CodeMuster.Domain;
using CodeMuster.Infrastructure.Audits;

namespace CodeMuster.Infrastructure.Tests;

public class DependencyAuditorTests
{
    [Fact]
    public async Task YarnBerryUsesItsNpmAuditCommand()
    {
        var calls = new List<string>();
        var auditor = new DependencyAuditor((_, args, _, _) =>
        {
            calls.Add(string.Join(' ', args));
            return Task.FromResult(new ProcessResult(0, args[0] == "--version" ? "4.9.0" : "{\"advisories\":{}}", ""));
        });
        var result = await auditor.AuditAsync(Root, ["yarn.lock", "package.json"], null, CancellationToken.None);
        Assert.Contains("npm audit --all --recursive --json", calls);
        Assert.Single(result.Manifests);
    }

    private const string Root = "/repo";

    private readonly List<(string File, string Arguments, string WorkingDirectory)> _calls = [];
    private readonly Dictionary<string, (int Code, string Output)> _replies = new(StringComparer.Ordinal);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "audits", name));

    private DependencyAuditor Auditor() => new((file, arguments, directory, _) =>
    {
        _calls.Add((file, string.Join(' ', arguments), directory));
        var key = Path.GetFileNameWithoutExtension(file);
        if (key == "yarn" && arguments[0] == "--version") return Task.FromResult(new ProcessResult(0, "1.22.22", ""));
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
    public async Task Plain_text_instead_of_a_json_report_is_quoted_by_its_first_line()
    {
        // .NET 10's dotnet list package writes its restore errors to stdout as text when a NuGet source cannot be reached.
        var auditor = new DependencyAuditor((_, _, _, _) => Task.FromResult(new ProcessResult(
            1,
            "error: Unable to load the service index for source https://127.0.0.1:9/v3/index.json.\nerror: No connection could be made because the target machine actively refused it. (127.0.0.1:9)\n",
            "")));

        var audit = await auditor.AuditAsync(Root, ["App/App.csproj"], null, CancellationToken.None);

        Assert.Empty(audit.Manifests);
        Assert.Equal(
            "App/App.csproj: dotnet list package gave no usable report (error: Unable to load the service index for source https://127.0.0.1:9/v3/index.json.); any earlier findings are kept, run it by hand to see why",
            Assert.Single(audit.Diagnostics));
    }

    [Fact]
    public async Task A_yarn_classic_error_on_stderr_is_the_reason_when_stdout_holds_no_report()
    {
        var auditor = new DependencyAuditor((_, arguments, _, _) => Task.FromResult(arguments[0] == "--version"
            ? new ProcessResult(0, "1.22.22", "")
            : new ProcessResult(
                1,
                Fixture("yarn-offline.json"),
                "{\"type\":\"error\",\"data\":\"Error: https://registry.yarnpkg.com/-/npm/v1/security/audits: tunneling socket could not be established, cause=connect ECONNREFUSED 127.0.0.1:9\\n    at ClientRequest.onError\"}\n")));

        var audit = await auditor.AuditAsync(Root, ["package.json", "yarn.lock"], null, CancellationToken.None);

        Assert.Empty(audit.Manifests);
        Assert.Equal(
            "package.json: yarn audit gave no usable report (Error: https://registry.yarnpkg.com/-/npm/v1/security/audits: tunneling socket could not be established, cause=connect ECONNREFUSED 127.0.0.1:9); any earlier findings are kept, run it by hand to see why",
            Assert.Single(audit.Diagnostics));
    }

    [Theory]
    [InlineData("4.9.2", true)]
    [InlineData("1.22.22", false)]
    public async Task Yarn_berry_writing_nothing_with_exit_0_is_a_clean_report_but_other_tools_are_not(string version, bool clean)
    {
        // Yarn 2 and later print "No audit suggestions" only as an info line, which --json leaves out, so a clean audit is empty.
        var auditor = new DependencyAuditor((_, arguments, _, _) => Task.FromResult(arguments[0] == "--version"
            ? new ProcessResult(0, version, "")
            : new ProcessResult(0, "", "")));

        var audit = await auditor.AuditAsync(Root, ["package.json", "yarn.lock"], null, CancellationToken.None);

        if (clean)
        {
            var manifest = Assert.Single(audit.Manifests);
            Assert.Equal(("package.json", "yarn npm audit"), (manifest.Manifest, manifest.Tool));
            Assert.Empty(manifest.Packages);
            Assert.Empty(audit.Diagnostics);
        }
        else
        {
            Assert.Empty(audit.Manifests);
            Assert.StartsWith("package.json: yarn audit wrote nothing (exit 0)", Assert.Single(audit.Diagnostics), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_error_envelope_becomes_a_diagnostic_rather_than_a_clean_manifest()
    {
        _replies["npm"] = (1, Fixture("npm-offline.json"));

        var audit = await Auditor().AuditAsync(Root, ["web/package-lock.json", "web/package.json"], null, CancellationToken.None);

        Assert.Empty(audit.Manifests);
        var diagnostic = Assert.Single(audit.Diagnostics);
        Assert.StartsWith("web/package.json: npm audit ", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ECONNREFUSED 127.0.0.1:9", diagnostic, StringComparison.Ordinal);
        Assert.Contains("earlier findings are kept", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_lockfiles_in_one_folder_audit_once_with_pnpm_and_say_so()
    {
        _replies["npm"] = (1, Fixture("npm-audit.json"));
        _replies["pnpm"] = (1, Fixture("pnpm-audit.json"));

        var audit = await Auditor().AuditAsync(Root, ["web/package-lock.json", "web/package.json", "web/pnpm-lock.yaml"], null, CancellationToken.None);

        var manifest = Assert.Single(audit.Manifests);
        Assert.Equal("web/package.json", manifest.Manifest);
        Assert.Equal("pnpm audit", manifest.Tool);
        Assert.Equal("pnpm", Path.GetFileNameWithoutExtension(Assert.Single(_calls).File));
        var warning = Assert.Single(audit.Diagnostics);
        Assert.StartsWith("web/package.json: found package-lock.json and pnpm-lock.yaml; audited with pnpm", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Three_lockfiles_prefer_pnpm_then_yarn_then_npm()
    {
        _replies["npm"] = (1, Fixture("npm-audit.json"));
        _replies["pnpm"] = (1, Fixture("pnpm-audit.json"));
        _replies["yarn"] = (1, Fixture("yarn-audit.json"));

        var audit = await Auditor().AuditAsync(
            Root,
            ["web/package.json", "web/package-lock.json", "web/pnpm-lock.yaml", "web/yarn.lock", "site/package.json", "site/package-lock.json", "site/yarn.lock"],
            null,
            CancellationToken.None);

        Assert.Equal(
            [("site/package.json", "yarn audit"), ("web/package.json", "pnpm audit")],
            audit.Manifests.OrderBy(m => m.Manifest, StringComparer.Ordinal).Select(m => (m.Manifest, m.Tool)));
        Assert.Contains(audit.Diagnostics, d => d.StartsWith("web/package.json: found package-lock.json, pnpm-lock.yaml and yarn.lock; audited with pnpm", StringComparison.Ordinal));
        Assert.Contains(audit.Diagnostics, d => d.StartsWith("site/package.json: found package-lock.json and yarn.lock; audited with yarn", StringComparison.Ordinal));
        Assert.Equal(2, audit.Diagnostics.Count);
    }

    [Fact]
    public async Task Npm_lockfile_and_shrinkwrap_are_one_npm_audit_without_a_warning()
    {
        _replies["npm"] = (1, Fixture("npm-audit.json"));

        var audit = await Auditor().AuditAsync(Root, ["package.json", "package-lock.json", "npm-shrinkwrap.json"], null, CancellationToken.None);

        Assert.Equal("npm audit", Assert.Single(audit.Manifests).Tool);
        Assert.Single(_calls);
        Assert.Empty(audit.Diagnostics);
    }

    [Theory]
    [InlineData("npm@10.9.0", "npm audit", "as packageManager names it")]
    [InlineData("yarn@4.9.2+sha224.953c8233f7a92884eee2de69a1b92d1f2ec1655e66d08071ba9a02fa", "yarn npm audit", "as packageManager names it")]
    [InlineData("bun@1.2.0", "pnpm audit", "the first of pnpm, yarn and npm")]
    public async Task PackageManager_picks_the_tool_among_its_lockfiles(string packageManager, string tool, string reason)
    {
        var root = Path.Combine(Path.GetTempPath(), "codemuster-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "web"));
        try
        {
            File.WriteAllText(Path.Combine(root, "web", "package.json"), $$"""{"name": "web", "packageManager": "{{packageManager}}"}""");
            _replies["npm"] = (1, Fixture("npm-audit.json"));
            _replies["pnpm"] = (1, Fixture("pnpm-audit.json"));
            var auditor = new DependencyAuditor((file, arguments, directory, _) =>
            {
                _calls.Add((file, string.Join(' ', arguments), directory));
                return Task.FromResult(Path.GetFileNameWithoutExtension(file) switch
                {
                    "yarn" when arguments[0] == "--version" => new ProcessResult(0, "4.9.2", ""),
                    "yarn" => new ProcessResult(1, Fixture("yarn-berry.json"), ""),
                    var key => new ProcessResult(1, _replies[key].Output, ""),
                });
            });

            var audit = await auditor.AuditAsync(root, ["web/package.json", "web/package-lock.json", "web/pnpm-lock.yaml", "web/yarn.lock"], null, CancellationToken.None);

            Assert.Equal(tool, Assert.Single(audit.Manifests).Tool);
            Assert.All(_calls, call => Assert.EndsWith("web", call.WorkingDirectory.Replace('\\', '/').TrimEnd('/'), StringComparison.Ordinal));
            Assert.Contains(reason, Assert.Single(audit.Diagnostics), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
