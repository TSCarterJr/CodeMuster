using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Validates recorded browser observations before a UI unit can count as reviewed.</summary>
public static class UxEvidence
{
    /// <summary>Additional response property required for a completed UI review; replace every example with observed evidence.</summary>
    public const string Sample = """
        "ux_review": {
          "fingerprint": "COPY THE CURRENT UNIT FINGERPRINT",
          "runtime_url": "http://localhost:3000",
          "runtime_source_evidence": "Describe how the running app was started or rebuilt from this checkout and checked against the current source; echoing the fingerprint alone is not runtime evidence.",
          "status": "complete",
          "pages": [{
            "source_paths": ["web/Invoice.tsx"],
            "route": "/invoices/42",
            "state": "open invoice, billing manager",
            "theme": "light",
            "viewport": {"width": 1280, "height": 800},
            "artifact": ".codemuster/evidence/invoice.png",
            "artifact_sha256": "REPLACE WITH THE ACTUAL 64-HEX SHA256 OF THE SCREENSHOT FILE",
            "experience_checks": [
              {"area":"flow","source_path":"web/Invoice.tsx","line_start":1,"line_end":2,"assessment":"pass","evidence":"Describe whether the goal, sequence, required context and recovery make sense, even if every button technically works.","basis":"observed"},
              {"area":"validation_recovery","source_path":"web/Invoice.tsx","line_start":1,"line_end":2,"assessment":"pass","evidence":"Describe actual invalid input, error feedback, retained values and correction/retry behavior.","basis":"observed"},
              {"area":"graphics","source_path":"web/Invoice.tsx","line_start":1,"line_end":2,"assessment":"pass","evidence":"Describe inspected rendering, clipping, overlaps, images and layout across relevant states.","basis":"observed"},
              {"area":"interaction_feedback","source_path":"web/Invoice.tsx","line_start":1,"line_end":2,"assessment":"pass","evidence":"Describe actual click/pressed/focus feedback and busy/loading feedback for asynchronous actions.","basis":"observed"},
              {"area":"text_quality","source_path":"web/Invoice.tsx","line_start":1,"line_end":2,"assessment":"pass","evidence":"Describe inspected spelling, wording, labels, instructions and consistency of business terminology.","basis":"observed"}
            ],
            "readability": {
              "visual_inspection": "Describe the rendered labels, amounts and controls actually inspected.",
              "font_and_spacing": "Describe actual font sizes, spacing, clipping and readability.",
              "overlays_and_states": "Describe inspected dialogs, errors, loading and empty states, or why a state cannot apply.",
              "contrast_samples": [{
                "source_path": "web/Invoice.tsx", "line_start": 10, "line_end": 10,
                "target": "Outstanding balance",
                "foreground": "#000000", "background": "#ffffff",
                "font_size_px": 16, "font_weight": 400,
                "contrast_ratio": 21, "assessment": "pass"
              }]
            },
            "workflow": {
              "task": "Collect payment for an outstanding invoice",
              "steps": ["Open the invoice and review the balance.", "Open payment collection and check the amount without submitting a real charge."],
              "result": "Describe what happened and whether the task made sense.",
              "actions": [{
                "source_path": "web/Invoice.tsx", "line_start": 20, "line_end": 30,
                "action": "Charge Customer",
                "expected_location": "Invoice beside the balance",
                "observed_locations": ["Invoice beside the balance", "Schedule shortcut"],
                "assessment": "appropriate", "basis": "observed",
                "evidence": "Explain expected versus available placement and relevant permissions and business state."
              }]
            }
          }]
        }
        """;

