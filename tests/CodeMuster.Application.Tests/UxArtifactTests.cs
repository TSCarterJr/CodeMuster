using System.Security.Cryptography;
using System.Text.Json;
using CodeMuster.Application.Tests.Fakes;

namespace CodeMuster.Application.Tests;

public class UxArtifactTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j7n8AAAAASUVORK5CYII=");

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("not_image")]
    [InlineData("traversal")]
    public async Task RejectsMissingChangedOrNonImageEvidence(string problem)
    {
        var fs = new FakeFileSystem();
        var root = Path.GetFullPath("fixture");
        var artifact = problem == "traversal" ? "../other.png" : ".codemuster/evidence/screen.png";
        if (problem != "missing") fs.BinaryFiles[Path.GetFullPath(Path.Combine(root, artifact))] = problem == "not_image" ? new byte[40] : Png;
        var hash = Convert.ToHexString(SHA256.HashData(problem == "not_image" ? new byte[40] : Png));
        if (problem == "changed") hash = new string('0', 64);
        var receipt = JsonSerializer.Serialize(new { pages = new[] { new { artifact, artifact_sha256 = hash } } });
        await Assert.ThrowsAsync<JsonException>(() => UxArtifacts.ValidateAsync(receipt, root, fs, CancellationToken.None));
    }

    [Fact]
    public async Task AcceptsAnExistingImageWithItsExactHash()
    {
        var fs = new FakeFileSystem();
        var root = Path.GetFullPath("fixture");
        const string artifact = ".codemuster/evidence/screen.png";
        fs.BinaryFiles[Path.GetFullPath(Path.Combine(root, artifact))] = Png;
        var receipt = JsonSerializer.Serialize(new { pages = new[] { new { artifact, artifact_sha256 = Convert.ToHexString(SHA256.HashData(Png)) } } });
        await UxArtifacts.ValidateAsync(receipt, root, fs, CancellationToken.None);
    }
}
