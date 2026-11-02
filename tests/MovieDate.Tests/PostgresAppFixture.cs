using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    public Task InitializeAsync() => _postgres.StartAsync();

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