    /// <summary>Returns canonical evidence JSON, or rejects absent, stale, blocked or incomplete browser evidence.</summary>
    public static string ValidateAndSerialize(string responseJson, string fingerprint, IReadOnlyList<string> uiTargets)
    {
        using var document = JsonDocument.Parse(responseJson);
        var review = Required(document.RootElement, "ux_review", JsonValueKind.Object);
        if (Text(review, "fingerprint") != fingerprint) throw new JsonException("ux_review fingerprint does not match the current unit; inspect the current source and application again");
        var status = Text(review, "status");
        if (status != "complete")
        {
            var reason = review.TryGetProperty("reason", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            throw new JsonException($"ux_review is {status}; browser review remains incomplete{(string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason)}");
        }

        var runtimeUrl = Text(review, "runtime_url");
        if (!Uri.TryCreate(runtimeUrl, UriKind.Absolute, out var runtime) || runtime.Scheme is not ("http" or "https"))
            throw new JsonException("ux_review runtime_url must be an absolute HTTP(S) application URL");
        Text(review, "runtime_source_evidence");

        var targets = uiTargets.Select(RepoPath.Normalize).ToHashSet(StringComparer.Ordinal);
        if (targets.Count == 0) throw new JsonException("ux_review has no applicable UI targets");
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in Items(review, "pages"))
        {
            var paths = Strings(page, "source_paths");
            foreach (var path in paths)
            {
                RelativePath(path, "source_paths");
                if (!targets.Contains(RepoPath.Normalize(path))) throw new JsonException($"ux_review source path {path} is not an applicable UI target");
                covered.Add(RepoPath.Normalize(path));
            }

            var route = Text(page, "route");
            if (!(route.StartsWith('/') && !route.StartsWith("//", StringComparison.Ordinal))
                && !(Uri.TryCreate(route, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                throw new JsonException("ux_review route must be an application route or an HTTP(S) URL");
            Text(page, "state");
            Text(page, "theme");
            var artifact = Text(page, "artifact");
            RelativePath(artifact, "artifact");
            if (Path.GetExtension(artifact).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                throw new JsonException("ux_review artifact must reference a PNG, JPEG or WebP screenshot");
            var hash = Text(page, "artifact_sha256");
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))
                throw new JsonException("ux_review artifact_sha256 must contain the screenshot's 64 hexadecimal SHA256 digits");
            var viewport = Required(page, "viewport", JsonValueKind.Object);
            PositiveInteger(viewport, "width");
            PositiveInteger(viewport, "height");

            var readability = Required(page, "readability", JsonValueKind.Object);
            Text(readability, "visual_inspection");
            Text(readability, "font_and_spacing");
            Text(readability, "overlays_and_states");
            foreach (var sample in Items(readability, "contrast_samples"))
            {
                Location(sample, paths);
                Text(sample, "target");
                var (ratio, threshold) = Contrast(sample);
                var reported = Number(sample, "contrast_ratio");
                if (Math.Abs(ratio - reported) > 0.05) throw new JsonException("ux_review contrast_ratio does not match the supplied composited foreground and background colors");
                var expected = ratio >= threshold ? "pass" : "fail";
                if (Text(sample, "assessment") != expected) throw new JsonException($"ux_review contrast assessment must be {expected} for the measured text");
            }

            var workflow = Required(page, "workflow", JsonValueKind.Object);
            Text(workflow, "task");
            Text(workflow, "result");
            if (Strings(workflow, "steps").Count < 2) throw new JsonException("ux_review workflow steps must include at least two observed steps through a task");
            foreach (var action in Items(workflow, "actions"))
            {
                Location(action, paths);
                Text(action, "action");
                Text(action, "expected_location");
                Text(action, "evidence");
                var assessment = Text(action, "assessment");
                if (assessment is not ("appropriate" or "missing" or "misplaced")) throw new JsonException("ux_review action assessment must be appropriate, missing or misplaced");
                if (Text(action, "basis") is not ("observed" or "recommendation")) throw new JsonException("ux_review action basis must be observed or recommendation");
                Strings(action, "observed_locations", assessment == "missing");
            }

            string[] requiredAreas = ["flow", "validation_recovery", "graphics", "interaction_feedback", "text_quality"];
            var assessedAreas = new HashSet<string>(StringComparer.Ordinal);
            foreach (var check in Items(page, "experience_checks"))
            {
                Location(check, paths);
                var area = Text(check, "area");
                if (!requiredAreas.Contains(area, StringComparer.Ordinal)) throw new JsonException("ux_review experience_checks area must be flow, validation_recovery, graphics, interaction_feedback or text_quality");
                assessedAreas.Add(area);
                Text(check, "evidence");
                var assessment = Text(check, "assessment");
                if (assessment is not ("pass" or "issue" or "not_applicable")) throw new JsonException("ux_review experience_checks assessment must be pass, issue or not_applicable");
                if (Text(check, "basis") is not ("observed" or "recommendation")) throw new JsonException("ux_review experience_checks basis must be observed or recommendation");
                if (assessment == "not_applicable")
                {
                    if (area is "flow" or "graphics") throw new JsonException($"ux_review {area} must be inspected for every UI target");
                    Text(check, "reason");
                }
            }

            var missingAreas = requiredAreas.Except(assessedAreas).ToList();
            if (missingAreas.Count > 0) throw new JsonException("ux_review experience_checks did not assess: " + string.Join(", ", missingAreas));
        }

        var missing = targets.Except(covered).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0) throw new JsonException("ux_review did not inspect UI target(s): " + string.Join(", ", missing));
        return JsonSerializer.Serialize(review, DomainJson.Options);
    }

    /// <summary>Produces defects for every recorded contrast failure so an empty model findings list cannot hide them.</summary>
    public static IReadOnlyList<Finding> MeasuredFindings(string canonicalJson, string targetPath)
    {
        using var document = JsonDocument.Parse(canonicalJson);
        var findings = new List<Finding>();
        foreach (var page in Items(document.RootElement, "pages"))
        {
            foreach (var sample in Items(Required(page, "readability", JsonValueKind.Object), "contrast_samples"))
            {
                if (Text(sample, "assessment") != "fail" || RepoPath.Normalize(Text(sample, "source_path")) != RepoPath.Normalize(targetPath)) continue;
                var (ratio, threshold) = Contrast(sample);
                var target = Text(sample, "target");
                findings.Add(NewFinding(sample, "ux_readability", $"{target} has insufficient text contrast.",
                    string.Create(CultureInfo.InvariantCulture, $"Measured {ratio:0.00}:1; required {threshold:0.##}:1 for {Number(sample, "font_size_px"):0.##}px text at weight {Number(sample, "font_weight"):0}. Foreground {Text(sample, "foreground")}, background {Text(sample, "background")}. {PageContext(page)}"), 1));
            }
        }

        return findings.Distinct().ToList();
    }

    /// <summary>Produces observed workflow defects or explicitly classified recommendations from recorded placement checks.</summary>
    public static IReadOnlyList<Finding> WorkflowFindings(string canonicalJson, string targetPath)
    {
        using var document = JsonDocument.Parse(canonicalJson);
        var findings = new List<Finding>();
        foreach (var page in Items(document.RootElement, "pages"))
        {
            foreach (var action in Items(Required(page, "workflow", JsonValueKind.Object), "actions"))
            {
                if (Text(action, "assessment") == "appropriate" || RepoPath.Normalize(Text(action, "source_path")) != RepoPath.Normalize(targetPath)) continue;
                var recommendation = Text(action, "basis") == "recommendation";
                var observed = Strings(action, "observed_locations", true);
                findings.Add(NewFinding(action, recommendation ? "ux_recommendation" : "ux_workflow",
                    $"{Text(action, "action")} is {Text(action, "assessment")} where expected: {Text(action, "expected_location")}.",
                    $"{Text(action, "evidence")} Observed locations: {(observed.Count == 0 ? "none" : string.Join("; ", observed))}. {PageContext(page)}", recommendation ? 0.6 : 0.9));
            }
        }

        return findings.Distinct().ToList();
    }

    /// <summary>Produces findings for recorded flow, validation, rendering, interaction and text issues independently of submitted findings.</summary>
    public static IReadOnlyList<Finding> ExperienceFindings(string canonicalJson, string targetPath)
    {
        using var document = JsonDocument.Parse(canonicalJson);
        var findings = new List<Finding>();
        foreach (var page in Items(document.RootElement, "pages"))
        {
            foreach (var check in Items(page, "experience_checks"))
            {
                if (Text(check, "assessment") != "issue" || RepoPath.Normalize(Text(check, "source_path")) != RepoPath.Normalize(targetPath)) continue;
                var area = Text(check, "area");
                var recommendation = Text(check, "basis") == "recommendation";
                var category = recommendation ? "ux_recommendation" : area switch
                {
                    "flow" or "validation_recovery" => "ux_workflow",
                    "graphics" => "ux_rendering",
                    "interaction_feedback" => "ux_interaction",
                    "text_quality" => "ux_text",
                    _ => throw new JsonException("unknown experience_checks area"),
                };
                var evidence = Text(check, "evidence");
                findings.Add(NewFinding(check, category, evidence, $"{area}: {evidence} {PageContext(page)}", recommendation ? 0.6 : 0.9)
                    with
                { Severity = area == "text_quality" ? Severity.Low : Severity.Medium });
            }
        }

        return findings.Distinct().ToList();
    }

    private static Finding NewFinding(JsonElement location, string category, string claim, string evidence, double confidence) =>
        new(RepoPath.Normalize(Text(location, "source_path")), PositiveInteger(location, "line_start"), PositiveInteger(location, "line_end"),
            Severity.Medium, category, claim, evidence, confidence, "user_experience");

    private static string PageContext(JsonElement page) =>
        $"Route {Text(page, "route")}; state {Text(page, "state")}; theme {Text(page, "theme")}; artifact {Text(page, "artifact")}.";

    private static void Location(JsonElement item, IReadOnlyList<string> pagePaths)
    {
        var path = RepoPath.Normalize(Text(item, "source_path"));
        if (!pagePaths.Select(RepoPath.Normalize).Contains(path, StringComparer.Ordinal)) throw new JsonException($"ux_review evidence source_path {path} is not among the inspected UI source paths");
        if (PositiveInteger(item, "line_end") < PositiveInteger(item, "line_start")) throw new JsonException("ux_review line_end must not precede line_start");
    }

    private static (double Ratio, double Threshold) Contrast(JsonElement sample)
    {
        var first = Luminance(Text(sample, "foreground"));
        var second = Luminance(Text(sample, "background"));
        var size = Number(sample, "font_size_px");
        var weight = Number(sample, "font_weight");
        if (size <= 0 || weight < 1 || weight > 1000) throw new JsonException("ux_review contrast needs a positive font_size_px and font_weight between 1 and 1000");
        return ((Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05), size >= 24 || size >= 56d / 3 && weight >= 700 ? 3 : 4.5);
    }

    private static double Luminance(string color)
    {
        if (color.Length != 7 || color[0] != '#' || !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new JsonException("ux_review contrast colors must be composited opaque #RRGGBB values; gradients and transparency require rendered color sampling");
        return 0.2126 * Channel((rgb >> 16) & 255) + 0.7152 * Channel((rgb >> 8) & 255) + 0.0722 * Channel(rgb & 255);
    }

    private static double Channel(uint value)
    {
        var srgb = value / 255d;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static void RelativePath(string path, string field)
    {
        var normalized = RepoPath.Normalize(path);
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Any(char.IsControl)
            || normalized.Split('/').Any(segment => segment is ".." or ".") || normalized.Length == 0)
            throw new JsonException($"ux_review {field} must be a repository-relative path without traversal");
    }

    private static JsonElement Required(JsonElement element, string field, JsonValueKind kind)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(field, out var value) || value.ValueKind != kind)
            throw new JsonException($"ux_review requires {field} as {kind.ToString().ToLowerInvariant()}");
        return value;
    }

