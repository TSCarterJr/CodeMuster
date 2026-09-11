namespace MixedRepo.Api.Services;

public sealed class EmptyQuoteService : IQuoteService
{
    public IReadOnlyList<QuoteSummary> ListQuotes(int tenantId)
    {
        return [];
    }

    public QuoteSummary? GetQuote(int tenantId, int id)
    {
        return null;
    }
}
