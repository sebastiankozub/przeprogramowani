using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDate.Data;
using MovieDate.Services;

namespace MovieDate.Tests;

/// <summary>
/// Background match recompute (S-06) end to end: writes stamp and queue, the worker recomputes off the request
/// path, and <c>GET /api/matches/status</c> reports and self-heals pending lists. The database is shared with the
/// rest of the collection and the worker runs for everyone, so tests only assert on their own users' stamps and on
/// list entries that can only be added (lists never shrink), and they poll with a timeout instead of sleeping.
/// </summary>
[Collection(PostgresAppCollection.Name)]
public sealed class MatchRecomputeTests(PostgresAppFixture factory)
{
    // Seed cities (Wikidata QIDs). Distances are irrelevant here; the self-heal test gets a city of its own.
    private const string Zakopane = "Q144786";
    private const string Suwalki = "Q236083";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // Hands out distinct catalog movies so no two tests in this class share one.
    private static int _movieCursor;

    private record StatusBody(bool Pending, bool ProfileComplete);

    private record MatchBody(string UserId);

    private record MeBody(string Email);

    private sealed record User(HttpClient Client, string Id) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private record Stamps(DateTimeOffset? RequestedAt, DateTimeOffset? ComputedAt);

    public enum Trigger
    {
        WatchedAdd,
        WatchedUpdate,
        WatchedDelete,
        WatchlistAdd,
        WatchlistDelete,
        WatchlistMarkWatched,
        City,
        DisplayName,
    }

    [Fact]
    public async Task Status_without_session_returns_401()
    {
        using var client = TestUsers.NewClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/matches/status")).StatusCode);
    }

    [Fact]
    public async Task Rating_a_shared_movie_puts_both_users_on_each_others_lists_without_a_recompute_call()
    {
        var movies = await UniqueMovies(1);
        using var a = await NewUser("Tło A", Zakopane);
        using var b = await NewUser("Tło B", Zakopane);

        await Rate(a, movies[0], 8);
        await Rate(b, movies[0], 9);

        await WaitUntil(async () =>
            (await List(a)).Any(m => m.UserId == b.Id) && (await List(b)).Any(m => m.UserId == a.Id));
    }

    [Theory]
    [InlineData(Trigger.WatchedAdd)]
    [InlineData(Trigger.WatchedUpdate)]
    [InlineData(Trigger.WatchedDelete)]
    [InlineData(Trigger.WatchlistAdd)]
    [InlineData(Trigger.WatchlistDelete)]
    [InlineData(Trigger.WatchlistMarkWatched)]
    [InlineData(Trigger.City)]
    [InlineData(Trigger.DisplayName)]
    public async Task Every_trigger_write_makes_the_list_pending_until_the_worker_has_run(Trigger trigger)
    {
        var movies = await UniqueMovies(1);
        var movieId = movies[0];
        using var user = await NewUser("Wyzwalacz", Zakopane);
        switch (trigger)
        {
            case Trigger.WatchedUpdate or Trigger.WatchedDelete:
                await Rate(user, movieId, 5);
                break;
            case Trigger.WatchlistDelete or Trigger.WatchlistMarkWatched:
                await AddToWatchlist(user, movieId);
                break;
        }

        await WaitUntilSettled(user);
        var before = await GetStamps(user.Id);

        var response = trigger switch
        {
            Trigger.WatchedAdd => await user.Client.PostAsJsonAsync("/api/watched", new { movieId, rating = 8 }),
            Trigger.WatchedUpdate => await user.Client.PutAsJsonAsync($"/api/watched/{movieId}", new { rating = 9 }),
            Trigger.WatchedDelete => await user.Client.DeleteAsync($"/api/watched/{movieId}"),
            Trigger.WatchlistAdd => await user.Client.PostAsJsonAsync("/api/watchlist", new { movieId }),
            Trigger.WatchlistDelete => await user.Client.DeleteAsync($"/api/watchlist/{movieId}"),
            Trigger.WatchlistMarkWatched =>
                await user.Client.PostAsJsonAsync($"/api/watchlist/{movieId}/watched", new { rating = 7 }),
            Trigger.City => await user.Client.PutAsJsonAsync("/api/profile/city",
                new { cityId = await CityId(Suwalki) }),
            Trigger.DisplayName => await user.Client.PutAsJsonAsync("/api/profile/display-name",
                new { displayName = "Nowe imię" }),
            _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
        };
        Assert.True(response.IsSuccessStatusCode, $"{trigger} returned {response.StatusCode}.");

        Assert.Equal(new StatusBody(true, true), await Status(user));
        Assert.True((await GetStamps(user.Id)).RequestedAt > before.RequestedAt);

        await WaitUntilSettled(user);
        Assert.True((await GetStamps(user.Id)).ComputedAt > before.ComputedAt);
        Assert.Equal(new StatusBody(false, true), await Status(user));
    }

