using MinimalApi;

var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/hello", Endpoints.Hello);
app.MapPost("/echo", Endpoints.Echo);
app.Run();
