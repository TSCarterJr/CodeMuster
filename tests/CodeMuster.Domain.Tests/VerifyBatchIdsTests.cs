using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class VerifyBatchIdsTests
{
    [Fact]
    public void OneFinding_KeepsTheOriginalId_AndSeveralAreListed()
    {
        Assert.Equal("verify:12", UnitIds.Verify([12]));
        Assert.Equal("verify:12,13,20", UnitIds.Verify([12, 13, 20]));
    }

    [Fact]
    public void VerifiedFindings_ReadsTheIdsBack_AndIsEmptyForOtherUnits()
    {
        Assert.Equal([12L], UnitIds.VerifiedFindings("verify:12"));
        Assert.Equal([12L, 13L, 20L], UnitIds.VerifiedFindings("verify:12,13,20"));
        Assert.Empty(UnitIds.VerifiedFindings("file:src/verify:1.cs"));
        Assert.Empty(UnitIds.VerifiedFindings("verify:x"));
    }

    [Fact]
    public void TheBatchResponse_RoundTripsItsSample()
    {
        var parsed = VerifyBatchResponseJson.Parse(VerifyBatchResponseJson.Sample);

        Assert.Equal(2, parsed.Verdicts.Count);
        Assert.Equal(new FindingVerdict(12, Verdict.Confirmed, parsed.Verdicts[0].Reason), parsed.Verdicts[0]);
        Assert.Equal(parsed, VerifyBatchResponseJson.Parse(VerifyBatchResponseJson.Serialize(parsed) is var json ? json : ""), new BatchComparer());
        Assert.Throws<System.Text.Json.JsonException>(() => VerifyBatchResponseJson.Parse("""{ "verdicts": [{ "finding": 1, "verdict": "maybe", "reason": "x" }] }"""));
    }

    private sealed class BatchComparer : IEqualityComparer<VerifyBatchResponse>
    {
        public bool Equals(VerifyBatchResponse? x, VerifyBatchResponse? y) => x!.Verdicts.SequenceEqual(y!.Verdicts);
        public int GetHashCode(VerifyBatchResponse obj) => obj.Verdicts.Count;
    }
}
