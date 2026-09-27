using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class NormalizedHashTests
{
    private const string Source = """
        export function sumPrices(items: { price: number }[]) {
          // adds the prices
          let total = 0;
          for (const item of items) {
            total += item.price;
          }
          return total;
        }

        export function   countWeights(rows: { weight: number }[]) {
          let sum = 1;
          for (const row of rows) { sum += row.weight; }
          return sum;
        }

        export function multiplyPrices(items: { price: number }[]) {
          let total = 0;
          for (const item of items) {
            total *= item.price;
          }
          return total;
        }

        export class Cart {
          total(items: { price: number }[]) {
            /** Same loop again. */
            let total = 0;
            for (const item of items) {
              total += item.price;
            }
            return total;
          }
        }

        export const deep = (a: any) => a.b.c;
        export const shallow = (a: any) => a.b;
        export const save = () => "Save";
        export const cancel = () => `Cancel`;
        export const thrower = (x: any) => { throw x; };
        export const returner = (x: any) => { return x; };
        """;

    private static async Task<Dictionary<string, Symbol>> MapAsync()
    {
        using var temp = new TempFolder();
        temp.Copy(TestPaths.TypeScriptPackage, "node_modules/typescript");
        temp.Write("tsconfig.json", """{ "compilerOptions": { "strict": true, "noEmit": true, "target": "es2022" } }""");
        temp.Write("src/sums.ts", Source + "\n");
        var map = await MapScript.RunAsync(temp.Root, ["tsconfig.json"], ["tsconfig.json", "src/sums.ts"]);
        return map.Symbols.ToDictionary(symbol => symbol.Id[(symbol.Id.IndexOf('#') + 1)..]);
    }

    [Fact]
    public async Task Bodies_that_differ_only_in_names_literals_whitespace_and_comments_share_a_normalized_hash()
    {
        var symbols = await MapAsync();

        Assert.All(symbols.Values, symbol => Assert.Matches(new Regex("^[0-9a-f]{64}$"), symbol.NormalizedHash));
        Assert.Equal(symbols["sumPrices"].NormalizedHash, symbols["countWeights"].NormalizedHash);
        Assert.Equal(symbols["sumPrices"].NormalizedHash, symbols["Cart.total"].NormalizedHash);
        Assert.NotEqual(symbols["sumPrices"].BodyHash, symbols["countWeights"].BodyHash);
        Assert.Equal(symbols["save"].NormalizedHash, symbols["cancel"].NormalizedHash);
    }

    [Fact]
    public async Task Operators_keywords_and_property_access_depth_change_the_normalized_hash()
    {
        var symbols = await MapAsync();

        Assert.NotEqual(symbols["sumPrices"].NormalizedHash, symbols["multiplyPrices"].NormalizedHash);
        Assert.NotEqual(symbols["deep"].NormalizedHash, symbols["shallow"].NormalizedHash);
        Assert.NotEqual(symbols["thrower"].NormalizedHash, symbols["returner"].NormalizedHash);
    }
}
