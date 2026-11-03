using MovieDate.Services;

namespace MovieDate.Tests;

/// <summary>Pins the DB-free scoring formulas (PRD §Business Logic).</summary>
public sealed class MatchScoringTests
{
    private const double Tolerance = 1e-9;

    // Seed coordinates (Data/Seed/cities.json).
    private const double WarszawaLat = 52.23, WarszawaLon = 21.01111;
    private const double KrakowLat = 50.06139, KrakowLon = 19.93722;

    [Fact]
    public void Favorite_threshold_is_7() => Assert.Equal(7, MatchScoringService.FavoriteThreshold);

    [Fact]
    public void Proximity_anchor_points()
    {
        Assert.Equal(1, MatchScoringService.Proximity(0), Tolerance);
        Assert.Equal(0.5, MatchScoringService.Proximity(100), Tolerance);
        Assert.Equal(0.25, MatchScoringService.Proximity(500), Tolerance);
    }

    [Fact]
    public void Proximity_never_reaches_zero_and_keeps_decreasing()
    {
        var far = MatchScoringService.Proximity(5000);

        Assert.True(far > 0);
        Assert.True(MatchScoringService.Proximity(1000) > far);
        Assert.True(far > MatchScoringService.Proximity(20000));
        Assert.True(MatchScoringService.Proximity(20000) > 0);
    }

    [Fact]
    public void Proximity_is_strictly_decreasing()
    {
        var previous = MatchScoringService.Proximity(0);
        foreach (var km in new[] { 1d, 10, 50, 100, 250, 500, 1000 })
        {
            var current = MatchScoringService.Proximity(km);
            Assert.True(current < previous, $"Proximity({km}) should be below the previous value.");
            previous = current;
        }
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(2, 0, 0.5)]
    [InlineData(1, 2, 0.5)]
    [InlineData(0, 4, 0.5)]
    [InlineData(1, 0, 1d / 3)]
    [InlineData(0, 1, 0.2)]
    public void Taste_curve(int sharedFavorites, int sharedWatchlist, double expected) =>
        Assert.Equal(expected, MatchScoringService.Taste(sharedFavorites, sharedWatchlist), Tolerance);

    [Fact]
    public void Taste_stays_below_one()
    {
        Assert.True(MatchScoringService.Taste(1000, 1000) < 1);
        Assert.True(MatchScoringService.Taste(3, 0) > MatchScoringService.Taste(2, 0));
    }

    [Fact]
    public void Score_is_within_0_exclusive_and_100_inclusive()
    {
        Assert.True(MatchScoringService.Score(0, 0, 20000) > 0);
        Assert.True(MatchScoringService.Score(1000, 1000, 0) <= 100);
        Assert.Equal(50, MatchScoringService.Score(0, 0, 0), Tolerance);
        Assert.Equal(50, MatchScoringService.Score(2, 0, 100), Tolerance);
    }

    [Fact]
    public void Score_is_symmetric_between_users()
    {
        var ab = MatchScoringService.DistanceKm(WarszawaLat, WarszawaLon, KrakowLat, KrakowLon);
        var ba = MatchScoringService.DistanceKm(KrakowLat, KrakowLon, WarszawaLat, WarszawaLon);

        Assert.Equal(ab, ba, Tolerance);
        Assert.Equal(MatchScoringService.Score(2, 1, ab), MatchScoringService.Score(2, 1, ba), Tolerance);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(3, 2, true)]
    public void Strong_needs_at_least_one_shared_movie(int sharedFavorites, int sharedWatchlist, bool expected) =>
        Assert.Equal(expected, MatchScoringService.IsStrong(sharedFavorites, sharedWatchlist));

    [Fact]
    public void Distance_warszawa_krakow_is_about_252_km()
    {
        var km = MatchScoringService.DistanceKm(WarszawaLat, WarszawaLon, KrakowLat, KrakowLon);

        Assert.InRange(km, 247, 257);
    }

    [Fact]
    public void Distance_to_self_is_zero() =>
        Assert.Equal(0, MatchScoringService.DistanceKm(WarszawaLat, WarszawaLon, WarszawaLat, WarszawaLon), Tolerance);
}
