using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodeMuster.Cli.Tests;

public class ApplicationReviewTests
{
    private const string Target = "web/invoice.html";
    private const string SyntheticPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jrWQAAAAASUVORK5CYII=";

    [Fact]
    public async Task InitLeavesNewReviewsOffAndPreservesConfiguredChoices()
    {
        using var repo = CreateRepo();
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        var configPath = Path.Combine(repo.Root, ".codemuster", "config.json");
        var config = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
        Assert.False(config["dead_code"]!.GetValue<bool>());
        Assert.False(config["user_experience"]!["enabled"]!.GetValue<bool>());

        config["automation"] = "off";
        config["user_experience"]!["enabled"] = true;
        config["vulnerabilities"] = false;
        var saved = config.ToJsonString();
        await File.WriteAllTextAsync(configPath, saved);

        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        Assert.Equal(saved, await File.ReadAllTextAsync(configPath));
    }

    [Fact]
    public async Task ConfiguredScanQueuesOnlyUiAndNextCarriesTheBrowserContract()
    {
        using var repo = CreateRepo();
        await InitializeAsync(repo);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);

        var next = await CliProcess.RunAsync(repo.Root, "next", "--kind", "ux", "--batch", "100");

        Assert.Equal(0, next.ExitCode);
        Assert.Equal(["web/invoice.html", "web/schedule.html"], Regex.Matches(next.Stdout, @"^- key: (.+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value.TrimEnd('\r')));
        Assert.DoesNotContain("- key: api/InvoiceService.cs", next.Stdout);
        Assert.Contains("artifact_sha256", next.Stdout);
        Assert.Contains("runtime_source_evidence", next.Stdout);
        Assert.Contains("contrast_samples", next.Stdout);
        Assert.Contains("expected_location", next.Stdout);
    }

    [Fact]
    public async Task SourceOnlyDoneAndHeadlessRunCannotClaimUxCompletion()
    {
        using var repo = CreateRepo();
        await InitializeAsync(repo);
        var (unit, fingerprint) = await ScanAndNextAsync(repo);
        var responsePath = Path.Combine(repo.Root, "source-only.json");
        await File.WriteAllTextAsync(responsePath, """{"summary":"Source looks fine","findings":[]}""");

        var done = await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", fingerprint, "--findings", responsePath);
        var run = await CliProcess.RunAsync(repo.Root, "run", "--kind", "ux", "--agent", "fake", "--attempts", "1");

        Assert.NotEqual(0, done.ExitCode);
        Assert.NotEqual(0, run.ExitCode);
        var status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Contains("ux 0/2", status.Stdout);
        Assert.Contains("incomplete", status.Stdout);
    }

    [Fact]
    public async Task ValidSyntheticReceiptPersistsEvidenceAndEmitsBothDefectsWithoutSubmittedFindings()
    {
        using var repo = CreateRepo();
        await InitializeAsync(repo);
        var (unit, fingerprint) = await ScanAndNextAsync(repo);
        var response = await WriteReceiptAsync(repo, fingerprint);

        var done = await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", fingerprint, "--findings", response);

        Assert.True(done.ExitCode == 0, done.Stdout + done.Stderr);
        Assert.Contains("recorded 2 finding(s)", done.Stdout);
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Equal(0, report.ExitCode);
        Assert.Contains("insufficient text contrast", report.Stdout);
        Assert.Contains("Charge Customer", report.Stdout);
        Assert.Contains("Invoice beside the outstanding balance", report.Stdout);
        Assert.Contains("2.32:1", report.Stdout);
        Assert.Contains("artifact_sha256", report.Stdout);
        Assert.Contains("Synthetic contract fixture", report.Stdout);
        Assert.Contains("ux 1/2", (await CliProcess.RunAsync(repo.Root, "status")).Stdout);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("not_image")]
    public async Task DoneRejectsMissingChangedOrNonImageArtifacts(string fault)
    {
        using var repo = CreateRepo();
        await InitializeAsync(repo);
        var (unit, fingerprint) = await ScanAndNextAsync(repo);
        var response = await WriteReceiptAsync(repo, fingerprint);
        var artifact = Path.Combine(repo.Root, ".codemuster", "evidence", "synthetic.png");
        if (fault == "missing") File.Delete(artifact);
        else if (fault == "changed") await File.AppendAllTextAsync(artifact, "changed bytes");
        else
        {
            await File.WriteAllTextAsync(artifact, "This is deliberately not an image.");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(response))!;
            json["ux_review"]!["pages"]![0]!["artifact_sha256"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(artifact)));
            await File.WriteAllTextAsync(response, json.ToJsonString());
        }

