using System.Globalization;
using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>Plans and records conservative static usage assessments during scan.</summary>
public sealed class DeadCodeScan
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<DeadCodeAssessment>> _assessments;
    private readonly IReadOnlyList<string> _diagnostics;

    private DeadCodeScan(IReadOnlyList<PlannedUnit> plans, IReadOnlyDictionary<string, IReadOnlyList<DeadCodeAssessment>> assessments, IReadOnlyList<string> diagnostics)
    {
        Plans = plans;
        _assessments = assessments;
        _diagnostics = diagnostics;
    }

    /// <summary>Source units to upsert before recording their static assessments.</summary>
    public IReadOnlyList<PlannedUnit> Plans { get; }

    /// <summary>Builds assessments using the complete included source context and mapper diagnostics.</summary>
    public static DeadCodeScan Build(CompositeMap mapped, IReadOnlyList<FileRecord> included, IReadOnlyDictionary<string, string> sources)
    {
        var files = included.Where(file => file.ExcludedReason is null && file.DeletedAt is null)
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
        var code = files.Where(file => IsCode(file.Language) || mapped.MappedLanguages.Contains(file.Language)).ToList();
        var paths = files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
        var available = sources.Where(pair => paths.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var missingLanguages = code.Select(file => file.Language)
            .Where(language => !mapped.MappedLanguages.Contains(language) || mapped.FailedLanguages.Contains(language))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var diagnostics = mapped.Map.Diagnostics
            .Concat(missingLanguages.Select(language => $"Mapping unavailable for {language}; possible callers are not fully established."))
            .Concat(files.Where(file => !available.ContainsKey(file.Path)).Select(file => $"Source unavailable for {file.Path}; usage context is incomplete."))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var assessed = DeadCodeReview.Analyze(mapped.Map with { Diagnostics = diagnostics }, available)
            .ToLookup(assessment => assessment.Path, StringComparer.Ordinal);
        var byId = new Dictionary<string, IReadOnlyList<DeadCodeAssessment>>(StringComparer.Ordinal);
        foreach (var file in code)
        {
            var assessments = assessed[file.Path].ToList();
            if (assessments.Count == 0)
            {
                var reason = missingLanguages.Contains(file.Language)
                    ? $"Mapping unavailable for {file.Language}; no unused-code inference is possible for this file."
                    : "No mapped declarations are available for this file; its usage cannot be established.";
                assessments.Add(new DeadCodeAssessment(UnitIds.DeadCode(file.Path), file.Path, new LineRange(1, 1), DeadCodeState.Unknown, reason, []));
            }
            byId.Add(UnitIds.DeadCode(file.Path), assessments);
        }

        var contextHash = Hashing.Sha256Hex(JsonSerializer.Serialize(new
        {
            Files = files.Select(file => new { file.Path, file.ContentHash }),
            Sources = available.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { Path = pair.Key, Hash = Hashing.Sha256Hex(pair.Value) }),
            Assessments = byId.Values.SelectMany(assessments => assessments),
            Diagnostics = diagnostics
        }, DomainJson.Options));
        var plans = code.Select(file =>
        {
            var id = UnitIds.DeadCode(file.Path);
            var fidelity = byId[id].Any(assessment => assessment.State == DeadCodeState.Unknown) ? Fidelity.Low : Fidelity.Full;
            return new PlannedUnit(id, UnitKind.DeadCode, file.Path, fidelity,
                [new UnitMember(id, file.Path, null, Hashing.Sha256Hex(file.ContentHash + "\0" + contextHash), 0)]);
        }).ToList();
        return new DeadCodeScan(plans, byId, diagnostics);
    }

    /// <summary>Records changed assessments while retaining unchanged finding identities.</summary>
    public async Task RecordAsync(ILedger ledger, Config config, string now, CancellationToken cancellationToken)
    {
        var units = (await ledger.GetUnitsAsync(cancellationToken)).ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        foreach (var plan in Plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fingerprint = Fingerprints.Compute(plan.Members);
            if (!units.TryGetValue(plan.Id, out var unit) || unit.Kind != UnitKind.DeadCode || unit.Status == UnitStatus.Retired || unit.Fingerprint != fingerprint)
                throw new InvalidOperationException($"Dead-code plan for {plan.Key} does not match its current unit; scan again before recording.");
            var lensHash = Config.HashOf(config.LensesFor(plan.Members.Select(member => (member.Path, Languages.FromPath(member.Path)))));
            if (unit.Status == UnitStatus.Done && unit.SummaryHash == fingerprint && unit.LensHash == lensHash) continue;

            var assessments = _assessments[plan.Id];
            var candidates = assessments.Where(assessment => assessment.State == DeadCodeState.Candidate).ToList();
            var unknown = assessments.Count(assessment => assessment.State == DeadCodeState.Unknown);
            var protectedCount = assessments.Count - candidates.Count - unknown;
            var summary = string.Create(CultureInfo.InvariantCulture,
                $"Static usage: {candidates.Count} unused candidate(s), {protectedCount} protected/reachable, {unknown} unknown{(unknown > 0 ? " (mapping or usage unavailable)" : "")}; candidates do not authorize deletion.");
            var evidence = JsonSerializer.Serialize(new
            {
                SourcePath = plan.Key,
                Scope = "Included repository source and supported mappings; external, generated and configuration-driven consumers may remain unresolved.",
                Assessments = assessments,
                Diagnostics = _diagnostics
            }, DomainJson.Options);
            var findings = candidates.Select(assessment => new Finding(
                assessment.Path, assessment.Range.StartLine, assessment.Range.EndLine, Severity.Info, DeadCodeReview.Id,
                $"{assessment.SymbolId} is an unused-code candidate within the supplied map.",
                DeadCodeReview.RenderEvidence(assessment), 0.5, DeadCodeReview.Id)).ToList();
            await ledger.RecordAnalysisAsync(new Analysis(plan.Id, fingerprint, lensHash, now, true, summary, null, new AgentIdentity("codemuster-static"))
            {
                EvidenceJson = evidence
            }, findings, cancellationToken);
        }
    }

    private static bool IsCode(string language) => language is Languages.CSharp or Languages.TypeScript or Languages.JavaScript
        or Languages.Razor or Languages.Python or Languages.Go or Languages.Rust or Languages.Java or Languages.Shell or Languages.PowerShell;
}
