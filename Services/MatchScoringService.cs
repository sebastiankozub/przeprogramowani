namespace MovieDate.Services;

/// <summary>
/// The matching business rule as pure, DB-free formulas (PRD §Business Logic). Every input is symmetric,
/// so score(A, B) == score(B, A).
/// <list type="bullet">
/// <item>Taste <c>t = r / (r + 4)</c> with <c>r = 2H + W</c> (H = shared favorites, W = shared watchlist).</item>
/// <item>Proximity <c>p(d) = 1 / (1 + (d / 100)^k)</c>, <c>k = ln 3 / ln 5</c>: p(0) = 1, p(100) = 0.5, p(500) = 0.25, never 0.</item>
/// <item>Score <c>50·t + 50·p</c>, range (0, 100].</item>
/// </list>
/// </summary>
public static class MatchScoringService
{
    /// <summary>A movie rated at least this is a favorite.</summary>
    public const int FavoriteThreshold = 7;

    public const double EarthRadiusKm = 6371;

    /// <summary>Distance at which proximity is exactly 0.5.</summary>
    public const double HalfProximityKm = 100;

    /// <summary>Chosen so that p(500 km) = 0.25: (500/100)^k = 3.</summary>
    public static readonly double ProximityExponent = Math.Log(3) / Math.Log(5);

    /// <summary>Taste "half-saturation" constant: r = 4 (e.g. two shared favorites) gives t = 0.5.</summary>
    public const double TasteHalfSaturation = 4;

    public const double TasteWeight = 50;
    public const double ProximityWeight = 50;

    /// <summary>Great-circle (haversine) distance in km between two points given in degrees.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        // Clamp guards against rounding pushing the argument just above 1 for (near-)antipodal points.
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }

    /// <summary>Distance-decayed proximity in (0, 1]; 1 at 0 km.</summary>
    public static double Proximity(double km) =>
        1 / (1 + Math.Pow(Math.Max(km, 0) / HalfProximityKm, ProximityExponent));

    /// <summary>Taste in [0, 1); 0 when nothing is shared.</summary>
    public static double Taste(int sharedFavorites, int sharedWatchlist)
    {
        double r = 2 * sharedFavorites + sharedWatchlist;
        return r <= 0 ? 0 : r / (r + TasteHalfSaturation);
    }

    /// <summary>Composite score in (0, 100].</summary>
    public static double Score(int sharedFavorites, int sharedWatchlist, double km) =>
        TasteWeight * Taste(sharedFavorites, sharedWatchlist) + ProximityWeight * Proximity(km);

    /// <summary>A strong candidate shares at least one favorite or watchlist movie.</summary>
    public static bool IsStrong(int sharedFavorites, int sharedWatchlist) => sharedFavorites + sharedWatchlist >= 1;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
