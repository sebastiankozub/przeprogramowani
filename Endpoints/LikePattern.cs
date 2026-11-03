namespace MovieDate.Endpoints;

/// <summary>Helpers for matching user input literally inside SQL LIKE/ILIKE patterns.</summary>
internal static class LikePattern
{
    /// <summary>The escape character used by <see cref="Escape"/>; pass it as the ILIKE escape argument.</summary>
    public const string EscapeChar = "\\";

    /// <summary>Escapes LIKE wildcards so user input is matched literally (escape character: backslash).</summary>
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
