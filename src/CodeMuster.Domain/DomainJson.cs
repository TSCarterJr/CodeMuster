using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMuster.Domain;

/// <summary>Serializer settings shared by every JSON contract in the Domain: snake_case names, lowercase enums, strict required fields.</summary>
public static class DomainJson
{
    /// <summary>The one options instance every Domain JSON contract uses.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        WriteIndented = true,
    };
}
