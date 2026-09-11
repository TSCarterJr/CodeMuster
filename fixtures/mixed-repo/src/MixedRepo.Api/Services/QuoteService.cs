using MixedRepo.Api.Data;
using MixedRepo.Api.Shared;

namespace MixedRepo.Api.Services;

public sealed class QuoteService(QuoteRepository repository) : IQuoteService
{
    public IReadOnlyList<QuoteSummary> ListQuotes(int tenantId)
    {
        return repository.ListForTenant(tenantId).Select(ToSummary).ToList();
    }

    public QuoteSummary? GetQuote(int tenantId, int id)
    {
        var quote = repository.FindForTenant(tenantId, id);
        return quote is null ? null : ToSummary(quote);
    }

    public string ArchiveQuote(int id)
    {
        return $"Quote {id} archived";
    }

    private static QuoteSummary ToSummary(Quote quote)
    {
        return new QuoteSummary(quote.Id, quote.Customer, Money.Format(quote.Total), quote.Status);
    }
}
