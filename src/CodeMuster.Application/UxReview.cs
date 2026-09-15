using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Plans browser work separately from source coverage and provides its review contract.</summary>
public static class UxReview
{
    /// <summary>The reserved lens for UI browser reviews.</summary>
    public const string Id = "user_experience";

    /// <summary>The browser review obligations that source inspection alone cannot satisfy.</summary>
    public const string Instructions =
        "The primary question is whether the user's end-to-end task makes sense: understandable sequence, information and actions in the right context, predictable results, and useful validation and recovery. A technically working flow can still be a UX defect. "
        + "Use the running UI in a browser and inspect the rendered screen, not only its source or DOM. "
        + "Review readability: measured text contrast against its actual composited background, font size and spacing, "
        + "overlaps, clipping, responsive rendering, graphical inconsistencies, focus, error/loading states, and relevant themes and viewports. Use rendered computed colors; never guess values from a screenshot. "
        + "Inspect visible text for typos, misleading labels, inconsistent terms and unclear validation messages. Exercise buttons with pointer and keyboard: check pressed/click feedback, focus, disabled/loading state and acknowledgement so the user knows an action happened. "
        + "Sample meaningful active text; decorative, disabled and logo text do not establish normative contrast failures. "
        + "Review a meaningful business task through at least two steps, including sensible order, unnecessary detours, validation before submission, preserving entered work after errors, and the expected location of each important action. "
        + "Inspect related screens: a Charge Customer shortcut on a schedule does not replace collecting payment from the invoice/job where the balance and work are reviewed. "
        + "Explain expected versus observed action placement using the application's business rules, permissions, and status. "
        + "A schedule shortcut is not inherently a defect; distinguish observed broken/missing behavior (ux_workflow) from design recommendations (ux_recommendation). "
        + "Read related UI and backend code for context, but cite findings only against the UI target under Files. "
        + "Exercise non-destructive actions with test data; never charge a real customer or send a real message as a review step. "
        + "Save browser screenshots under a repo-relative artifact path, and supply the ux_review receipt with readability AND workflow evidence. "
        + "If the app, login, browser, target state, or trustworthy contrast samples are unavailable, explain the blocker; UX remains incomplete. "
        + "Do not invent browser observations, treat source coverage as a visual pass, or claim unvisited screens/states were checked.";

    /// <summary>One file per UI review. Repository source hashes contribute so shared styles, sibling workflows and backend behavior invalidate prior evidence.</summary>
    public static IReadOnlyList<PlannedUnit> Plan(IReadOnlyList<FileRecord> files, UserExperienceSettings settings)
    {
        var ui = files.Where(f => settings.Applies(f.Path)).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        var contextHash = Hashing.Sha256Hex(string.Join('\n', files.OrderBy(f => f.Path, StringComparer.Ordinal).Select(f => f.Path + "\0" + f.ContentHash))
            + "\0" + JsonSerializer.Serialize(settings, DomainJson.Options));
        return ui.Select(file =>
        {
            var id = UnitIds.Ux(file.Path);
            return new PlannedUnit(id, UnitKind.Ux, file.Path, Fidelity.Full,
                [new UnitMember(id, file.Path, null, Hashing.Sha256Hex(file.ContentHash + "\0" + contextHash), 0)]);
        }).ToList();
    }

    /// <summary>Settings are part of the lens hash so changing the URL or UI boundaries reopens browser review.</summary>
    public static Lens Lens(UserExperienceSettings settings) => new(Id,
        Instructions + "\n\nRepository UI settings: " + JsonSerializer.Serialize(settings, DomainJson.Options), [], []);
}
