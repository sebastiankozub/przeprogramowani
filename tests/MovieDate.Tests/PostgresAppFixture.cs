using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MovieDate.Data;
using MovieDate.Services;
using Testcontainers.PostgreSql;

namespace MovieDate.Tests;

/// <summary>
/// Starts a throwaway Postgres 17 container and boots the app against it.
/// Independent of the local docker-compose database.
/// </summary>
public class PostgresAppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Background match recompute (S-06) with short debounce timings so tests don't wait seconds per write.
    public static readonly TimeSpan RecomputeQuietPeriod = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan RecomputeMaxDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RecomputePollInterval = TimeSpan.FromMilliseconds(50);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads the connection string from
        // builder.Configuration during top-level code, before ConfigureAppConfiguration callbacks apply.
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("MatchRecompute:QuietPeriod", RecomputeQuietPeriod.ToString("c"));
        builder.UseSetting("MatchRecompute:MaxDelay", RecomputeMaxDelay.ToString("c"));
        builder.UseSetting("MatchRecompute:PollInterval", RecomputePollInterval.ToString("c"));
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

/// <summary>
/// The same app on its own Postgres container, but without the background match recompute worker: writes still
/// stamp and queue, nothing drains the queue. For tests of the recompute rule itself (through
/// <c>POST /api/matches/recompute</c>), whose list contents would otherwise depend on when background recomputes
/// of every user in the shared database happen to run.
/// </summary>
public sealed class PostgresAppWithoutRecomputeWorkerFixture : PostgresAppFixture
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            var worker = services.Single(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(MatchRecomputeWorker));
            services.Remove(worker);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresAppWithoutRecomputeWorkerCollection
    : ICollectionFixture<PostgresAppWithoutRecomputeWorkerFixture>
{
    public const string Name = "Postgres app without recompute worker";
}
