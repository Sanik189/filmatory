using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using POP.Models;
using POP.Services.Localization;
using POP.Services.Tmdb;

namespace POP.Services;

public sealed class MovieLibraryState(
    TmdbService tmdb,
    WatchHistoryStore historyStore,
    AuthenticationStateProvider authStateProvider,
    LocalizationState localization,
    UserManager<ApplicationUser> userManager)
{
    private const int RecentViewLimit = 8;
    private const int MaxSeeds = 4;
    private const int MaxPerSeed = 3;
    private const string SortRecommended = "recommended";
    private const string SortPopular = "popular";
    private const string SortNewest = "newest";
    private const string SortTitle = "title";

    private readonly Dictionary<int, Movie> movieCache = [];
    private readonly HashSet<int> selectedGenreIds = [];
    private readonly Dictionary<int, int> ratings = [];
    private readonly LinkedList<int> recentViews = [];

    private List<Movie> catalog = [];
    private List<MovieRecommendation> recommendations = [];
    private CancellationTokenSource? searchDebounceCts;
    private int catalogRequestVersion;
    private int currentPage = 1;
    private int totalPages = 1;
    private bool initialized;

    private bool yearFilterEnabled;
    private int? preferredYearFrom;
    private int? preferredYearTo;

    public event Action? Changed;

    public IReadOnlyList<Movie> CatalogMovies => catalog;

    public IReadOnlyList<MovieRecommendation> Recommendations => recommendations;

    public IReadOnlyList<(string Value, string Label)> SortOptions =>
    [
        (SortRecommended, localization.T("sort.recommended")),
        (SortPopular, localization.T("sort.popular")),
        (SortNewest, localization.T("sort.newest")),
        (SortTitle, localization.T("sort.title"))
    ];

    public IReadOnlyList<(int Id, string Name)> AvailableGenres { get; private set; } = [];

    public string SearchQuery { get; private set; } = string.Empty;

    public string SortMode { get; private set; } = SortRecommended;

    public IReadOnlyCollection<int> SelectedGenreIds => selectedGenreIds;

    public Movie? SelectedMovie => SelectedMovieId is int id ? movieCache.GetValueOrDefault(id) : null;

    public int? SelectedMovieId { get; private set; }

    public IReadOnlyList<Movie> RecentMovies => recentViews
        .Select(movieCache.GetValueOrDefault)
        .Where(movie => movie is not null)
        .Select(movie => movie!)
        .ToList();

    public bool IsLoading { get; private set; }

    public bool IsLoadingMore { get; private set; }

    public bool IsLoadingRecommendations { get; private set; }

    public string? ErrorMessage { get; private set; }

    public bool CanLoadMore => !IsLoading && !IsLoadingMore && currentPage < totalPages && catalog.Count > 0;

    public int TotalResults { get; private set; }

    public int TotalRatings => ratings.Count;

    public bool IsAuthenticated { get; private set; }

    /// <summary>
    /// True when the "по рекомендациям" catalog sort is actually driven by the user's taste
    /// (genre affinity from rating history) rather than falling back to plain popularity.
    /// </summary>
    public bool IsPersonalizedCatalog { get; private set; }

    public string StatusSummary => TotalResults > 0
        ? localization.T("status.withTotal", catalog.Count, TotalResults)
        : localization.T("status.simple", catalog.Count);

    private string TmdbLanguage => localization.TmdbLanguage;

    private string? CurrentUserId { get; set; }

    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userId = authState.User.Identity?.IsAuthenticated == true
            ? authState.User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;

        CurrentUserId = userId;
        IsAuthenticated = userId is not null;

        if (userId is not null)
        {
            await LoadHistoryAsync(userId);

            var user = await userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                yearFilterEnabled = user.YearFilterEnabled;
                preferredYearFrom = user.PreferredYearFrom;
                preferredYearTo = user.PreferredYearTo;
            }
        }

        if (!tmdb.IsConfigured)
        {
            ErrorMessage = localization.T("error.apiKeyMissing");
            NotifyChanged();
            return;
        }

        AvailableGenres = await tmdb.GetGenresAsync(TmdbLanguage);
        await RefreshCatalogAsync(resetPage: true);

        if (ratings.Count > 0)
        {
            await RefreshRecommendationsAsync();
            NotifyChanged();
        }
    }

    public void SetSearchQuery(string query)
    {
        SearchQuery = query.TrimStart();
        NotifyChanged();

        searchDebounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        searchDebounceCts = cts;

        _ = RunSearchDebounceAsync(cts.Token);
    }

    public void SetSortMode(string sortMode)
    {
        SortMode = sortMode switch
        {
            SortPopular or SortNewest or SortTitle => sortMode,
            _ => SortRecommended
        };

        _ = RefreshCatalogAsync(resetPage: true);
    }

    public void ToggleGenre(int genreId)
    {
        if (!selectedGenreIds.Add(genreId))
        {
            selectedGenreIds.Remove(genreId);
        }

        _ = RefreshCatalogAsync(resetPage: true);
    }

    public void ClearFilters()
    {
        SearchQuery = string.Empty;
        SortMode = SortRecommended;
        selectedGenreIds.Clear();
        _ = RefreshCatalogAsync(resetPage: true);
    }

    public void LoadMore()
    {
        if (!CanLoadMore)
        {
            return;
        }

        currentPage++;
        _ = RefreshCatalogAsync(resetPage: false);
    }

    public void SelectMovie(Movie movie)
    {
        SelectedMovieId = movie.Id;
        movieCache[movie.Id] = movie;
        TouchRecentView(movie.Id);
        NotifyChanged();

        _ = LoadSelectedMovieDetailAndRecommendationsAsync(movie.Id);

        if (CurrentUserId is string userId)
        {
            _ = historyStore.RecordViewAsync(userId, movie);
        }
    }

    public void CloseDetail()
    {
        SelectedMovieId = null;
        NotifyChanged();
    }

    public void RateMovie(Movie movie, int rating)
    {
        if (rating is < 1 or > 5)
        {
            return;
        }

        ratings[movie.Id] = rating;
        movieCache[movie.Id] = movie;
        SelectedMovieId = movie.Id;
        TouchRecentView(movie.Id);
        NotifyChanged();

        _ = RunRefreshRecommendationsAsync();

        if (CurrentUserId is string userId)
        {
            _ = historyStore.RecordRatingAsync(userId, movie, rating);
        }
    }

    public int GetRating(Movie movie) => ratings.TryGetValue(movie.Id, out var rating) ? rating : 0;

    public string DescribeRecommendation(Movie movie) => DescribeRecommendationFor(movie, BuildGenreAffinity());

    /// <summary>
    /// Called by the profile page right after it persists new year-range preferences, so the
    /// current session's recommendations reflect the change immediately without a full reload.
    /// </summary>
    public void UpdateYearPreferences(bool enabled, int? from, int? to)
    {
        yearFilterEnabled = enabled;
        preferredYearFrom = from;
        preferredYearTo = to;

        _ = RefreshCatalogAsync(resetPage: true);
        _ = RunRefreshRecommendationsAsync();
    }

    private async Task LoadHistoryAsync(string userId)
    {
        var history = await historyStore.GetAllAsync(userId);

        foreach (var entry in history.OrderBy(h => h.ViewedAt))
        {
            var genres = DeserializeGenres(entry.GenresJson);
            var movie = new Movie(
                entry.MovieId,
                entry.Title,
                string.Empty,
                entry.Year,
                null,
                0,
                0,
                0,
                entry.PosterUrl,
                null,
                genres);

            movieCache[movie.Id] = movie;
            TouchRecentView(movie.Id);

            if (entry.Rating is int rating)
            {
                ratings[movie.Id] = rating;
            }
        }
    }

    private static IReadOnlyList<string> DeserializeGenres(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task RunSearchDebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await RefreshCatalogAsync(resetPage: true);
    }

    private async Task RunRefreshRecommendationsAsync()
    {
        await RefreshRecommendationsAsync();
        NotifyChanged();
    }

    private async Task LoadSelectedMovieDetailAndRecommendationsAsync(int movieId)
    {
        var detail = await tmdb.GetMovieDetailAsync(movieId, TmdbLanguage);
        if (detail is not null && SelectedMovieId == movieId)
        {
            movieCache[movieId] = detail;
            catalog = catalog.Select(m => m.Id == movieId ? detail : m).ToList();
        }

        await RefreshRecommendationsAsync();
        NotifyChanged();
    }

    private async Task RefreshCatalogAsync(bool resetPage)
    {
        if (resetPage)
        {
            currentPage = 1;
        }

        var version = ++catalogRequestVersion;
        IsLoading = resetPage;
        IsLoadingMore = !resetPage;
        ErrorMessage = null;
        NotifyChanged();

        var (yearFrom, yearTo) = GetEffectiveYearRange();
        TmdbResult result;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            IsPersonalizedCatalog = false;
            result = await tmdb.SearchAsync(SearchQuery, currentPage, TmdbLanguage);
        }
        else if (selectedGenreIds.Count == 0 && SortMode == SortRecommended)
        {
            // The "по рекомендациям" sort must actually reflect taste, not just re-show what's
            // popular (that was the reported bug: it looked identical to the popularity sort).
            // When we have rating history, drive the catalog off the user's top affinity genres
            // sorted by rating quality; only fall back to plain popularity when there's no
            // history yet to personalize from.
            var topGenres = GetTopAffinityGenreIds(3);
            IsPersonalizedCatalog = topGenres.Count > 0;

            result = topGenres.Count > 0
                ? await tmdb.DiscoverAsync(topGenres, "vote_average.desc", currentPage, TmdbLanguage, yearFrom, yearTo)
                : await tmdb.GetPopularAsync(currentPage, TmdbLanguage);
        }
        else
        {
            IsPersonalizedCatalog = false;
            result = await tmdb.DiscoverAsync(selectedGenreIds, MapSortMode(SortMode), currentPage, TmdbLanguage, yearFrom, yearTo);
        }

        if (version != catalogRequestVersion)
        {
            return;
        }

        IsLoading = false;
        IsLoadingMore = false;

        if (!result.Success)
        {
            ErrorMessage = result.Error;
            if (resetPage)
            {
                catalog = [];
                TotalResults = 0;
            }

            NotifyChanged();
            return;
        }

        totalPages = result.TotalPages;
        TotalResults = result.TotalResults;

        foreach (var movie in result.Movies)
        {
            movieCache[movie.Id] = movie;
        }

        catalog = resetPage ? result.Movies.ToList() : [.. catalog, .. result.Movies];

        NotifyChanged();
    }

    /// <summary>
    /// Builds the personalized rail from several of the user's best-rated movies (not just the
    /// most recently viewed one), pulling TMDb's own "recommendations"/"similar" results per seed,
    /// weighting by how highly the seed was rated, capping how much any single seed can contribute
    /// (for variety), filtering out already-watched titles, low-quality results and (optionally)
    /// movies outside the user's preferred release-year range, then re-ranking by a blend of TMDb
    /// relevance, genre affinity built from the whole rating history, and recency.
    /// </summary>
    private async Task RefreshRecommendationsAsync()
    {
        IsLoadingRecommendations = true;

        var (yearFrom, yearTo) = GetEffectiveYearRange();

        var ratedMovies = movieCache.Values
            .Where(m => ratings.ContainsKey(m.Id))
            .Select(m => (Movie: m, Rating: ratings[m.Id]))
            .ToList();

        var seeds = ratedMovies
            .Where(r => r.Rating >= 4)
            .OrderByDescending(r => r.Rating)
            .ThenByDescending(r => r.Movie.Popularity)
            .Take(MaxSeeds)
            .ToList();

        if (seeds.Count == 0)
        {
            seeds = ratedMovies
                .Where(r => r.Rating == 3)
                .Take(MaxSeeds)
                .ToList();
        }

        var watchedIds = new HashSet<int>(ratings.Keys.Concat(recentViews));

        if (seeds.Count == 0)
        {
            var popular = await tmdb.GetPopularAsync(1, TmdbLanguage);
            recommendations = popular.Success
                ? popular.Movies
                    .Where(m => !watchedIds.Contains(m.Id) && IsWithinYearRange(m, yearFrom, yearTo))
                    .Take(6)
                    .Select(movie => new MovieRecommendation(movie, movie.Popularity, localization.T("recs.reason.popular"), []))
                    .ToList()
                : [];

            IsLoadingRecommendations = false;
            return;
        }

        var affinity = BuildGenreAffinity();
        var gathered = new Dictionary<int, (Movie Movie, double SeedWeight, int SeedRank)>();

        foreach (var (seedMovie, seedRating) in seeds)
        {
            var result = await tmdb.GetRecommendationsAsync(seedMovie.Id, TmdbLanguage);
            if (!result.Success || result.Movies.Count == 0)
            {
                result = await tmdb.GetSimilarAsync(seedMovie.Id, TmdbLanguage);
            }

            if (!result.Success)
            {
                continue;
            }

            var seedWeight = seedRating switch
            {
                5 => 1.4,
                4 => 1.15,
                _ => 0.9
            };

            var rank = 0;
            var takenFromSeed = 0;

            foreach (var candidate in result.Movies)
            {
                rank++;

                if (watchedIds.Contains(candidate.Id))
                {
                    continue;
                }

                if (candidate.VoteCount < 50 || candidate.VoteAverage < 6.0m)
                {
                    continue;
                }

                if (!IsWithinYearRange(candidate, yearFrom, yearTo))
                {
                    continue;
                }

                if (takenFromSeed >= MaxPerSeed)
                {
                    break;
                }

                movieCache.TryAdd(candidate.Id, candidate);
                takenFromSeed++;

                if (!gathered.TryGetValue(candidate.Id, out var existing) || existing.SeedWeight < seedWeight)
                {
                    gathered[candidate.Id] = (candidate, seedWeight, rank);
                }
            }
        }

        recommendations = gathered.Values
            .Select(entry => new MovieRecommendation(
                entry.Movie,
                GetRecommendationScore(entry.Movie, affinity, entry.SeedWeight, entry.SeedRank),
                DescribeRecommendationFor(entry.Movie, affinity),
                entry.Movie.Genres.Where(affinity.ContainsKey).OrderByDescending(g => affinity[g]).Take(3).ToArray()))
            .OrderByDescending(recommendation => recommendation.Score)
            .Take(6)
            .ToList();

        IsLoadingRecommendations = false;
    }

    private (int? From, int? To) GetEffectiveYearRange()
    {
        if (!yearFilterEnabled || preferredYearFrom is null || preferredYearTo is null)
        {
            return (null, null);
        }

        return (preferredYearFrom - 2, preferredYearTo + 2);
    }

    private static bool IsWithinYearRange(Movie movie, int? from, int? to)
    {
        if (from is null && to is null)
        {
            return true;
        }

        if (movie.Year is not int year)
        {
            return true;
        }

        if (from is int f && year < f)
        {
            return false;
        }

        if (to is int t && year > t)
        {
            return false;
        }

        return true;
    }

    private List<int> GetTopAffinityGenreIds(int count)
    {
        if (AvailableGenres.Count == 0)
        {
            return [];
        }

        var affinity = BuildGenreAffinity();
        var nameToId = AvailableGenres.ToDictionary(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);

        return affinity
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => nameToId.TryGetValue(kv.Key, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Take(count)
            .ToList();
    }

    private static string MapSortMode(string sortMode) => sortMode switch
    {
        SortPopular => "popularity.desc",
        SortNewest => "primary_release_date.desc",
        SortTitle => "original_title.asc",
        _ => "popularity.desc"
    };

    private string DescribeRecommendationFor(Movie movie, IReadOnlyDictionary<string, double> affinity)
    {
        var matchingGenres = movie.Genres
            .Where(affinity.ContainsKey)
            .OrderByDescending(genre => affinity[genre])
            .Take(2)
            .ToArray();

        var rating = GetRating(movie);

        if (matchingGenres.Length > 0 && rating >= 4)
        {
            return localization.T("recs.reason.topGenres", string.Join(", ", matchingGenres));
        }

        if (matchingGenres.Length > 0)
        {
            return localization.T("recs.reason.recentInterests", string.Join(", ", matchingGenres));
        }

        if (RecentMovies.Count == 0)
        {
            return localization.T("recs.reason.popular");
        }

        return rating >= 4
            ? localization.T("recs.reason.reinforces")
            : localization.T("recs.reason.complements");
    }

    private double GetRecommendationScore(Movie movie, IReadOnlyDictionary<string, double> affinity, double seedWeight, int seedRank)
    {
        var score = (double)movie.VoteAverage * 10 * seedWeight;
        score += Math.Max(0, 20 - seedRank);

        foreach (var genre in movie.Genres)
        {
            if (affinity.TryGetValue(genre, out var value))
            {
                score += value * 30;
            }
        }

        if (movie.Year is int year && year >= DateTime.Now.Year - 3)
        {
            score += 6;
        }

        score += Math.Min(movie.Popularity, 150) * 0.03;

        return score;
    }

    private Dictionary<string, double> BuildGenreAffinity()
    {
        var affinity = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;

        foreach (var movieId in recentViews)
        {
            if (!movieCache.TryGetValue(movieId, out var movie))
            {
                continue;
            }

            var rating = GetRating(movie);
            var recencyWeight = 1.0 - Math.Min(depth * 0.1, 0.6);
            var ratingWeight = rating switch
            {
                5 => 1.6,
                4 => 1.35,
                3 => 1.0,
                2 => 0.72,
                1 => 0.45,
                _ => 0.9
            };

            foreach (var genre in movie.Genres)
            {
                affinity[genre] = affinity.TryGetValue(genre, out var existing)
                    ? existing + recencyWeight * ratingWeight
                    : recencyWeight * ratingWeight;
            }

            depth++;
        }

        foreach (var (movieId, rating) in ratings)
        {
            if (!movieCache.TryGetValue(movieId, out var movie))
            {
                continue;
            }

            var ratingWeight = rating switch
            {
                5 => 0.9,
                4 => 0.7,
                3 => 0.25,
                2 => -0.45,
                1 => -0.7,
                _ => 0
            };

            foreach (var genre in movie.Genres)
            {
                affinity[genre] = affinity.TryGetValue(genre, out var existing)
                    ? existing + ratingWeight
                    : ratingWeight;
            }
        }

        return affinity;
    }

    private void TouchRecentView(int movieId)
    {
        recentViews.Remove(movieId);
        recentViews.AddFirst(movieId);

        while (recentViews.Count > RecentViewLimit)
        {
            recentViews.RemoveLast();
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
