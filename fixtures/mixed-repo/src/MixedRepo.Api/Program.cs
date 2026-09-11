using MixedRepo.Api.Data;
using MixedRepo.Api.Services;
using MixedRepo.Api.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<QuoteRepository>();
builder.Services.AddSingleton<IQuoteService, QuoteService>();
builder.Services.AddHostedService<ReminderWorker>();

var app = builder.Build();
app.MapControllers();
app.Run();