    private static string Text(JsonElement element, string field)
    {
        var value = Required(element, field, JsonValueKind.String).GetString()!;
        if (string.IsNullOrWhiteSpace(value)) throw new JsonException($"ux_review {field} must not be empty");
        return value;
    }

    private static double Number(JsonElement element, string field)
    {
        var value = Required(element, field, JsonValueKind.Number);
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) throw new JsonException($"ux_review {field} must be a finite number");
        return number;
    }

    private static int PositiveInteger(JsonElement element, string field)
    {
        var value = Required(element, field, JsonValueKind.Number);
        if (!value.TryGetInt32(out var number) || number <= 0) throw new JsonException($"ux_review {field} must be a positive integer");
        return number;
    }

    private static IReadOnlyList<JsonElement> Items(JsonElement element, string field)
    {
        var items = Required(element, field, JsonValueKind.Array).EnumerateArray().ToList();
        if (items.Count == 0 || items.Any(item => item.ValueKind != JsonValueKind.Object)) throw new JsonException($"ux_review {field} requires at least one evidence object");
        return items;
    }

    private static IReadOnlyList<string> Strings(JsonElement element, string field, bool allowEmpty = false)
    {
        var items = Required(element, field, JsonValueKind.Array).EnumerateArray().ToList();
        if ((!allowEmpty && items.Count == 0) || items.Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
            throw new JsonException($"ux_review {field} requires nonempty strings");
        return items.Select(item => item.GetString()!).ToList();
    }
}
