using System.Text.Json;
using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class SharedParseTests
{
    private const string Seed = "web/tools/seed.ts";

    // The fixture's options with only emit settings changed: nothing that affects parsing or binding.
    private const string SameBinding = """
        {
          "compilerOptions": {
            "strict": true, "jsx": "preserve", "noEmit": true, "moduleResolution": "bundler", "module": "esnext", "target": "es2022",
            "outDir": "../out", "sourceMap": true, "declaration": true
          },
          "include": ["../lib", "../types", "seed.ts"]
        }
        """;

    private const string OtherTarget = """
        {
          "compilerOptions": {
            "strict": true, "jsx": "preserve", "noEmit": true, "moduleResolution": "bundler", "module": "esnext", "target": "es2020"
          },
          "include": ["../lib", "../types", "seed.ts"]
        }
        """;

    private static TempFolder WebWithTools(string toolsConfig)
    {
        var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepoWithTypeScript(), "web"), "web");
        temp.Write("web/tools/tsconfig.json", toolsConfig);
        temp.Write(Seed, "import { fetchQuotes } from \"../lib\";\n\nexport async function seed() {\n  return fetchQuotes(1);\n}\n");
        return temp;
    }

    private static Task<string> MapAsync(TempFolder temp, bool debug, params string[] tsconfigs) =>
        MapScript.RunRawAsync(temp.Root, tsconfigs, TestPaths.RepoPaths(temp.Root), debug);

    private static int ParsedFiles(string output) => JsonDocument.Parse(output).RootElement.GetProperty("parsed_files").GetInt32();

    private static void MatchesGoldenWithSeed(CodeMap map)
    {
        var golden = GoldenAssert.Golden();
        var seed = Assert.Single(map.Symbols, symbol => symbol.Path == Seed);
        Assert.Equal("web/tools/seed.ts#seed", seed.Id);
        Assert.Equal(GoldenAssert.Sorted(golden.Symbols), GoldenAssert.Sorted(map.Symbols.Where(symbol => symbol.Path != Seed)));
        Assert.Equal(
            GoldenAssert.Sorted([.. golden.Edges, new Edge("web/tools/seed.ts#seed", "web/lib/api.ts#fetchQuotes", EdgeKind.Call)]),
            GoldenAssert.Sorted(map.Edges));
        Assert.Equal(GoldenAssert.Sorted(golden.EntryPoints), GoldenAssert.Sorted(map.EntryPoints));
        Assert.Empty(map.Diagnostics);
    }

    [Fact]
    public async Task Files_two_tsconfigs_share_are_parsed_once_when_only_emit_options_differ()
    {
        using var temp = WebWithTools(SameBinding);
        var alone = ParsedFiles(await MapAsync(temp, debug: true, "web/tsconfig.json"));

        var both = await MapAsync(temp, debug: true, "web/tsconfig.json", "web/tools/tsconfig.json");

        MatchesGoldenWithSeed(CodeMapJson.Parse(both));
        Assert.Equal(alone + 1, ParsedFiles(both));
    }

    [Fact]
    public async Task A_different_target_parses_the_shared_files_again_and_maps_byte_for_byte_the_same()
    {
        using var same = WebWithTools(SameBinding);
        using var other = WebWithTools(OtherTarget);
        var alone = ParsedFiles(await MapAsync(other, debug: true, "web/tsconfig.json"));

        var shared = await MapAsync(same, debug: false, "web/tsconfig.json", "web/tools/tsconfig.json");
        var separate = await MapAsync(other, debug: false, "web/tsconfig.json", "web/tools/tsconfig.json");

        MatchesGoldenWithSeed(CodeMapJson.Parse(separate));
        Assert.Equal(shared, separate);
        Assert.DoesNotContain("parsed_files", shared, StringComparison.Ordinal);
        Assert.True(ParsedFiles(await MapAsync(other, debug: true, "web/tsconfig.json", "web/tools/tsconfig.json")) > alone + 1);
    }
}