        var done = await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", fingerprint, "--findings", response);

        Assert.NotEqual(0, done.ExitCode);
        Assert.Contains("ux 0/2", (await CliProcess.RunAsync(repo.Root, "status")).Stdout);
    }

    [Fact]
    public async Task SourceChangeAndRescanInvalidateRecordedBrowserEvidence()
    {
        using var repo = CreateRepo();
        await InitializeAsync(repo);
        var (unit, fingerprint) = await ScanAndNextAsync(repo);
        var response = await WriteReceiptAsync(repo, fingerprint);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", fingerprint, "--findings", response)).ExitCode);
        await File.AppendAllTextAsync(Path.Combine(repo.Root, "web", "schedule.html"), "\n<p>Changed sibling workflow</p>\n");

        var (sameUnit, changedFingerprint) = await ScanAndNextAsync(repo);

        Assert.Equal(unit, sameUnit);
        Assert.NotEqual(fingerprint, changedFingerprint);
        var status = await CliProcess.RunAsync(repo.Root, "status");
        Assert.Contains("ux 0/2", status.Stdout);
        Assert.Contains("stale", status.Stdout);
        var report = await CliProcess.RunAsync(repo.Root, "report");
        Assert.Contains("Historical", report.Stdout);
    }

    [Fact]
    public async Task DoneRejectsSourceChangesAfterThePackWithoutRequiringAnotherScan()
    {
        using var repo = CreateRepo();
        await InitializeAsync(repo);
        var (unit, fingerprint) = await ScanAndNextAsync(repo);
        var response = await WriteReceiptAsync(repo, fingerprint);
        await File.AppendAllTextAsync(Path.Combine(repo.Root, "web", "schedule.html"), "\n<p>Changed sibling workflow</p>\n");

        var done = await CliProcess.RunAsync(repo.Root, "done", unit, "--fingerprint", fingerprint, "--findings", response);

        Assert.NotEqual(0, done.ExitCode);
        Assert.Contains("ux 0/2", (await CliProcess.RunAsync(repo.Root, "status")).Stdout);
    }

    [Fact]
    public async Task BackendOnlyRepositoryReportsUxNotApplicable()
    {
        using var repo = CreateRepo(ui: false);
        await InitializeAsync(repo);
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);

        var next = await CliProcess.RunAsync(repo.Root, "next", "--kind", "ux");
        var status = await CliProcess.RunAsync(repo.Root, "status");

        Assert.Equal(0, next.ExitCode);
        Assert.Empty(next.Stdout);
        Assert.Contains("UX: not applicable", status.Stdout);
    }

    private static TempRepo CreateRepo(bool ui = true)
    {
        var repo = new TempRepo();
        Directory.CreateDirectory(Path.Combine(repo.Root, "api"));
        File.WriteAllText(Path.Combine(repo.Root, ".gitignore"), ".codemuster/ledger.db\n.codemuster/ledger.db-*\n");
        File.WriteAllText(Path.Combine(repo.Root, "api", "InvoiceService.cs"), "public class InvoiceService { public int Balance() => 450; }\n");
        repo.Git("init", "-q");
        repo.Git("config", "core.autocrlf", "false");
        repo.Git("add", ".gitignore", "api/InvoiceService.cs");
        if (ui)
        {
            Directory.CreateDirectory(Path.Combine(repo.Root, "web"));
            File.WriteAllText(Path.Combine(repo.Root, "web", "invoice.html"), "<!doctype html>\n<style>p { color:#aaa; background:white; }</style>\n<h1>Invoice</h1><p>Outstanding balance: $450</p>\n");
            File.WriteAllText(Path.Combine(repo.Root, "web", "schedule.html"), "<!doctype html>\n<h1>Schedule</h1>\n<button>Charge Customer</button>\n");
            repo.Git("add", "web/invoice.html", "web/schedule.html");
        }
        repo.Git("-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "synthetic UX contract fixture");
        return repo;
    }

    private static async Task InitializeAsync(TempRepo repo)
    {
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "init", "--yes", "--no-skills")).ExitCode);
        var path = Path.Combine(repo.Root, ".codemuster", "config.json");
        var config = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        config["vulnerabilities"] = false;
        config["verify"] = false;
        config["user_experience"] = new JsonObject { ["enabled"] = true, ["base_url"] = "http://127.0.0.1:43210" };
        await File.WriteAllTextAsync(path, config.ToJsonString());
    }

    private static async Task<(string Unit, string Fingerprint)> ScanAndNextAsync(TempRepo repo)
    {
        Assert.Equal(0, (await CliProcess.RunAsync(repo.Root, "scan", "--mode", "file")).ExitCode);
        var next = await CliProcess.RunAsync(repo.Root, "next", "--kind", "ux", "--path", Target);
        Assert.Equal(0, next.ExitCode);
        var fingerprint = Regex.Match(next.Stdout, @"^- fingerprint: ([0-9a-f]{64})", RegexOptions.Multiline).Groups[1].Value;
        Assert.Equal(64, fingerprint.Length);
        return ("ux:" + Target, fingerprint);
    }

    private static async Task<string> WriteReceiptAsync(TempRepo repo, string fingerprint)
    {
        var artifact = Path.Combine(repo.Root, ".codemuster", "evidence", "synthetic.png");
        Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
        var bytes = Convert.FromBase64String(SyntheticPng);
        await File.WriteAllBytesAsync(artifact, bytes);
        var response = new
        {
            summary = "Synthetic contract fixture: verifies ingestion, not actual browser use.",
            findings = Array.Empty<object>(),
            ux_review = new
            {
                fingerprint,
                status = "complete",
                runtime_url = "http://127.0.0.1:43210",
                runtime_source_evidence = "Synthetic contract fixture; this automated test does not claim browser inspection.",
                pages = new[] { new {
                    source_paths = new[] { Target }, route = "/invoices/42", state = "Unpaid completed sample job", theme = "light",
                    viewport = new { width = 1, height = 1 }, artifact = ".codemuster/evidence/synthetic.png", artifact_sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    experience_checks = new[] { "flow", "validation_recovery", "graphics", "interaction_feedback", "text_quality" }.Select(area => new { area, source_path = Target, line_start = 2, line_end = 3, assessment = "pass", evidence = "Synthetic contract observation for " + area + "; this test does not claim live inspection.", basis = "observed" }).ToArray(),
                    readability = new {
                        visual_inspection = "Synthetic observation of faint balance text.", font_and_spacing = "Synthetic 16px regular text.", overlays_and_states = "Static fixture has no overlays or asynchronous states.",
                        contrast_samples = new[] { new { source_path = Target, line_start = 2, line_end = 2, target = "Outstanding balance", foreground = "#aaaaaa", background = "#ffffff", font_size_px = 16, font_weight = 400, contrast_ratio = 2.32, assessment = "fail" } }
                    },
                    workflow = new {
                        task = "Collect payment for an invoice", steps = new[] { "Open invoice and inspect available actions.", "Open Schedule and inspect Charge Customer." }, result = "Invoice lacks the primary payment action.",
                        actions = new[] { new { source_path = Target, line_start = 3, line_end = 3, action = "Charge Customer", expected_location = "Invoice beside the outstanding balance", observed_locations = new[] { "Schedule" }, assessment = "misplaced", basis = "observed", evidence = "Synthetic observation: invoice has a balance but payment action exists only on Schedule." } }
                    }
                } }
            }
        };
        var path = Path.Combine(repo.Root, "ux-response.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(response));
        return path;
    }
}
