# Dead-code and UI experience reviews

These optional reviews extend CodeMuster's source audit. Both start disabled and follow the
repository's existing `automation` mode when enabled. This guide describes the current source;
check that your installed CLI's `next --help` lists `ux` before using these features.

## Enable the reviews you want

Merge these properties into `.codemuster/config.json`, preserving existing lenses and other
settings. Use your actual application address:

```json
{
  "dead_code": true,
  "user_experience": {
    "enabled": true,
    "base_url": "http://localhost:3000",
    "include": [],
    "exclude": []
  }
}
```

| Setting | Default | Behavior |
|---|---|---|
| `dead_code` | `false` | Record conservative static usage assessments during `scan`; no model call or automatic deletion. |
| `user_experience.enabled` | `false` | Plan separate browser-review units for applicable UI source files. |
| `user_experience.base_url` | Absent | Optional absolute HTTP(S) address of the running application. Without it, the active agent must locate the application. |
| `user_experience.include` | `[]` | Additional repo-relative UI globs for project-specific conventions. |
| `user_experience.exclude` | `[]` | Remove matching paths from UI review; these exclusions take precedence over UI includes. |

`include` extends UI detection; it does not replace the normal conventions. Recognized files
include JSX/TSX, Vue, Svelte, Astro, HTML, Razor, stylesheets and Angular `*.component.ts` files.
Tests are normally excluded from UI detection. An explicit include can identify a custom UI
convention, so use it deliberately. The repository's top-level `exclude` still removes files
from the entire scan.

A backend-only project has no applicable UX targets. Backend services, controllers and CLI
code are not reviewed as screens. An agent may read backend behavior to understand a UI's
permissions, status transitions and available actions.

`automation: "update"` refreshes static assessments and queues browser work; it does not
perform a browser review. `review` processes the selected review scope. `review_and_fix` also
permits eligible repairs through the existing CLI workflow. Changing the mode does not enable
either optional review by itself.

## Dead code: usage evidence before conclusions

Run `codemuster scan` in normal slice mode. CodeMuster follows mapped entry points and their
transitive call paths, including dependency-bound and virtual calls. It records one static
usage unit per applicable source file and preserves the assessment evidence in the ledger.

| Assessment | Meaning |
|---|---|
| Protected entry point | An HTTP route, public/exported member, or recognized framework/runtime entry may have callers outside the direct call graph. |
| Reachable | A mapped path connects the declaration to a protected entry. |
| Unused candidate | No known entry reaches this bounded internal declaration in the supplied map. Further usage review is needed. |
| Unknown | The mapping, declaration visibility or dynamic invocation evidence cannot establish usage. |

An HTTP endpoint stays protected when no code caller is found. Literal request-shaped calls
such as `fetch('/customers')` or `api.post('/customers/123/charge', body)` can add positive
source evidence connecting a UI request to an endpoint. Matching supports simple HTTP verbs
and route parameters. It does not resolve every generated client, constructed URL, form
submission, external integration or runtime consumer. A literal match is not a recorded
network request, and an absent match never makes an endpoint safe to delete.

Unresolved calls, mapper diagnostics, missing language support, or detected dynamic
invocation/registration keep otherwise unreachable declarations unknown. This conservative
behavior can suppress candidates in real applications. File-only scan mode cannot establish
symbol reachability. The current analysis covers mapped declarations; it is not an exhaustive
unused-file, export, resource or dependency remover.

An orphan unit means code was outside the mapped entry-point slices. It does not prove dead
code. An isolated group whose members only call each other may be a candidate, but that still
does not prove nothing outside the map uses it.

Findings use category and lens `dead_code`, remain report-only, and never enter automatic
`fix`, even if someone previously marked them confirmed or enabled `review_and_fix`. Do not
turn an AI confidence score or a quiet production observation period into deletion authority.

## UI review: render, read and complete a task

Start or rebuild the application from the checkout being reviewed using the project's normal
development workflow. Use the browser already available to the active coding agent. CodeMuster
does not install a browser, provide credentials or create a separate browser service.

