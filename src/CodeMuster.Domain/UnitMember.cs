namespace CodeMuster.Domain;

/// <summary>One file or symbol that belongs to a unit, with the content hash it had when the unit was built.</summary>
/// <param name="UnitId">Owning unit.</param>
/// <param name="Path">Repo-relative path.</param>
/// <param name="Symbol">Symbol id within the file, or null for the whole file.</param>
/// <param name="MemberHash">Content hash of the file (or symbol body) at build time.</param>
/// <param name="Distance">Call-path distance from the entry point; 0 for file units.</param>
public sealed record UnitMember(string UnitId, string Path, string? Symbol, string MemberHash, int Distance);
