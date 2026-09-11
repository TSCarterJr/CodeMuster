namespace CodeMuster.Mapping.TypeScript.Tests;

public class MapScriptTests
{
    private const string QuoteTable = "web/components/QuoteTable.tsx#QuoteTable";
    private const string CustomersPage = "web/app/customers/page.tsx#CustomersPage";

    private const string ReindentedQuoteTable = """
        import type { Quote } from "../lib";

        export function QuoteTable( { quotes }: { quotes: Quote[] } ) {
            return (
                <table>
                    <tbody>
                        {quotes.map( ( quote ) => (
                            <tr key={ quote.id }>
                                <td
                                    dangerouslySetInnerHTML={ { __html: quote.customer } }
                                />
                                <td>{ quote.total }</td>
                                <td>
                                    {quote.status}
                                </td>
                            </tr>
                        ) )}
                    </tbody>
                </table>
            );
        }

        """;

    [Fact]
    public async Task Maps_the_fixture_to_the_golden()
    {
        var root = TestPaths.MixedRepoWithTypeScript();

        var map = await MapScript.RunAsync(root, ["web/tsconfig.json"], TestPaths.RepoPaths(root));

        GoldenAssert.Matches(map);
    }

    [Fact]
    public async Task Reindenting_jsx_keeps_the_body_hash_and_a_real_edit_changes_it()
    {
        using var temp = new TempFolder();
        temp.Copy(Path.Combine(TestPaths.MixedRepoWithTypeScript(), "web"), "web");
        temp.Write("web/components/QuoteTable.tsx", ReindentedQuoteTable);
        var customers = File.ReadAllText(Path.Combine(temp.Root, "web", "app", "customers", "page.tsx"));
        temp.Write("web/app/customers/page.tsx", customers.Replace("<h1>Customers</h1>", "<h1>Clients</h1>", StringComparison.Ordinal));

        var map = await MapScript.RunAsync(temp.Root, ["web/tsconfig.json"], TestPaths.RepoPaths(temp.Root));

        var golden = GoldenAssert.Golden().Symbols.ToDictionary(symbol => symbol.Id);
        var actual = map.Symbols.ToDictionary(symbol => symbol.Id);
        Assert.NotEqual(golden[QuoteTable].Range, actual[QuoteTable].Range);
        Assert.Equal(golden[QuoteTable].BodyHash, actual[QuoteTable].BodyHash);
        Assert.NotEqual(golden[CustomersPage].BodyHash, actual[CustomersPage].BodyHash);
    }
}
