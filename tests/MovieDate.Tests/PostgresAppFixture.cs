using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MovieDate.Data;
using Testcontainers.PostgreSql;

namespace MovieDate.Tests;

/// <summary>
/// Starts a throwaway Postgres 17 container and boots the app against it.
/// Independent of the local docker-compose database.
/// </summary>
public sealed class PostgresAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads the connection string from
        // builder.Configuration during top-level code, before ConfigureAppConfiguration callbacks apply.
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrate once per collection through the same code path as Fly's release_command (--migrate),
        // so every test starts from the current schema regardless of execution order.
        var exitCode = await MigrationRunner.RunAsync(Services);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Test database migration failed (MigrationRunner exit code {exitCode}).");
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresAppCollection : ICollectionFixture<PostgresAppFixture>
{
    public const string Name = "Postgres app";
}