    [Fact]
    public async Task Error_outcomes_do_not_request_a_recompute()
    {
        var movies = await UniqueMovies(2);
        using var user = await NewUser("Błąd", Zakopane);
        await Rate(user, movies[0], 6);
        await WaitUntilSettled(user);
        var before = await GetStamps(user.Id);
        Assert.NotNull(before.RequestedAt);

        var attempts = new (Func<Task<HttpResponseMessage>> Send, HttpStatusCode Expected)[]
        {
            (() => user.Client.PostAsJsonAsync("/api/watched", new { movieId = movies[1], rating = 11 }), HttpStatusCode.BadRequest),
            (() => user.Client.PostAsJsonAsync("/api/watched", new { movieId = movies[0], rating = 7 }), HttpStatusCode.Conflict),
            (() => user.Client.PostAsJsonAsync("/api/watched", new { movieId = int.MaxValue, rating = 7 }), HttpStatusCode.NotFound),
            (() => user.Client.PutAsJsonAsync($"/api/watched/{movies[0]}", new { rating = 0 }), HttpStatusCode.BadRequest),
            (() => user.Client.PutAsJsonAsync($"/api/watched/{movies[1]}", new { rating = 7 }), HttpStatusCode.NotFound),
            (() => user.Client.DeleteAsync($"/api/watched/{movies[1]}"), HttpStatusCode.NotFound),
            (() => user.Client.PostAsJsonAsync("/api/watchlist", new { movieId = int.MaxValue }), HttpStatusCode.NotFound),
            (() => user.Client.DeleteAsync($"/api/watchlist/{movies[1]}"), HttpStatusCode.NotFound),
            (() => user.Client.PostAsJsonAsync($"/api/watchlist/{movies[1]}/watched", new { rating = 7 }), HttpStatusCode.NotFound),
            (() => user.Client.PutAsJsonAsync("/api/profile/city", new { cityId = int.MaxValue }), HttpStatusCode.NotFound),
            (() => user.Client.PutAsJsonAsync("/api/profile/display-name", new { displayName = "   " }), HttpStatusCode.BadRequest),
        };

        // Sequential: each request is sent only when invoked below.
        foreach (var (send, expected) in attempts)
        {
            Assert.Equal(expected, (await send()).StatusCode);
        }

        Assert.Equal(before, await GetStamps(user.Id));
        Assert.Equal(new StatusBody(false, true), await Status(user));
    }

    [Fact]
    public async Task A_burst_of_writes_within_the_quiet_period_runs_one_recompute()
    {
        var movies = await UniqueMovies(4);
        using var user = await NewUser("Seria", Zakopane);
        await WaitUntilSettled(user);
        var computedBefore = (await GetStamps(user.Id)).ComputedAt;
        Assert.NotNull(computedBefore);

        // Each write lands well within the quiet period of the previous one, so nothing may run in between.
        foreach (var movieId in movies)
        {
            await Rate(user, movieId, 8);
            Assert.Equal(computedBefore, (await GetStamps(user.Id)).ComputedAt);
        }

        await WaitUntilSettled(user);
        var computedAfter = (await GetStamps(user.Id)).ComputedAt;
        Assert.NotEqual(computedBefore, computedAfter);

        // No second run follows.
        await Task.Delay(PostgresAppFixture.RecomputeQuietPeriod * 2 + PostgresAppFixture.RecomputePollInterval * 4);
        Assert.Equal(computedAfter, (await GetStamps(user.Id)).ComputedAt);
        Assert.Equal(new StatusBody(false, true), await Status(user));
    }

