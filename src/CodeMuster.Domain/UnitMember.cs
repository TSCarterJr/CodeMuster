namespace CodeMuster.Domain;

/// <summary>One file or symbol that belongs to a unit, with the hash it had when the unit was built.</summary>
/// <param name="UnitId">Owning unit.</param>
/// <param name="Path">Repo-relative path.</param>
/// <param name="Symbol">Symbol id (D26), or null for the whole file.</param>
/// <param name="MemberHash">Content hash of the whole file, or a hash over the symbol's body hash and signature, at build time.</param>
/// <param name="Distance">Call-path distance from the entry point; 0 for entry points and whole-file members.</param>
/// <param name="Range">Lines the symbol spanned at build time; null for the whole file.</param>
/// <param name="Signature">The symbol's signature (D26); null for the whole file.</param>
public sealed record UnitMember(string UnitId, string Path, string? Symbol, string MemberHash, int Distance, LineRange? Range = null, string? Signature = null);
