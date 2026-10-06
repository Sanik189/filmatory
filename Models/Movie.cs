namespace POP.Models;

public enum MediaType
{
    Movie,
    Tv
}

public enum ContentFilter
{
    Mixed,
    MoviesOnly,
    SeriesOnly
}

/// <summary>
/// Movie IDs and TV show IDs are separate namespaces in TMDb and can collide (the same
/// numeric ID can refer to an unrelated movie and an unrelated show), so every piece of
/// internal state (cache, ratings, view history) keys on this pair, not the bare ID.
/// </summary>
public readonly record struct MediaKey(MediaType Type, int Id);

public sealed record Movie(
    MediaKey Key,
    string Title,
    string Overview,
    int? Year,
    string DurationLabel,
    decimal VoteAverage,
    int VoteCount,
    double Popularity,
    string? PosterUrl,
    string? BackdropUrl,
    IReadOnlyList<string> Genres,
    int? CollectionId = null)
{
    public int Id => Key.Id;

    public MediaType Type => Key.Type;

    public string ScoreLabel => VoteAverage > 0 ? VoteAverage.ToString("0.0") : "—";
}

/// <summary>
/// A genre merged by display name across the movie and TV genre lists, which use different
/// IDs (and sometimes different groupings, e.g. TV's "Action &amp; Adventure" vs movies'
/// separate "Action"/"Adventure"). Either ID can be null if that side has no matching genre.
/// </summary>
public sealed record GenreOption(string Name, int? MovieGenreId, int? TvGenreId);

public sealed record MovieRecommendation(
    Movie Movie,
    double Score,
    string Reason,
    IReadOnlyList<string> MatchingGenres);

public sealed record WatchProviderEntry(string Name, string? LogoUrl);

/// <summary>
/// Where-to-watch data (JustWatch, via TMDb) for one region. Attribution to JustWatch is
/// required by TMDb's terms whenever this is displayed.
/// </summary>
public sealed record WatchProviders(
    string? Link,
    IReadOnlyList<WatchProviderEntry> Flatrate,
    IReadOnlyList<WatchProviderEntry> Rent,
    IReadOnlyList<WatchProviderEntry> Buy)
{
    public bool HasAny => Flatrate.Count > 0 || Rent.Count > 0 || Buy.Count > 0;
}

/// <summary>One entry in the region's full provider catalog, used for the profile picker.</summary>
public sealed record WatchProviderOption(int Id, string Name, string? LogoUrl);
