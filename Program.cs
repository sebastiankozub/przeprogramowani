using MovieDate.Data;
using MovieDate.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddAppDatabase(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: [HealthEndpoints.DbTag]);

var app = builder.Build();

// `dotnet movie-date.dll --migrate`: apply pending migrations and exit without starting the web server.
if (args.Contains(MigrationRunner.Flag))
{
    return await MigrationRunner.RunAsync(app.Services);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection(); // Fly's edge proxy handles TLS/redirect in production; container only listens on plain HTTP.
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthEndpoints();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Any future app.MapGroup("/api") / other API routes must be registered ABOVE this line.
app.MapFallbackToFile("index.html"); // SPA fallback — must stay last so it doesn't swallow /health or /api/*.

app.Run();
return 0;

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
