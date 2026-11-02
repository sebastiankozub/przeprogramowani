using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;

namespace MovieDate.Tests;

[Collection(PostgresAppCollection.Name)]
public sealed class DatabaseTests(PostgresAppFixture factory)
{
    [Fact]
    public async Task Fixture_migration_applied_InitialCreate()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Guard: the app must talk to the test container, not the dev DB from appsettings.Development.json.
        Assert.Equal(factory.ConnectionString, db.Database.GetConnectionString());

        var applied = await db.Database.GetAppliedMigrationsAsync();

        Assert.Contains(applied, id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Health_db_returns_200_against_container()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/db");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
