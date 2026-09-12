using CodeMuster.Domain;

namespace CodeMuster.Application;

/// <summary>One unit's work ready for the model: the id and fingerprint <c>done</c> must echo back, how to name the unit for a person, and the pack itself.</summary>
/// <param name="UnitId">The unit the pack is for.</param>
/// <param name="Kind">What kind of unit it is.</param>
/// <param name="Key">The unit's short human name, such as an endpoint or a path.</param>
/// <param name="Fingerprint">The unit fingerprint the pack was built from.</param>
/// <param name="Markdown">The whole pack as one markdown document.</param>
public sealed record UnitPack(string UnitId, UnitKind Kind, string Key, string Fingerprint, string Markdown);
