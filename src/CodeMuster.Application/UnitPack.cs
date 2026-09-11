namespace CodeMuster.Application;

/// <summary>One unit's work ready for the model: the id and fingerprint <c>done</c> must echo back, and the pack itself.</summary>
/// <param name="UnitId">The unit the pack is for.</param>
/// <param name="Fingerprint">The unit fingerprint the pack was built from.</param>
/// <param name="Markdown">The whole pack as one markdown document.</param>
public sealed record UnitPack(string UnitId, string Fingerprint, string Markdown);
