using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeMuster.Application.Tests;

public class UxEvidenceTests
{
    private static JsonObject Response() => JsonNode.Parse("""
        {
          "summary":"Invoice payment journey reviewed.",
          "findings":[],
          "ux_review":{
            "fingerprint":"current-source",
            "runtime_url":"http://localhost:3000",
            "runtime_source_evidence":"Started the application from this checkout after the current changes; the invoice label matches the edited source.",
            "status":"complete",
            "pages":[{
              "source_paths":["web/Invoice.tsx"],
              "route":"/invoices/42",
              "state":"open invoice, signed in as billing manager",
              "theme":"light",
              "viewport":{"width":1280,"height":800},
              "artifact":".codemuster/evidence/invoice.png",
              "artifact_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "experience_checks":[
                {"area":"flow","source_path":"web/Invoice.tsx","line_start":1,"line_end":30,"assessment":"pass","evidence":"Reviewed invoice goal, sequence, available context and task completion.","basis":"observed"},
                {"area":"validation_recovery","source_path":"web/Invoice.tsx","line_start":1,"line_end":30,"assessment":"pass","evidence":"Validation explains the issue, preserves entered values and allows correction.","basis":"observed"},
                {"area":"graphics","source_path":"web/Invoice.tsx","line_start":1,"line_end":30,"assessment":"pass","evidence":"No clipped text, overlapping controls, broken imagery or unreadable graphical elements in inspected state.","basis":"observed"},
                {"area":"interaction_feedback","source_path":"web/Invoice.tsx","line_start":1,"line_end":30,"assessment":"pass","evidence":"Buttons visibly respond to activation and show busy state while work is pending.","basis":"observed"},
                {"area":"text_quality","source_path":"web/Invoice.tsx","line_start":1,"line_end":30,"assessment":"pass","evidence":"Visible labels and messages have correct spelling and consistent customer/invoice terminology.","basis":"observed"}
              ],
              "readability":{
                "visual_inspection":"Read all labels and amounts in the rendered invoice screenshot.",
                "font_and_spacing":"Body text is 16px with no clipped labels or cramped amounts.",
                "overlays_and_states":"Payment dialog and validation message remain legible without covering the amount.",
                "contrast_samples":[{
                  "source_path":"web/Invoice.tsx",
                  "line_start":10,
                  "line_end":10,
                  "target":"Invoice outstanding balance",
                  "foreground":"#000000",
                  "background":"#ffffff",
                  "font_size_px":16,
                  "font_weight":400,
                  "contrast_ratio":21,
                  "assessment":"pass"
                }]
              },
              "workflow":{
                "task":"Collect payment for an outstanding invoice",
                "steps":["Open invoice 42 and read the amount due.","Open Charge Customer and verify the amount before cancelling."],
                "result":"The invoice exposes collection beside the balance and opens a confirmation with the same amount.",
                "actions":[{
                  "source_path":"web/Invoice.tsx",
                  "line_start":20,
                  "line_end":30,
                  "action":"Charge Customer",
                  "expected_location":"Invoice view beside the outstanding balance",
                  "observed_locations":["Invoice view beside the outstanding balance","Schedule shortcut"],
                  "assessment":"appropriate",
                  "evidence":"The invoice and schedule both expose payment collection; the invoice contains the amount needed to decide.",
                  "basis":"observed"
                }]
              }
            }]
          }
        }
        """)!.AsObject();

    private static JsonObject Page(JsonObject response) => response["ux_review"]!["pages"]![0]!.AsObject();

    private static string Validate(JsonObject response, params string[] targets) =>
        UxEvidence.ValidateAndSerialize(response.ToJsonString(), "current-source", targets.Length == 0 ? ["web/Invoice.tsx"] : targets);

