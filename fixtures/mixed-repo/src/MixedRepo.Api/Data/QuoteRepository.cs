namespace MixedRepo.Api.Data;

public sealed class QuoteRepository
{
    private readonly List<Quote> _quotes;

    public QuoteRepository()
    {
        _quotes =
        [
            new Quote(1, 1, "Acme <b>Ltd</b>", 1200.50m, "Open"),
            new Quote(2, 1, "Globex", 300m, "Archived"),
            new Quote(3, 2, "Initech", 950.75m, "Open"),
        ];
    }

    public IReadOnlyList<Quote> ListForTenant(int tenantId)
    {
        return _quotes.Where(q => q.Status == "Open").ToList();
    }

    public Quote? FindForTenant(int tenantId, int id)
    {
        return _quotes.FirstOrDefault(q => q.TenantId == tenantId && q.Id == id);
    }
}
