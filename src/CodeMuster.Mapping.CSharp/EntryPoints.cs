using CodeMuster.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMuster.Mapping.CSharp;

internal static class EntryPoints
{
    public static IEnumerable<EntryPoint> Find(SyntaxNode root, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            var found = node switch
            {
                ClassDeclarationSyntax declaration when model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol { IsAbstract: false } type => OfType(type),
                InvocationExpressionSyntax invocation => OfMapCall(invocation, model, cancellationToken),
                _ => [],
            };
            foreach (var entry in found)
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<EntryPoint> OfType(INamedTypeSymbol type)
    {
        if (IsController(type))
        {
            var classTemplate = Template(type.GetAttributes().FirstOrDefault(attribute => NameOf(attribute.AttributeClass) == "Microsoft.AspNetCore.Mvc.RouteAttribute"));
            foreach (var action in Actions(type))
            {
                if (Entry(action, "http", Display(type, action, classTemplate)) is { } entry)
                {
                    yield return entry;
                }
            }
        }

        var start = Lineage(type).Any(IsBackgroundService)
            ? Lineage(type)
                .TakeWhile(ancestor => !IsBackgroundService(ancestor))
                .SelectMany(ancestor => ancestor.GetMembers("ExecuteAsync"))
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.IsOverride)
            : type.AllInterfaces
                .Where(face => NameOf(face) == "Microsoft.Extensions.Hosting.IHostedService")
                .SelectMany(face => face.GetMembers("StartAsync"))
                .Select(type.FindImplementationForInterfaceMember)
                .OfType<IMethodSymbol>()
                .FirstOrDefault();
        if (start is not null && Entry(start, "background", type.Name) is { } background)
        {
            yield return background;
        }
    }

    private static IEnumerable<EntryPoint> OfMapCall(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var verb = invocation.Expression is MemberAccessExpressionSyntax access
            ? access.Name.Identifier.Text switch
            {
                "MapGet" => "GET",
                "MapPost" => "POST",
                "MapPut" => "PUT",
                "MapDelete" => "DELETE",
                "MapPatch" => "PATCH",
                _ => null,
            }
            : null;
        if (verb is null
            || invocation.ArgumentList.Arguments is not [var route, var handler, ..]
            || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol map
            || NameOf(map.ContainingType) != "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"
            || model.GetConstantValue(route.Expression, cancellationToken).Value is not string pattern
            || Handler(model.GetSymbolInfo(handler.Expression, cancellationToken)) is not { MethodKind: MethodKind.Ordinary } target
            || Entry(target, "http", $"{verb} {(pattern.StartsWith('/') ? pattern : "/" + pattern)}") is not { } entry)
        {
            return [];
        }

        return [entry];
    }

    private static IMethodSymbol? Handler(SymbolInfo info) => info switch
    {
        { Symbol: IMethodSymbol method } => method,
        { CandidateReason: CandidateReason.OverloadResolutionFailure, CandidateSymbols: [IMethodSymbol single] } => single,
        _ => null,
    };

    private static bool IsController(INamedTypeSymbol type) =>
        type.GetAttributes().Any(attribute => NameOf(attribute.AttributeClass) is "Microsoft.AspNetCore.Mvc.ApiControllerAttribute" or "Microsoft.AspNetCore.Mvc.ControllerAttribute")
        || Lineage(type).Any(ancestor => NameOf(ancestor) == "Microsoft.AspNetCore.Mvc.ControllerBase");

    private static bool IsBackgroundService(INamedTypeSymbol type) => NameOf(type) == "Microsoft.Extensions.Hosting.BackgroundService";

    private static IEnumerable<IMethodSymbol> Actions(INamedTypeSymbol type)
    {
        var overridden = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var method in Lineage(type)
            .TakeWhile(ancestor => ancestor.SpecialType != SpecialType.System_Object)
            .SelectMany(ancestor => ancestor.GetMembers())
            .OfType<IMethodSymbol>())
        {
            for (var ancestor = method.OverriddenMethod; ancestor is not null; ancestor = ancestor.OverriddenMethod)
            {
                overridden.Add(ancestor.OriginalDefinition);
            }

            if (!overridden.Contains(method.OriginalDefinition) && IsAction(method))
            {
                yield return method;
            }
        }
    }

    private static bool IsAction(IMethodSymbol method) =>
        method is { MethodKind: MethodKind.Ordinary, DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsAbstract: false }
        && !method.GetAttributes().Any(attribute => NameOf(attribute.AttributeClass) == "Microsoft.AspNetCore.Mvc.NonActionAttribute");

    private static string Display(INamedTypeSymbol type, IMethodSymbol action, string? classTemplate)
    {
        var attributes = action.GetAttributes();
        var http = attributes.FirstOrDefault(attribute => attribute.AttributeClass is { } attributeClass
            && Lineage(attributeClass).Any(ancestor => NameOf(ancestor) == "Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute"));
        var verb = http?.AttributeClass?.Name is { } name && name.StartsWith("Http", StringComparison.Ordinal) && name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name["Http".Length..^"Attribute".Length].ToUpperInvariant()
            : "ANY";
        var methodTemplate = Template(http) ?? Template(attributes.FirstOrDefault(attribute => NameOf(attribute.AttributeClass) == "Microsoft.AspNetCore.Mvc.RouteAttribute"));
        var controller = type.Name.EndsWith("Controller", StringComparison.Ordinal) ? type.Name[..^"Controller".Length] : type.Name;
        var route = (classTemplate, methodTemplate) switch
        {
            (null, null) => $"/{controller}/{action.Name}",
            (_, { } absolute) when absolute.StartsWith('/') || absolute.StartsWith("~/", StringComparison.Ordinal) => absolute.TrimStart('~'),
            _ => "/" + string.Join('/', new[] { classTemplate, methodTemplate }.OfType<string>().Select(template => template.Trim('/')).Where(template => template.Length > 0)),
        };
        return verb + " " + route
            .Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", action.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Template(AttributeData? attribute) =>
        attribute?.ConstructorArguments is [{ Value: string template }, ..] ? template : null;

    private static EntryPoint? Entry(IMethodSymbol method, string kind, string display) =>
        CodeMapBuilder.Id(method) is { } id ? new EntryPoint(id, kind, display) : null;

    private static IEnumerable<INamedTypeSymbol> Lineage(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static string? NameOf(ISymbol? symbol) => symbol?.OriginalDefinition.ToDisplayString();
}