```sh
codemuster scan
codemuster next --kind ux --path web/Invoice.tsx
```

Use the actual UI source path. Without `--path`, `next --kind ux` selects the next pending UI
target. Each pack contains the authoritative `ux_review` response template and the exact
`done` command for its unit and fingerprint.

The primary user flow comes first. Carry out a meaningful task with at least two observed
steps and judge whether the sequence makes sense, including where information and actions
are available. Check permissions and business state, and inspect related screens where needed.
Every button can technically work while the workflow still forces users to act in an
illogical order, leave the relevant task, or decide before seeing required information.

Also inspect the actual rendered experience: readability, text contrast, graphics, broken
images, alignment, clipping, overlapping controls, typos and misleading labels. Operate the
controls and examine pressed/loading feedback, repeated-click behavior, validation, error
messages and recovery. A click reaching its handler does not establish that the button feels
responsive or that users understand what happened. Include relevant themes, viewports and
loading, empty, focus and error states in the recorded scope.

For example, a schedule shortcut for **Charge Customer** can be useful. If the invoice shows
an outstanding balance but collecting payment requires leaving the billing workflow, inspect
the actual invoice/job actions and permissions before reporting a missing or misplaced
action. An observed flow that conflicts with the task or required business sequence is a
`ux_workflow` defect even when all its controls function. Explain the observed sequence,
expected sequence and practical consequence. Keep pure visual taste and unproven preferences
as recommendations; do not automatically downgrade an illogical workflow to subjective advice.

Use test data and non-destructive actions. Opening the payment flow to inspect its amount and
controls does not require charging a real customer.

### Browser evidence receipt

Add the pack's `ux_review` object to the normal analysis response. Keep the response JSON in a
temporary file, and retain screenshot artifacts under the repository, for example
`.codemuster/evidence/invoice.png`.

The receipt requires:

- The current pack fingerprint, actual `runtime_url`, and `runtime_source_evidence` explaining
  how the running application was started/rebuilt from this checkout. Copying the fingerprint
  alone does not establish runtime provenance.
- Every inspected target's source path, route, role/business state, theme and viewport.
- An actual PNG, JPEG or WebP screenshot path and its computed `artifact_sha256`.
- Readability observations and contrast samples with source locations, target text, measured
  composited colors, font size/weight, ratio and assessment.
- The business task, observed steps/result, and important actions' expected and observed
  locations. Each placement judgment states its evidence and whether its basis is observed
  behavior or a recommendation.
- Per-page `experience_checks` covering the five areas below, so checking contrast or
  successfully clicking a button cannot stand in for the complete experience review.

| Required area | What to inspect | Observed issue category |
|---|---|---|
| `flow` | Primary task, sensible sequence, expected context, information and action placement | `ux_workflow` |
| `validation_recovery` | Invalid input, helpful errors, preserved work and a clear way to recover | `ux_workflow` |
| `graphics` | Rendering, broken images, clipping, overlap, alignment and graphical defects | `ux_rendering` |
| `interaction_feedback` | Button-click feel, pressed/loading states, completion feedback and repeated clicks | `ux_interaction` |
| `text_quality` | Typos, misleading labels, inconsistent wording and unclear messages | `ux_text` |

Each experience check cites `source_path`, `line_start` and `line_end`, gives an `assessment`
of `pass`, `issue` or `not_applicable`, and records its `evidence` and `basis` (`observed` or
`recommendation`). A not-applicable check requires a reason and is allowed only for
`validation_recovery`, `interaction_feedback` or `text_quality`; `flow` and `graphics` must
always be inspected. There must be at least one check for each area on every inspected page.
An issue based on a preference becomes a report-only
recommendation; an observed problem uses the category in the table.

Use the exact `codemuster done ... --findings <response-file>` command printed in the pack.
The CLI checks the receipt, screenshot file type and hash, and the current source state. It
records the evidence in the ledger. A missing, changed or invalid artifact prevents completion.
These checks validate the supplied evidence; they do not independently prove that an agent
visited every relevant state or made an accurate business judgment.

