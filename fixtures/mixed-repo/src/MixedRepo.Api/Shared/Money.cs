using System.Globalization;

namespace MixedRepo.Api.Shared;

public static class Money
{
    public static string Format(decimal amount)
    {
        return "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
    }
}
