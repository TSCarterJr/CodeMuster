using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class ReferenceTests
{
    private const string Model = """
        export class Base {}

        export interface Priced {
          price(): number;
        }

        export enum Color {
          Red,
          Green,
        }

        export const LIMIT = 10;
        let counter = 0;

        export function sealed(target: unknown, context: unknown) {
          return target;
        }

        @sealed
        export class Order extends Base implements Priced {
          total = 0;

          price(): number {
            return this.total;
          }
        }

        export class Plain {
          constructor() {}
        }

        export function bump(): number {
          counter = 1;
          counter += 2;
          counter++;
          [counter] = [3];
          ({ counter } = { counter: 4 });
          return counter;
        }

        export interface Named extends Priced {}
        """;

    private const string Use = """
        import { Order, Plain, Color, LIMIT as MAX, type Priced } from "./model";
        import { bump } from "./model";
        import { helper } from "./helper";
        import { ext } from "extlib";

        export function run(items: Array<Order>): Priced {
          const order = new Order();
          new Plain();
          helper();
          ext();
          order.total = 1;
          order.total += 2;
          order.total++;
          order.price();
          const color: Color = Color.Red;
          const limit = MAX;
          return order satisfies Priced;
        }

        bump();
        """;

    private const string View = """
        export function Badge() {
          return null;
        }

        export function Page() {
          return <Badge />;
        }
        """;

    private const string Spaces = """
        import * as h from "./barrel";

        export function viaNamespace() {
          h.help();
        }
        """;

    private static readonly Dictionary<string, string> Sources = new(StringComparer.Ordinal)
    {
        ["src/model.ts"] = Model,
        ["src/use.ts"] = Use,
        ["src/view.tsx"] = View,
        ["src/helper.ts"] = "export function helper() {}",
        ["src/barrel.ts"] = """export { helper as help } from "./helper";""",
        ["src/spaces.ts"] = Spaces,
    };

    private static readonly Lazy<Task<CodeMap>> Mapped = new(MapAsync);

    private static async Task<CodeMap> MapAsync()
    {
        using var temp = new TempFolder();
        temp.Copy(TestPaths.TypeScriptPackage, "node_modules/typescript");
        temp.Write("node_modules/extlib/package.json", """{ "name": "extlib", "types": "index.d.ts", "main": "index.js" }""");
        temp.Write("node_modules/extlib/index.d.ts", "export declare function ext(): void;\n");
        temp.Write("tsconfig.json", """
            { "compilerOptions": { "strict": true, "noEmit": true, "target": "es2022", "module": "esnext", "moduleResolution": "bundler", "jsx": "preserve" } }
            """);
        foreach (var (path, source) in Sources)
        {
            temp.Write(path, source + "\n");
        }

        return await MapScript.RunAsync(temp.Root, ["tsconfig.json"], ["tsconfig.json", .. Sources.Keys.Order(StringComparer.Ordinal)]);
    }

    // "<from> <kind> @<line>:<text>", where the column must be where <text> starts on that line of the source.
    private static async Task<List<string>> ReferencesTo(string to)
    {
        var map = await Mapped.Value;
        return
        [
            .. map.References.Where(reference => reference.To == to).Select(reference =>
            {
                var text = Sources[reference.Path].Split('\n')[reference.Line - 1][(reference.Column - 1)..];
                var token = new string([.. text.TakeWhile(character => char.IsLetterOrDigit(character) || character is '_' or '$')]);
                return $"{reference.From} {reference.Kind.ToString().ToLowerInvariant()} @{reference.Line}:{token}";
            }),
        ];
    }

    [Fact]
    public async Task A_function_call_is_a_call_from_the_containing_function_and_the_import_is_an_import_from_the_file()
    {
        Assert.Equal(["src/use.ts import @2:bump", "src/use.ts call @20:bump"], await ReferencesTo("src/model.ts#bump"));
    }

    [Fact]
    public async Task A_renamed_re_export_called_through_a_namespace_reaches_the_original_function()
    {
        Assert.Equal(
            ["src/barrel.ts import @1:helper", "src/spaces.ts#viaNamespace call @4:help", "src/use.ts import @3:helper", "src/use.ts#run call @9:helper"],
            await ReferencesTo("src/helper.ts#helper"));
    }

    [Fact]
    public async Task A_method_called_through_an_instance_is_a_call_to_the_method()
    {
        Assert.Equal(["src/use.ts#run call @14:price"], await ReferencesTo("src/model.ts#Order.price"));
    }

    [Fact]
    public async Task A_jsx_element_is_a_call_to_the_component()
    {
        Assert.Equal(["src/view.tsx#Page call @6:Badge"], await ReferencesTo("src/view.tsx#Badge"));
    }

    [Fact]
    public async Task A_new_expression_calls_the_constructor_when_the_class_has_one_and_the_class_otherwise()
    {
        Assert.Equal(["src/use.ts#run call @8:Plain"], await ReferencesTo("src/model.ts#Plain.constructor"));
        Assert.Equal(["src/use.ts import @1:Plain"], await ReferencesTo("src/model.ts#Plain"));
        Assert.Equal(
            ["src/use.ts import @1:Order", "src/use.ts#run type @6:Order", "src/use.ts#run call @7:Order"],
            await ReferencesTo("src/model.ts#Order"));
    }

    [Fact]
    public async Task Assignment_compound_assignment_increment_and_destructuring_are_writes_and_other_uses_reads()
    {
        Assert.Equal(
            [
                "src/model.ts#bump write @33:counter",
                "src/model.ts#bump write @34:counter",
                "src/model.ts#bump write @35:counter",
                "src/model.ts#bump write @36:counter",
                "src/model.ts#bump write @37:counter",
                "src/model.ts#bump read @38:counter",
            ],
            await ReferencesTo("src/model.ts#counter"));
        Assert.Equal(
            [
                "src/model.ts#Order.price read @24:total",
                "src/use.ts#run write @11:total",
                "src/use.ts#run write @12:total",
                "src/use.ts#run write @13:total",
            ],
            await ReferencesTo("src/model.ts#Order.total"));
    }

    [Fact]
    public async Task Annotations_generic_arguments_and_satisfies_are_type_references()
    {
        Assert.Equal(
            [
                "src/model.ts#Order implement @20:Priced",
                "src/model.ts#Named inherit @41:Priced",
                "src/use.ts import @1:Priced",
                "src/use.ts#run type @6:Priced",
                "src/use.ts#run type @17:Priced",
            ],
            await ReferencesTo("src/model.ts#Priced"));
        Assert.Equal(["src/use.ts import @1:Color", "src/use.ts#run type @15:Color", "src/use.ts#run read @15:Color"], await ReferencesTo("src/model.ts#Color"));
    }

    [Fact]
    public async Task A_class_extends_its_base_and_implements_its_interface()
    {
        Assert.Equal(["src/model.ts#Order inherit @20:Base"], await ReferencesTo("src/model.ts#Base"));
        Assert.Contains("src/model.ts#Order implement @20:Priced", await ReferencesTo("src/model.ts#Priced"));
    }

    [Fact]
    public async Task A_decorator_is_an_attribute_reference_from_what_it_decorates()
    {
        Assert.Equal(["src/model.ts#Order attribute @19:sealed"], await ReferencesTo("src/model.ts#sealed"));
    }

    [Fact]
    public async Task An_enum_member_read_is_a_read_of_the_member()
    {
        Assert.Equal(["src/use.ts#run read @15:Red"], await ReferencesTo("src/model.ts#Color.Red"));
        Assert.Empty(await ReferencesTo("src/model.ts#Color.Green"));
    }

    [Fact]
    public async Task An_exported_constant_read_through_an_import_alias_reaches_the_constant()
    {
        Assert.Equal(["src/use.ts import @1:LIMIT", "src/use.ts#run read @16:MAX"], await ReferencesTo("src/model.ts#LIMIT"));
    }

    [Fact]
    public async Task A_reference_into_node_modules_is_not_recorded()
    {
        var map = await Mapped.Value;

        Assert.DoesNotContain(map.References, reference => reference.To.StartsWith("node_modules/", StringComparison.Ordinal));
        Assert.DoesNotContain(map.References, reference => reference.Line == 10 && reference.Path == "src/use.ts");
        Assert.DoesNotContain(map.References, reference => reference.Line == 4 && reference.Path == "src/use.ts");
    }

    [Fact]
    public async Task References_are_sorted_and_the_same_on_every_run()
    {
        var first = await Mapped.Value;
        var second = await MapAsync();

        Assert.Equal(first.References, second.References);
        Assert.Equal(
            first.References.OrderBy(reference => reference.Path, StringComparer.Ordinal).ThenBy(reference => reference.Line).ThenBy(reference => reference.Column)
                .ThenBy(reference => reference.From, StringComparer.Ordinal).ThenBy(reference => reference.To, StringComparer.Ordinal).ThenBy(reference => reference.Kind),
            first.References);
        Assert.Equal(first.References.Count, first.References.Distinct().Count());
    }
}