    [Fact]
    public async Task Incomplete_profile_is_never_pending_and_the_worker_skips_it()
    {
        using var user = await NewUser(null, null);
        Assert.Equal(new StatusBody(false, false), await Status(user));

        var name = await user.Client.PutAsJsonAsync("/api/profile/display-name", new { displayName = "Bez miasta" });
        Assert.Equal(HttpStatusCode.OK, name.StatusCode);
        Assert.Equal(new StatusBody(false, false), await Status(user));

        // The queued job runs, finds no city, and stamps nothing.
        await WaitUntil(() => Task.FromResult(!Queue.Contains(user.Id)));
        var stamps = await GetStamps(user.Id);
        Assert.NotNull(stamps.RequestedAt);
        Assert.Null(stamps.ComputedAt);
        Assert.Empty(await List(user));

        // Completing the profile makes it pending, and the worker is still alive to compute it.
        var city = await user.Client.PutAsJsonAsync("/api/profile/city", new { cityId = await CityId(Zakopane) });
        Assert.Equal(HttpStatusCode.OK, city.StatusCode);
        Assert.Equal(new StatusBody(true, true), await Status(user));
        await WaitUntilSettled(user);
        Assert.NotNull((await GetStamps(user.Id)).ComputedAt);
    }

    [Fact]
    public async Task A_never_computed_user_gets_a_list_through_the_status_check_alone()
    {
        using var partner = await NewUser("Samonaprawa B", Suwalki);
        using var user = await NewUser("Samonaprawa A", Suwalki);
        await WaitUntilSettled(partner);
        await WaitUntilSettled(user);

        // Simulate an account from before S-06: complete profile, no stamps, no list.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Matches.Where(m => m.UserId == user.Id || m.CandidateId == user.Id).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.MatchesRequestedAt, (DateTimeOffset?)null)
                .SetProperty(u => u.MatchesComputedAt, (DateTimeOffset?)null));
        }

        Assert.False(Queue.Contains(user.Id));
        Assert.Equal(new StatusBody(true, true), await Status(user));
        Assert.True(Queue.Contains(user.Id));

        await WaitUntilSettled(user);
        Assert.NotNull((await GetStamps(user.Id)).ComputedAt);
        Assert.NotEmpty(await List(user));
    }

    private MatchRecomputeQueue Queue => factory.Services.GetRequiredService<MatchRecomputeQueue>();

    private async Task<User> NewUser(string? displayName, string? cityWikidataId)
    {
        var client = await TestUsers.NewSignedInClient(factory);
        if (displayName is not null)
        {
            var set = await client.PutAsJsonAsync("/api/profile/display-name", new { displayName });
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        if (cityWikidataId is not null)
        {
            var set = await client.PutAsJsonAsync("/api/profile/city", new { cityId = await CityId(cityWikidataId) });
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        // Test-only lookup: the API never exposes the caller's own id, so resolve it from the session's email.
        var me = await client.GetFromJsonAsync<MeBody>("/api/auth/me");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = await db.Users.Where(u => u.Email == me!.Email).Select(u => u.Id).SingleAsync();
        return new User(client, id);
    }

    private async Task<int> CityId(string wikidataId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Cities.Where(c => c.WikidataId == wikidataId).Select(c => c.Id).SingleAsync();
    }

    private async Task<int[]> UniqueMovies(int count)
    {
        var skip = Interlocked.Add(ref _movieCursor, count) - count;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await db.Movies.OrderBy(m => m.Id).Skip(skip).Take(count).Select(m => m.Id).ToArrayAsync();
        Assert.Equal(count, ids.Length);
        return ids;
    }

    private async Task<Stamps> GetStamps(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new Stamps(u.MatchesRequestedAt, u.MatchesComputedAt))
            .SingleAsync();
    }

    private static async Task<StatusBody> Status(User user)
    {
        var response = await user.Client.GetAsync("/api/matches/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StatusBody>())!;
    }

    private static async Task<List<MatchBody>> List(User user)
    {
        var response = await user.Client.GetAsync("/api/matches");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<MatchBody>>() ?? [];
    }

    private static async Task Rate(User user, int movieId, int rating)
    {
        var response = await user.Client.PostAsJsonAsync("/api/watched", new { movieId, rating });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AddToWatchlist(User user, int movieId)
    {
        var response = await user.Client.PostAsJsonAsync("/api/watchlist", new { movieId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Waits until the user's (complete-profile) list has been computed after their last request and nothing is
    /// queued for them. Reads the stamps directly: polling the status endpoint would re-enqueue the user while
    /// their job is running and leave a stray recompute behind.
    /// </summary>
    private Task WaitUntilSettled(User user) => WaitUntil(async () =>
    {
        if (Queue.Contains(user.Id))
        {
            return false;
        }

        var stamps = await GetStamps(user.Id);
        return stamps.ComputedAt is not null
               && (stamps.RequestedAt is null || stamps.ComputedAt >= stamps.RequestedAt);
    });

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(stopwatch.Elapsed < Timeout, $"Condition not met within {Timeout}.");
            await Task.Delay(50);
        }
    }
}

/// <summary>Due-selection of the debounce queue against a hand-driven clock.</summary>
public sealed class MatchRecomputeQueueTests
{
    private static readonly MatchRecomputeOptions Options = new()
    {
        QuietPeriod = TimeSpan.FromSeconds(2),
        MaxDelay = TimeSpan.FromSeconds(5),
    };

    private readonly ManualTime _time = new();
    private readonly MatchRecomputeQueue _queue;

    public MatchRecomputeQueueTests() =>
        _queue = new MatchRecomputeQueue(Microsoft.Extensions.Options.Options.Create(Options), _time);

    [Fact]
    public void A_user_is_due_once_the_quiet_period_has_elapsed()
    {
        _queue.Enqueue("a");

        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Empty(_queue.TakeDue());
        Assert.True(_queue.Contains("a"));

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["a"], _queue.TakeDue());
    }

    [Fact]
    public void Re_enqueueing_while_waiting_restarts_the_quiet_period()
    {
        _queue.Enqueue("a");
        _time.Advance(TimeSpan.FromSeconds(1.5));
        _queue.Enqueue("a");

        _time.Advance(TimeSpan.FromSeconds(1.5));
        Assert.Empty(_queue.TakeDue());

        _time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(["a"], _queue.TakeDue());
    }

    [Fact]
    public void A_continuous_burst_is_due_at_the_max_delay()
    {
        _queue.Enqueue("a");
        for (var i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            _queue.Enqueue("a");
            Assert.Empty(_queue.TakeDue());
        }

        _time.Advance(TimeSpan.FromSeconds(1)); // 5 s after the first request, 1 s after the last
        Assert.Equal(["a"], _queue.TakeDue());
    }

    [Fact]
    public void Taking_removes_only_the_due_users()
    {
        _queue.Enqueue("a");
        _time.Advance(TimeSpan.FromSeconds(1));
        _queue.Enqueue("b");
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["a"], _queue.TakeDue());
        Assert.False(_queue.Contains("a"));
        Assert.True(_queue.Contains("b"));
        Assert.Empty(_queue.TakeDue());
    }

    [Fact]
    public void Enqueueing_after_a_take_starts_a_new_window()
    {
        _queue.Enqueue("a");
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(["a"], _queue.TakeDue());

        _queue.Enqueue("a");
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(_queue.TakeDue());
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["a"], _queue.TakeDue());
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
