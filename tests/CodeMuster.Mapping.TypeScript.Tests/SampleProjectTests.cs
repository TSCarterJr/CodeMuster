using CodeMuster.Domain;

namespace CodeMuster.Mapping.TypeScript.Tests;

public class SampleProjectTests(SampleProject sample) : IClassFixture<SampleProject>
{
    private const string Quotes = "site/services/quotes.ts";
    private const string CustomerPage = "site/pages/customers/[id].tsx#CustomerPage";

    [Fact]
    public void Symbols_are_function_declarations_function_valued_consts_and_class_members_of_included_files()
    {
        (string Id, string Kind)[] expected =
        [
            ($"{Quotes}#Repo.load", "method"),
            ($"{Quotes}#QuoteService.constructor", "constructor"),
            ($"{Quotes}#QuoteService.get", "method"),
            ($"{Quotes}#format", "function"),
            ($"{Quotes}#parse", "function"),
            ($"{Quotes}#build", "function"),
            ("site/components/Widget.tsx#Widget.Panel", "method"),
            ("site/pages/index.tsx#default", "function"),
            (CustomerPage, "function"),
            ("site/pages/_app.tsx#App", "function"),
            ("site/pages/_document.tsx#Document", "function"),
            ("site/pages/_error.tsx#ErrorPage", "function"),
            ("site/pages/api/health.ts#handler", "function"),
            ("site/app/(marketing)/quotes/[id]/page.tsx#QuotePage", "function"),
            ("site/app/page.tsx#default", "function"),
        ];

        Assert.Equal(
            expected.OrderBy(symbol => symbol.Id, StringComparer.Ordinal),
            sample.Map.Symbols.Select(symbol => (symbol.Id, symbol.Kind)).OrderBy(symbol => symbol.Id, StringComparer.Ordinal));
        Assert.All(sample.Map.Symbols, symbol => Assert.Equal(symbol.Id[..symbol.Id.IndexOf('#', StringComparison.Ordinal)], symbol.Path));
    }

    [Fact]
    public void Signatures_stop_at_the_body_and_class_members_start_with_the_class_header()
    {
        var signatures = sample.Map.Symbols.ToDictionary(symbol => symbol.Id, symbol => symbol.Signature);

        Assert.Equal("export class QuoteService\nconstructor(private readonly repo: Repo)", signatures[$"{Quotes}#QuoteService.constructor"]);
        Assert.Equal("export class QuoteService\nget(id: number): number", signatures[$"{Quotes}#QuoteService.get"]);
        Assert.Equal("export class Widget\nstatic Panel()", signatures["site/components/Widget.tsx#Widget.Panel"]);
        Assert.Equal("export const format = (value: number) =>", signatures[$"{Quotes}#format"]);
        Assert.Equal("export const parse = function (text: string)", signatures[$"{Quotes}#parse"]);
        Assert.Equal("export default function ()", signatures["site/pages/index.tsx#default"]);
        Assert.Equal("export default () =>", signatures["site/app/page.tsx#default"]);
    }

    [Fact]
    public void Ranges_start_after_leading_comments_and_cover_the_whole_declaration()
    {
        var ranges = sample.Map.Symbols.ToDictionary(symbol => symbol.Id, symbol => symbol.Range);

        Assert.Equal(new LineRange(3, 5), ranges[$"{Quotes}#Repo.load"]);
        Assert.Equal(new LineRange(9, 9), ranges[$"{Quotes}#QuoteService.constructor"]);
        Assert.Equal(new LineRange(18, 20), ranges[$"{Quotes}#parse"]);
        Assert.Equal(new LineRange(4, 11), ranges[CustomerPage]);
        Assert.Equal(new LineRange(1, 1), ranges["site/app/page.tsx#default"]);
    }

    [Fact]
    public void Edges_come_from_the_enclosing_symbol_and_reach_methods_constructors_member_tags_and_identifier_arguments()
    {
        Edge[] expected =
        [
            new($"{Quotes}#QuoteService.get", $"{Quotes}#Repo.load", EdgeKind.Call),
            new($"{Quotes}#build", $"{Quotes}#QuoteService.constructor", EdgeKind.Call),
            new("site/pages/index.tsx#default", "site/components/Widget.tsx#Widget.Panel", EdgeKind.Call),
            new(CustomerPage, $"{Quotes}#parse", EdgeKind.Call),
            new(CustomerPage, $"{Quotes}#format", EdgeKind.Call),
        ];

        Assert.Equal(GoldenAssert.Sorted(expected), GoldenAssert.Sorted(sample.Map.Edges));
    }

    [Fact]
    public void Entry_points_are_default_exports_of_app_pages_and_pages_routes()
    {
        EntryPoint[] expected =
        [
            new("site/pages/index.tsx#default", "page", "/"),
            new(CustomerPage, "page", "/customers/[id]"),
            new("site/app/(marketing)/quotes/[id]/page.tsx#QuotePage", "page", "/quotes/[id]"),
            new("site/app/page.tsx#default", "page", "/"),
        ];

        Assert.Equal(GoldenAssert.Sorted(expected), GoldenAssert.Sorted(sample.Map.EntryPoints));
    }

    [Fact]
    public void Resolution_counts_call_sites_inside_symbols_and_ranks_unresolved_names()
    {
        Assert.Equal(11, sample.Map.Resolution.Resolved);
        Assert.Equal(4, sample.Map.Resolution.Unresolved);
        Assert.Equal(["missingHelper", "alpha", "boot"], sample.Map.Resolution.TopUnresolvedNames);
        Assert.Empty(sample.Map.Diagnostics);
    }
}