    [Fact]
    public void CompleteEvidenceRequiresAndPreservesBothRenderedReadabilityAndBusinessWorkflow()
    {
        var canonical = Validate(Response());

        using var document = JsonDocument.Parse(canonical);
        Assert.Equal("complete", document.RootElement.GetProperty("status").GetString());
        Assert.Contains("Charge Customer", canonical);
        Assert.Contains("contrast_samples", canonical);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blocked")]
    [InlineData("not_applicable")]
    public void DoesNotAcceptAReviewWithoutCompleteBrowserEvidence(string status)
    {
        var response = Response();
        if (status == "missing") response.Remove("ux_review");
        else
        {
            response["ux_review"]!["status"] = status;
            response["ux_review"]!["reason"] = "Browser is unavailable.";
        }

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Fact]
    public void RejectsEvidenceForAStaleSourceFingerprint()
    {
        var response = Response();
        response["ux_review"]!["fingerprint"] = "old-source";

        Assert.Contains("fingerprint", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void RejectsACompleteReviewThatDidNotExerciseEveryUiTarget()
    {
        Assert.Contains("web/Schedule.tsx", Assert.Throws<JsonException>(() => Validate(Response(), "web/Invoice.tsx", "web/Schedule.tsx")).Message);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("state")]
    [InlineData("theme")]
    [InlineData("artifact")]
    [InlineData("readability")]
    [InlineData("workflow")]
    public void RejectsMissingPageEvidence(string field)
    {
        var response = Response();
        Page(response).Remove(field);

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Theory]
    [InlineData("visual_inspection")]
    [InlineData("font_and_spacing")]
    [InlineData("overlays_and_states")]
    [InlineData("contrast_samples")]
    public void RejectsAWorkflowOnlyReviewThatSkipsRenderedReadability(string field)
    {
        var response = Response();
        Page(response)["readability"]!.AsObject().Remove(field);

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Fact]
    public void RejectsClickThroughWithoutCheckingExpectedActionPlacement()
    {
        var response = Response();
        Page(response)["workflow"]!["actions"] = new JsonArray();

        Assert.Contains("action", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void RejectsAWorkflowWithNoJourney()
    {
        var response = Response();
        Page(response)["workflow"]!["steps"] = new JsonArray("Opened the page.");

        Assert.Contains("steps", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void RejectsContrastNumbersThatDoNotMatchTheRenderedColors()
    {
        var response = Response();
        Page(response)["readability"]!["contrast_samples"]![0]!["contrast_ratio"] = 4.5;

        Assert.Contains("contrast", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void RejectsFalsePassForHardToReadText()
    {
        var response = Response();
        var sample = Page(response)["readability"]!["contrast_samples"]![0]!;
        sample["foreground"] = "#aaaaaa";
        sample["contrast_ratio"] = 2.32;

        Assert.Contains("assessment", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void FailedContrastProducesAFindingEvenWhenTheAgentReturnsNone()
    {
        var response = Response();
        var sample = Page(response)["readability"]!["contrast_samples"]![0]!;
        sample["foreground"] = "#aaaaaa";
        sample["contrast_ratio"] = 2.32;
        sample["assessment"] = "fail";

        var finding = Assert.Single(UxEvidence.MeasuredFindings(Validate(response), "web/Invoice.tsx"));

        Assert.Equal("ux_readability", finding.Category);
        Assert.Equal("user_experience", finding.LensId);
        Assert.Equal(10, finding.LineStart);
        Assert.Contains("2.32:1", finding.Evidence);
        Assert.Contains("4.5:1", finding.Evidence);
    }

    [Fact]
    public void LargeBoldTextUsesItsApplicableContrastThreshold()
    {
        var response = Response();
        var sample = Page(response)["readability"]!["contrast_samples"]![0]!;
        sample["foreground"] = "#777777";
        sample["contrast_ratio"] = 4.48;
        sample["font_size_px"] = 20;
        sample["font_weight"] = 700;

        Assert.Empty(UxEvidence.MeasuredFindings(Validate(response), "web/Invoice.tsx"));
    }

    [Theory]
    [InlineData("observed", "ux_workflow")]
    [InlineData("recommendation", "ux_recommendation")]
    public void MissingInvoiceActionProducesAnAppropriatelyClassifiedFinding(string basis, string category)
    {
        var response = Response();
        var action = Page(response)["workflow"]!["actions"]![0]!;
        action["observed_locations"] = new JsonArray("Schedule view");
        action["assessment"] = "misplaced";
        action["basis"] = basis;
        action["evidence"] = "Only Schedule has Charge Customer; the invoice shows the balance but requires leaving the billing workflow.";

        var finding = Assert.Single(UxEvidence.WorkflowFindings(Validate(response), "web/Invoice.tsx"));

        Assert.Equal(category, finding.Category);
        Assert.Contains("Charge Customer", finding.Claim);
        Assert.Contains("Invoice view", finding.Claim);
    }

    [Fact]
    public void RejectsEvidenceCitingSourceOutsideTheUiTargets()
    {
        var response = Response();
        Page(response)["readability"]!["contrast_samples"]![0]!["source_path"] = "backend/InvoiceService.cs";

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Theory]
    [InlineData("rgba(0,0,0,0.2)")]
    [InlineData("linear-gradient(white,black)")]
    public void RequiresCompositedOpaqueColorsForContrastMeasurement(string foreground)
    {
        var response = Response();
        Page(response)["readability"]!["contrast_samples"]![0]!["foreground"] = foreground;

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Theory]
    [InlineData("flow")]
    [InlineData("validation_recovery")]
    [InlineData("graphics")]
    [InlineData("interaction_feedback")]
    [InlineData("text_quality")]
    public void EveryExperienceAreaMustBeExplicitlyAssessed(string area)
    {
        var response = Response();
        var checks = Page(response)["experience_checks"]!.AsArray();
        checks.Remove(checks.Single(check => check!["area"]!.GetValue<string>() == area));

        Assert.Contains(area, Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void RejectsMissingExperienceChecks()
    {
        var response = Response();
        Page(response).Remove("experience_checks");

        Assert.Contains("experience_checks", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Theory]
    [InlineData("flow", "ux_workflow", "Payment works, but the user must re-enter an invoice number already present on the screen.")]
    [InlineData("validation_recovery", "ux_workflow", "Validation clears the customer's corrected billing address after a retry.")]
    [InlineData("graphics", "ux_rendering", "The outstanding amount is clipped by a fixed-width card.")]
    [InlineData("interaction_feedback", "ux_interaction", "The clicked Save button gives no pressed or loading feedback during the operation.")]
    [InlineData("text_quality", "ux_text", "The action label reads Chagre Customer.")]
    public void ExperienceDefectsCannotBeHiddenByAnEmptyFindingsArray(string area, string category, string evidence)
    {
        var response = Response();
        var check = Page(response)["experience_checks"]!.AsArray().Single(item => item!["area"]!.GetValue<string>() == area)!;
        check["assessment"] = "issue";
        check["evidence"] = evidence;
        var canonical = Validate(response);

        Assert.Empty(UxEvidence.WorkflowFindings(canonical, "web/Invoice.tsx"));
        var finding = Assert.Single(UxEvidence.ExperienceFindings(canonical, "web/Invoice.tsx"));
        Assert.Equal(category, finding.Category);
        Assert.Equal("user_experience", finding.LensId);
        Assert.Contains(evidence, finding.Evidence);
    }

    [Fact]
    public void PureStylePreferenceRemainsARecommendation()
    {
        var response = Response();
        var check = Page(response)["experience_checks"]![2]!;
        check["assessment"] = "issue";
        check["basis"] = "recommendation";
        check["evidence"] = "A rounder card corner could better match the chosen visual style.";

        Assert.Equal("ux_recommendation", Assert.Single(UxEvidence.ExperienceFindings(Validate(response), "web/Invoice.tsx")).Category);
    }

    [Fact]
    public void NotApplicableChecksRequireAnExplanation()
    {
        var response = Response();
        var check = Page(response)["experience_checks"]![1]!;
        check["assessment"] = "not_applicable";
        Assert.Throws<JsonException>(() => Validate(response));

        check["reason"] = "This static read-only invoice view has no editable fields or asynchronous operations.";
        Assert.Empty(UxEvidence.ExperienceFindings(Validate(response), "web/Invoice.tsx"));
    }

    [Theory]
    [InlineData("flow")]
    [InlineData("graphics")]
    public void UiFlowAndRenderingCannotBeDismissedAsNotApplicable(string area)
    {
        var response = Response();
        var check = Page(response)["experience_checks"]!.AsArray().Single(item => item!["area"]!.GetValue<string>() == area)!;
        check["assessment"] = "not_applicable";
        check["reason"] = "Skipped it.";

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Theory]
    [InlineData("assessment", "maybe")]
    [InlineData("basis", "guessed")]
    [InlineData("area", "unknown")]
    public void RejectsUnknownExperienceValues(string field, string value)
    {
        var response = Response();
        Page(response)["experience_checks"]![0]![field] = value;

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Theory]
    [InlineData("artifact", "../outside.png")]
    [InlineData("artifact", ".codemuster/evidence/receipt.json")]
    [InlineData("route", "not a route")]
    public void RejectsUnusableArtifactOrRoute(string field, string value)
    {
        var response = Response();
        Page(response)[field] = value;

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Fact]
    public void RejectsControlCharactersInArtifactPaths()
    {
        var response = Response();
        Page(response)["artifact"] = ".codemuster/evidence/invoice\u0000.png";

        Assert.Throws<JsonException>(() => Validate(response));
    }

    [Theory]
    [InlineData("runtime_url")]
    [InlineData("runtime_source_evidence")]
    public void RejectsMissingRuntimeIdentityEvidence(string field)
    {
        var response = Response();
        response["ux_review"]!.AsObject().Remove(field);

        Assert.Contains(field, Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void RequiresScreenshotContentHash(string? hash)
    {
        var response = Response();
        if (hash is null) Page(response).Remove("artifact_sha256");
        else Page(response)["artifact_sha256"] = hash;

        Assert.Contains("artifact_sha256", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Theory]
    [InlineData("/invoices")]
    [InlineData("file:///app.html")]
    [InlineData("not a url")]
    public void RuntimeUrlMustIdentifyAnHttpApplication(string runtimeUrl)
    {
        var response = Response();
        response["ux_review"]!["runtime_url"] = runtimeUrl;

        Assert.Contains("runtime_url", Assert.Throws<JsonException>(() => Validate(response)).Message);
    }

    [Fact]
    public void RejectsEmptyEvidenceAndInvalidViewport()
    {
        var response = Response();
        Page(response)["viewport"]!["width"] = 0;
        Assert.Throws<JsonException>(() => Validate(response));

        response = Response();
        Page(response)["readability"]!["font_and_spacing"] = " ";
        Assert.Throws<JsonException>(() => Validate(response));
    }
}
