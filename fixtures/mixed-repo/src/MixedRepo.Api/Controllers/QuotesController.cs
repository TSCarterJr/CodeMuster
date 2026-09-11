using Microsoft.AspNetCore.Mvc;
using MixedRepo.Api.Services;

namespace MixedRepo.Api.Controllers;

[ApiController]
public sealed class QuotesController(IQuoteService quotes) : ControllerBase
{
    [HttpGet("quotes")]
    public IReadOnlyList<QuoteSummary> ListQuotes(int tenantId)
    {
        return quotes.ListQuotes(tenantId);
    }

    [HttpGet("quotes/{id}")]
    public ActionResult<QuoteSummary> GetQuote(int tenantId, int id)
    {
        var quote = quotes.GetQuote(tenantId, id);
        if (quote is null)
        {
            return NotFound();
        }

        return quote;
    }
}
