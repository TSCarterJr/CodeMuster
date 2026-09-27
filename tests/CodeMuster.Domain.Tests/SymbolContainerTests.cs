using CodeMuster.Domain;

namespace CodeMuster.Domain.Tests;

public class SymbolContainerTests
{
    [Theory]
    [InlineData("M:MixedRepo.Api.Services.QuoteService.ListQuotes(System.Int32)", "MixedRepo.Api.Services.QuoteService")]
    [InlineData("M:Ns.Type.Method(System.Int32[])", "Ns.Type")]
    [InlineData("M:Ns.Outer.Inner.#ctor", "Ns.Outer.Inner")]
    [InlineData("M:Ns.Type.#cctor", "Ns.Type")]
    [InlineData("M:Ns.Type`1.Method``1(``0)", "Ns.Type`1")]
    [InlineData("M:Ns.Outer`1.Inner`2.Run(`0,`1)", "Ns.Outer`1.Inner`2")]
    [InlineData("M:Ns.Type.Method(System.Collections.Generic.List{System.String},Ns.Other.Thing@)", "Ns.Type")]
    [InlineData("M:Ns.Type.op_Implicit(System.Int32)~Ns.Type", "Ns.Type")]
    [InlineData("M:Ns.Type.System#IDisposable#Dispose", "Ns.Type")]
    [InlineData("M:Ns.Type.Ns#IStore{System#Int32}#Load(System.Int32)", "Ns.Type")]
    [InlineData("M:Ns.Type.get_Name", "Ns.Type")]
    [InlineData("M:Ns.Type.set_Item(System.Int32,System.String)", "Ns.Type")]
    [InlineData("M:Program.<Main>$(System.String[])", "Program")]
    [InlineData("M:Main", "")]
    public void CSharp_container_is_the_type_part_of_the_documentation_comment_id(string id, string container)
    {
        Assert.Equal(container, SymbolContainer.Of(new Symbol(id, "src/Api/Type.cs", new LineRange(1, 2), "method", "", "h")));
    }

    [Theory]
    [InlineData("web/src/api.ts", "web/src/api.ts#fetchQuotes", "web/src/api.ts")]
    [InlineData("web/src/api.client.ts", "web/src/api.client.ts#fetchQuotes", "web/src/api.client.ts")]
    [InlineData("web/src/App.tsx", "web/src/App.tsx#default", "web/src/App.tsx")]
    [InlineData("web/src/store.ts", "web/src/store.ts#QuoteStore.load", "web/src/store.ts#QuoteStore")]
    [InlineData("web/src/v1.2/store.ts", "web/src/v1.2/store.ts#QuoteStore.#refresh", "web/src/v1.2/store.ts#QuoteStore")]
    public void TypeScript_container_is_the_class_for_a_method_and_the_file_otherwise(string path, string id, string container)
    {
        Assert.Equal(container, SymbolContainer.Of(new Symbol(id, path, new LineRange(1, 2), "function", "", "h")));
    }
}
