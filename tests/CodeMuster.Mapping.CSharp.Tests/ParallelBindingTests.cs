using CodeMuster.Domain;

namespace CodeMuster.Mapping.CSharp.Tests;

public class ParallelBindingTests
{
    [Fact]
    public async Task Mapping_a_fixture_repeatedly_gives_byte_identical_maps()
    {
        var first = CodeMapJson.Serialize(await Fixtures.MapInPlaceAsync("mixed-repo", "MixedRepo.sln"));

        for (var run = 0; run < 3; run++)
        {
            Assert.Equal(first, CodeMapJson.Serialize(await Fixtures.MapInPlaceAsync("mixed-repo", "MixedRepo.sln")));
        }
    }

    [Fact]
    public async Task The_first_document_in_order_wins_a_shared_symbol_id()
    {
        var files = Enumerable.Range(0, 64)
            .Select(index => ($"src/F{index:D2}.cs", $$"""
                namespace N;

                file static class Formatter
                {
                    public static string Line(int value)
                    {
                        return value.ToString() + "{{index}}";
                    }
                }
                """))
            .ToArray();

        for (var run = 0; run < 5; run++)
        {
            var map = await Inline.MapAsync(files);

            Assert.Equal("src/F00.cs", Assert.Single(map.Symbols).Path);
        }
    }
}
