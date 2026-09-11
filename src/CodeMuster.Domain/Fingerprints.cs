namespace CodeMuster.Domain;

/// <summary>Computes the fingerprint that the members of a unit hash to, independent of member order.</summary>
public static class Fingerprints
{
    /// <summary>SHA-256 hex over the sorted (path, symbol, hash) triples.</summary>
    public static string Compute(IEnumerable<UnitMember> members)
    {
        var lines = members
            .Select(m => $"{m.Path}\0{m.Symbol}\0{m.MemberHash}")
            .Order(StringComparer.Ordinal);
        return Hashing.Sha256Hex(string.Join('\n', lines));
    }
}
