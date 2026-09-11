namespace MixedRepo.Api.Services;

public sealed record QuoteSummary(int Id, string Customer, string Total, string Status);

public interface IQuoteService
{
    IReadOnlyList<QuoteSummary> ListQuotes(int tenantId);

    QuoteSummary? GetQuote(int tenantId, int id);
}
