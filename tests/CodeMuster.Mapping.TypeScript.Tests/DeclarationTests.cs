using System.Text.RegularExpressions;
using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class DeclarationTests
{
    private const string Header = "export class Order extends Base implements Priced";

    private const string Source = """
        class Base {}

        export class Order extends Base implements Priced {
          static count = 0;
          private readonly lines: string[] = [];
          total!: number;

          constructor(public readonly id: number, name: string) {
            super();
          }

          price(): number {
            return this.total;
          }
        }

        export interface Priced {
          price(): number;
        }

        export type Money<T> = { amount: T };

        export enum Color {
          Red = 1,
          Green,
        }

        export const LIMIT: number = 10;
        let counter = 0;
        var legacy;
        export const handler = () => counter;
        const { a, b } = { a: 1, b: 2 };
        """;

    private static async Task<CodeMap> MapAsync(string source)
    {
        using var temp = new TempFolder();
        temp.Copy(TestPaths.TypeScriptPackage, "node_modules/typescript");
        temp.Write("tsconfig.json", """{ "compilerOptions": { "strict": true, "noEmit": true, "target": "es2022" } }""");
        temp.Write("src/model.ts", source + "\n");
        return await MapScript.RunAsync(temp.Root, ["tsconfig.json"], ["tsconfig.json", "src/model.ts"]);
    }

    [Fact]
    public async Task Types_fields_enum_members_and_plain_variables_are_declarations_and_symbols_are_unchanged()
    {
        var map = await MapAsync(Source);

        Assert.Equal(
            [
                "src/model.ts#Base class 1-1 class Base",
                "src/model.ts#Color enum 23-26 export enum Color",
                "src/model.ts#Color.Green enum_member 25-25 export enum Color\nGreen",
                "src/model.ts#Color.Red enum_member 24-24 export enum Color\nRed",
                "src/model.ts#LIMIT constant 28-28 export const LIMIT: number",
                "src/model.ts#Money type 21-21 export type Money<T>",
                $"src/model.ts#Order class 3-15 {Header}",
                $"src/model.ts#Order.count field 4-4 {Header}\nstatic count",
                $"src/model.ts#Order.id field 8-8 {Header}\npublic readonly id: number",
                $"src/model.ts#Order.lines field 5-5 {Header}\nprivate readonly lines: string[]",
                $"src/model.ts#Order.total field 6-6 {Header}\ntotal!: number",
                "src/model.ts#Priced interface 17-19 export interface Priced",
                "src/model.ts#counter variable 29-29 let counter",
                "src/model.ts#legacy variable 30-30 var legacy",
            ],
            map.Declarations.Select(declaration =>
                $"{declaration.Id} {declaration.Kind} {declaration.Range.StartLine}-{declaration.Range.EndLine} {declaration.Signature}"));
        Assert.All(map.Declarations, declaration => Assert.Matches(new Regex("^[0-9a-f]{64}$"), declaration.BodyHash));
        Assert.All(map.Declarations, declaration => Assert.Null(declaration.NormalizedHash));
        Assert.Equal(map.Declarations.Count, map.Declarations.Select(declaration => declaration.BodyHash).Distinct().Count());
        Assert.Equal(["src/model.ts#Order.constructor", "src/model.ts#Order.price", "src/model.ts#handler"], map.Symbols.Select(symbol => symbol.Id));
    }

    [Fact]
    public async Task A_whitespace_edit_keeps_each_declaration_hash_and_a_real_edit_changes_it()
    {
        var before = (await MapAsync(Source)).Declarations.ToDictionary(declaration => declaration.Id, declaration => declaration.BodyHash);

        var edited = Source.Replace("Red = 1,", "Red   =   1 ,", StringComparison.Ordinal).Replace("LIMIT: number = 10", "LIMIT: number = 11", StringComparison.Ordinal);
        var after = (await MapAsync(edited)).Declarations.ToDictionary(declaration => declaration.Id, declaration => declaration.BodyHash);

        Assert.Equal(before["src/model.ts#Color.Red"], after["src/model.ts#Color.Red"]);
        Assert.NotEqual(before["src/model.ts#LIMIT"], after["src/model.ts#LIMIT"]);
        Assert.Equal(before["src/model.ts#Priced"], after["src/model.ts#Priced"]);
    }
}
