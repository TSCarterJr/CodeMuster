using MixedRepo.Api.Services;

namespace MixedRepo.Api.Workers;

public sealed class ReminderWorker(IQuoteService quotes, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var open = quotes.ListQuotes(1);
            logger.LogInformation("{Count} open quotes need reminders", open.Count);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