The CLI derives findings from recorded contrast failures, missing/misplaced actions and
experience-check issues. An empty model-written findings array cannot hide failures present
in the receipt. Findings must cite the UI target, not a backend file consulted for context.

### Contrast measurements

Use browser-computed text colors and the actual composited background. Transparency, overlays,
gradients and images need reliable rendered color measurements; guessing colors from a
screenshot is not sufficient. Visual inspection remains necessary even when a numeric sample
passes.

CodeMuster calculates text contrast from opaque `#RRGGBB` values. It uses 4.5:1 for ordinary
text and 3:1 for large text: at least 24 CSS pixels, or at least 18 2/3 pixels at weight 700 or
higher. Ratios are checked before rounding. Keep disabled controls, purely decorative text
and logo text out of normative samples; explain relevant exceptions in the visual observations.
The thresholds and exceptions come from [W3C's contrast guidance](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html).

This is a text-readability check with recorded scope, not a complete accessibility certification.
If reliable applicable samples or required states cannot be inspected, record the blocker and
leave the UX review incomplete.

### When a browser review cannot run

Headless `run` leaves browser-required units pending and reports the browser requirement
without sending them to a model. It can continue normal source work. A successful source audit
does not complete UI work.

If the application, browser, login, state, screenshots or measurements are unavailable, explain
which requirement is missing. Do not repeatedly submit a fabricated or incomplete receipt.
Resume through the active browser-capable agent when the requirement is available.

## Findings, repairs and verification

| Category | Meaning | Automatic repair |
|---|---|---|
| `dead_code` | Bounded unused-code candidate | Never |
| `ux_readability` | Evidence-backed readability defect, including measured contrast failures | Only when confirmed and current |
| `ux_workflow` | Observed illogical flow, action-placement defect, or validation/recovery problem | Only when confirmed and current |
| `ux_rendering` | Observed graphical, clipping, overlap or rendering defect | Only when confirmed and current |
| `ux_interaction` | Observed button-response, pressed/loading or other feedback defect | Only when confirmed and current |
| `ux_text` | Observed typo, misleading label or wording defect | Only when confirmed and current |
| `ux_recommendation` | Pure taste or an unproven design/workflow preference | Never |

The reserved UX lens is `user_experience`. These rules also apply in `review_and_fix`.
Report-only findings stay visible; they do not prevent repair of eligible findings in the
same file. Repairs still use isolated workers, configured tests and scoped local commits.
The workers need the reviewed source committed, as described in the
[automatic workflow](usage.md#automatic-use-during-coding).

After repairing a UI defect, restart/rebuild the relevant application and revisit the browser.
Use the existing `verify --force` workflow to refresh known findings, then obtain browser
verification work interactively:

```sh
codemuster next --kind verify --path web/Invoice.tsx
```

A confirmed, refuted or resolved UX verdict needs a fresh `ux_review` receipt and the exact `done`
command from its verification pack. Headless verification cannot substitute source reasoning
for a browser observation. Use `unsure` when available evidence cannot settle the claim.
Finally run the configured `codemuster validate` checks and report remaining findings.

## Coverage and staleness

Read `codemuster status` and `codemuster report` for separate source, static-usage and browser
coverage. An enabled backend-only project reports UX as not applicable. An enabled UI project
with missing browser evidence remains incomplete.

The current implementation includes all scanned source hashes in each UI target's review
context. Changing shared styles, another page or backend behavior can therefore invalidate
earlier UI evidence. This is deliberately broader than a guessed UI dependency graph.

Automatic `next --path` still selects the task's paths. It does not silently expand the
review to every newly stale UI target. Report the screens/states actually inspected and the
remaining stale UI work; run the broader browser queue when full UI coverage is requested.
Retained screenshots and receipts describe the source/runtime/state that was observed, not
an assurance about an untested deployment or every possible user journey.
