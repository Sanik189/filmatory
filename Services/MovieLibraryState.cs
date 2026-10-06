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
    WatchlistStore watchlistStore,
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

    private readonly Dictionary<MediaKey, Movie> movieCache = [];
    private readonly HashSet<string> selectedGenreNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MediaKey, int> ratings = [];
    private readonly LinkedList<MediaKey> recentViews = [];
    private readonly List<MediaKey> watchlistKeys = [];
    private readonly HashSet<MediaKey> notInterestedKeys = [];

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

    private bool watchProviderFilterEnabled;
    private List<int> selectedProviderIds = [];

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

    public IReadOnlyList<GenreOption> AvailableGenres { get; private set; } = [];

    public string SearchQuery { get; private set; } = string.Empty;

    public string SortMode { get; private set; } = SortRecommended;

    public ContentFilter CurrentContentFilter { get; private set; } = ContentFilter.Mixed;

    public IReadOnlyCollection<string> SelectedGenreNames => selectedGenreNames;

    public Movie? SelectedMovie => SelectedMovieKey is MediaKey key ? movieCache.GetValueOrDefault(key) : null;

    public MediaKey? SelectedMovieKey { get; private set; }

    public WatchProviders? SelectedWatchProviders { get; private set; }

    public string? SelectedTrailerKey { get; private set; }

    public IReadOnlyList<WatchProviderOption> AvailableWatchProviders { get; private set; } = [];

    public IReadOnlyList<Movie> WatchlistMovies => watchlistKeys
        .Select(movieCache.GetValueOrDefault)
        .Where(movie => movie is not null)
        .Select(movie => movie!)
        .ToList();

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

    /// <summary>
    /// A stable "how big is this catalog" number for the hero stat, fetched once at startup
    /// from the unfiltered popular feed. Deliberately decoupled from <see cref="TotalResults"/>,
    /// which reflects the CURRENT filtered/personalized query and shrinks on purpose once genre
    /// personalization kicks in — showing that drop as the headline number reads as "the catalog
    /// broke" rather than "recommendations narrowed to your taste".
    /// </summary>
    public int CatalogSize { get; private set; }

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

    /// <summary>
    /// No geolocation available, so this is a best-effort region guess from the UI language
    /// rather than the user's actual country; good enough for a localized default.
    /// </summary>
    private string Region => localization.Current == AppLanguage.En ? "US" : "RU";

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
            await LoadWatchlistAsync(userId);

            var user = await userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                yearFilterEnabled = user.YearFilterEnabled;
                preferredYearFrom = user.PreferredYearFrom;
                preferredYearTo = user.PreferredYearTo;
                watchProviderFilterEnabled = user.WatchProviderFilterEnabled;
                selectedProviderIds = ParseProviderIds(user.PreferredWatchProviderIds);
            }
        }

        if (!tmdb.IsConfigured)
        {
            ErrorMessage = localization.T("error.apiKeyMissing");
            NotifyChanged();
            return;
        }

        AvailableWatchProviders = await tmdb.GetAvailableProvidersAsync(Region, TmdbLanguage);

        AvailableGenres = await BuildMergedGenresAsync();
        await LoadCatalogSizeAsync();
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

    public void SetContentFilter(ContentFilter filter)
    {
        if (CurrentContentFilter == filter)
        {
            return;
        }

        CurrentContentFilter = filter;
        _ = RefreshCatalogAsync(resetPage: true);
        _ = RunRefreshRecommendationsAsync();
    }

    public void ToggleGenre(string genreName)
    {
        if (!selectedGenreNames.Add(genreName))
        {
            selectedGenreNames.Remove(genreName);
        }

        _ = RefreshCatalogAsync(resetPage: true);
    }

    public void ClearFilters()
    {
        SearchQuery = string.Empty;
        SortMode = SortRecommended;
        selectedGenreNames.Clear();
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
        SelectedMovieKey = movie.Key;
        SelectedWatchProviders = null;
        SelectedTrailerKey = null;
        movieCache[movie.Key] = movie;
        TouchRecentView(movie.Key);
        NotifyChanged();

        _ = LoadSelectedMovieDetailAndRecommendationsAsync(movie.Key);

        if (CurrentUserId is string userId)
        {
            _ = historyStore.RecordViewAsync(userId, movie);
        }
    }

    public void CloseDetail()
    {
        SelectedMovieKey = null;
        SelectedWatchProviders = null;
        SelectedTrailerKey = null;
        NotifyChanged();
    }

    public void RateMovie(Movie movie, int rating)
    {
        if (rating is < 1 or > 5)
        {
            return;
        }

        ratings[movie.Key] = rating;
        movieCache[movie.Key] = movie;
        SelectedMovieKey = movie.Key;
        TouchRecentView(movie.Key);
        NotifyChanged();

        _ = RunRefreshRecommendationsAsync();

        if (CurrentUserId is string userId)
        {
            _ = historyStore.RecordRatingAsync(userId, movie, rating);
        }
    }

    public int GetRating(Movie movie) => ratings.TryGetValue(movie.Key, out var rating) ? rating : 0;

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

    /// <summary>Same immediate-refresh pattern as <see cref="UpdateYearPreferences"/>.</summary>
    public void UpdateWatchProviderPreferences(bool enabled, IReadOnlyList<int> providerIds)
    {
        watchProviderFilterEnabled = enabled;
        selectedProviderIds = providerIds.ToList();

        _ = RefreshCatalogAsync(resetPage: true);
        _ = RunRefreshRecommendationsAsync();
    }

    public bool IsInWatchlist(Movie movie) => watchlistKeys.Contains(movie.Key);

    public void ToggleWatchlist(Movie movie)
    {
        if (watchlistKeys.Remove(movie.Key))
        {
            NotifyChanged();

            if (CurrentUserId is string removingUserId)
            {
                _ = watchlistStore.RemoveAsync(removingUserId, movie.Key);
            }

            return;
        }

        watchlistKeys.Insert(0, movie.Key);
        movieCache[movie.Key] = movie;
        NotifyChanged();

        if (CurrentUserId is string addingUserId)
        {
            _ = watchlistStore.AddAsync(addingUserId, movie);
        }
    }

    public void RemoveFromWatchlist(MediaKey key)
    {
        if (!watchlistKeys.Remove(key))
        {
            return;
        }

        NotifyChanged();

        if (CurrentUserId is string userId)
        {
            _ = watchlistStore.RemoveAsync(userId, key);
        }
    }

    /// <summary>
    /// Lower-friction negative signal than a 1-star rating: hides the title from the catalog
    /// and recommendations immediately, feeds a negative weight into genre affinity (see
    /// <see cref="BuildGenreAffinity"/>), and is remembered so it never resurfaces.
    /// </summary>
    public void MarkNotInterested(Movie movie)
    {
        if (!notInterestedKeys.Add(movie.Key))
        {
            return;
        }

        movieCache[movie.Key] = movie;
        catalog = catalog.Where(m => m.Key != movie.Key).ToList();
        recommendations = recommendations.Where(r => r.Movie.Key != movie.Key).ToList();
        NotifyChanged();

        _ = RunRefreshRecommendationsAsync();

        if (CurrentUserId is string userId)
        {
            _ = historyStore.MarkNotInterestedAsync(userId, movie);
        }
    }

    private async Task<IReadOnlyList<GenreOption>> BuildMergedGenresAsync()
    {
        var movieGenres = await tmdb.GetGenresAsync(MediaType.Movie, TmdbLanguage);
        var tvGenres = await tmdb.GetGenresAsync(MediaType.Tv, TmdbLanguage);

        var byName = new Dictionary<string, GenreOption>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, name) in movieGenres)
        {
            byName[name] = new GenreOption(name, id, null);
        }

        foreach (var (id, name) in tvGenres)
        {
            byName[name] = byName.TryGetValue(name, out var existing)
                ? existing with { TvGenreId = id }
                : new GenreOption(name, null, id);
        }

        return byName.Values.OrderBy(g => g.Name).ToList();
    }

    private async Task LoadCatalogSizeAsync()
    {
        var results = await Task.WhenAll(
            tmdb.GetPopularAsync(MediaType.Movie, 1, TmdbLanguage),
            tmdb.GetPopularAsync(MediaType.Tv, 1, TmdbLanguage));

        CatalogSize = results.Where(r => r.Success).Sum(r => r.TotalResults);
    }

    private IReadOnlyList<MediaType> GetActiveMediaTypes() => CurrentContentFilter switch
    {
        ContentFilter.MoviesOnly => [MediaType.Movie],
        ContentFilter.SeriesOnly => [MediaType.Tv],
        _ => [MediaType.Movie, MediaType.Tv]
    };

    private List<int> ResolveSelectedGenreIds(MediaType type) => AvailableGenres
        .Where(g => selectedGenreNames.Contains(g.Name))
        .Select(g => type == MediaType.Movie ? g.MovieGenreId : g.TvGenreId)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .ToList();

    private List<int> GetTopAffinityGenreIds(MediaType type, int count)
    {
        if (AvailableGenres.Count == 0)
        {
            return [];
        }

        var affinity = BuildGenreAffinity();

        return affinity
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => AvailableGenres.FirstOrDefault(g => string.Equals(g.Name, kv.Key, StringComparison.OrdinalIgnoreCase)))
            .Where(g => g is not null)
            .Select(g => type == MediaType.Movie ? g!.MovieGenreId : g!.TvGenreId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Take(count)
            .ToList();
    }

    private async Task LoadHistoryAsync(string userId)
    {
        var history = await historyStore.GetAllAsync(userId);

        foreach (var entry in history.OrderBy(h => h.ViewedAt))
        {
            var genres = DeserializeGenres(entry.GenresJson);
            var key = new MediaKey(entry.Type, entry.MovieId);
            var movie = new Movie(key, entry.Title, string.Empty, entry.Year, "—", 0, 0, 0, entry.PosterUrl, null, genres);

            movieCache[key] = movie;

            // "Не интересно" is a terminal negative judgment: it should never feed the mildly
            // positive "recently viewed" affinity bucket, even if the row also has a ViewedAt
            // from before it was dismissed.
            if (entry.NotInterested)
            {
                notInterestedKeys.Add(key);
                continue;
            }

            TouchRecentView(key);

            if (entry.Rating is int rating)
            {
                ratings[key] = rating;
            }
        }
    }

    private async Task LoadWatchlistAsync(string userId)
    {
        var items = await watchlistStore.GetAllAsync(userId);

        foreach (var item in items)
        {
            var key = new MediaKey(item.Type, item.MovieId);
            movieCache.TryAdd(key, new Movie(key, item.Title, string.Empty, item.Year, "—", 0, 0, 0, item.PosterUrl, null, []));
            watchlistKeys.Add(key);
        }
    }

    private static List<int> ParseProviderIds(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

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

    private async Task LoadSelectedMovieDetailAndRecommendationsAsync(MediaKey key)
    {
        var detailTask = tmdb.GetDetailAsync(key.Type, key.Id, TmdbLanguage);
        var providersTask = tmdb.GetWatchProvidersAsync(key.Type, key.Id, Region);
        var trailerTask = tmdb.GetTrailerKeyAsync(key.Type, key.Id, TmdbLanguage);
        await Task.WhenAll(detailTask, providersTask, trailerTask);

        if (SelectedMovieKey == key)
        {
            var detail = detailTask.Result;
            if (detail is not null)
            {
                movieCache[key] = detail;
                catalog = catalog.Select(m => m.Key == key ? detail : m).ToList();
            }

            SelectedWatchProviders = providersTask.Result;
            SelectedTrailerKey = trailerTask.Result;
        }

        await RefreshRecommendationsAsync();
        NotifyChanged();
    }

    /// <summary>
    /// TMDb's /popular and /search endpoints don't accept a watch-provider filter (discover-only
    /// API limitation), so the provider filter only narrows the two discover-based branches below.
    /// </summary>
    private (IReadOnlyCollection<int>? Ids, string? Region) GetWatchProviderFilter() =>
        watchProviderFilterEnabled && selectedProviderIds.Count > 0
            ? (selectedProviderIds, Region)
            : (null, null);

    private async Task<(TmdbResult Result, bool Personalized)> FetchCatalogPageAsync(MediaType type, int page, int? yearFrom, int? yearTo)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            return (await tmdb.SearchAsync(type, SearchQuery, page, TmdbLanguage), false);
        }

        var (providerIds, providerRegion) = GetWatchProviderFilter();

        if (selectedGenreNames.Count == 0 && SortMode == SortRecommended)
        {
            // The "по рекомендациям" sort must actually reflect taste, not just re-show what's
            // popular. When we have rating history, drive the catalog off the user's top
            // affinity genres sorted by rating quality; only fall back to plain popularity
            // when there's no history yet to personalize from.
            var genreIds = GetTopAffinityGenreIds(type, 3);

            if (genreIds.Count > 0)
            {
                return (await tmdb.DiscoverAsync(type, genreIds, "vote_average.desc", page, TmdbLanguage, yearFrom, yearTo, watchProviderIds: providerIds, watchRegion: providerRegion), true);
            }

            return (await tmdb.GetPopularAsync(type, page, TmdbLanguage), false);
        }

        var selectedIds = ResolveSelectedGenreIds(type);
        return (await tmdb.DiscoverAsync(type, selectedIds, MapSortMode(SortMode), page, TmdbLanguage, yearFrom, yearTo, watchProviderIds: providerIds, watchRegion: providerRegion), false);
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
        var types = GetActiveMediaTypes();
        var fetched = await Task.WhenAll(types.Select(t => FetchCatalogPageAsync(t, currentPage, yearFrom, yearTo)));

        if (version != catalogRequestVersion)
        {
            return;
        }

        IsLoading = false;
        IsLoadingMore = false;
        IsPersonalizedCatalog = fetched.Any(f => f.Personalized);

        var successes = fetched.Where(f => f.Result.Success).Select(f => f.Result).ToList();

        if (successes.Count == 0)
        {
            ErrorMessage = fetched[0].Result.Error;
            if (resetPage)
            {
                catalog = [];
                TotalResults = 0;
            }

            NotifyChanged();
            return;
        }

        totalPages = successes.Max(r => r.TotalPages);
        TotalResults = successes.Sum(r => r.TotalResults);

        var merged = SortMerged(successes.SelectMany(r => r.Movies).Where(m => !notInterestedKeys.Contains(m.Key)).ToList());

        foreach (var movie in merged)
        {
            movieCache[movie.Key] = movie;
        }

        catalog = resetPage ? merged : [.. catalog, .. merged];

        NotifyChanged();
    }

    private List<Movie> SortMerged(List<Movie> items) => SortMode switch
    {
        SortNewest => items.OrderByDescending(m => m.Year ?? 0).ThenByDescending(m => m.Popularity).ToList(),
        SortTitle => items.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
        SortPopular => items.OrderByDescending(m => m.Popularity).ToList(),
        _ => items.OrderByDescending(m => m.VoteAverage).ThenByDescending(m => m.Popularity).ToList()
    };

    /// <summary>
    /// Builds the personalized rail from several of the user's best-rated titles (not just the
    /// most recently viewed one), pulling TMDb's own "recommendations"/"similar" results per seed,
    /// weighting by how highly the seed was rated, capping how much any single seed can contribute
    /// (for variety), filtering out already-watched titles, low-quality results and (optionally)
    /// titles outside the user's preferred release-year range, then re-ranking by a blend of TMDb
    /// relevance, genre affinity built from the whole rating history, and recency. Seeds and
    /// candidates stay within whichever media type(s) the active content filter allows; a movie
    /// seed's recommendations are always other movies (TMDb doesn't cross-recommend types), so a
    /// mixed filter naturally yields a mixed rail without any extra merging logic here.
    /// </summary>
    private async Task RefreshRecommendationsAsync()
    {
        IsLoadingRecommendations = true;

        var (yearFrom, yearTo) = GetEffectiveYearRange();
        var activeTypes = new HashSet<MediaType>(GetActiveMediaTypes());

        var ratedMovies = movieCache.Values
            .Where(m => ratings.ContainsKey(m.Key) && activeTypes.Contains(m.Type))
            .Select(m => (Movie: m, Rating: ratings[m.Key]))
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

        var watchedKeys = new HashSet<MediaKey>(ratings.Keys.Concat(recentViews).Concat(watchlistKeys).Concat(notInterestedKeys));

        if (seeds.Count == 0)
        {
            var popularResults = await Task.WhenAll(activeTypes.Select(t => tmdb.GetPopularAsync(t, 1, TmdbLanguage)));
            recommendations = popularResults
                .Where(r => r.Success)
                .SelectMany(r => r.Movies)
                .Where(m => !watchedKeys.Contains(m.Key) && IsWithinYearRange(m, yearFrom, yearTo))
                .OrderByDescending(m => m.Popularity)
                .Take(6)
                .Select(movie => new MovieRecommendation(movie, movie.Popularity, localization.T("recs.reason.popular"), []))
                .ToList();

            IsLoadingRecommendations = false;
            return;
        }

        var affinity = BuildGenreAffinity();
        var gathered = new Dictionary<MediaKey, (Movie Movie, double SeedWeight, int SeedRank, RecommendationSource Source)>();

        foreach (var (seedMovie, seedRating) in seeds)
        {
            var result = await tmdb.GetRecommendationsAsync(seedMovie.Type, seedMovie.Id, TmdbLanguage);
            if (!result.Success || result.Movies.Count == 0)
            {
                result = await tmdb.GetSimilarAsync(seedMovie.Type, seedMovie.Id, TmdbLanguage);
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

                if (watchedKeys.Contains(candidate.Key))
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

                movieCache.TryAdd(candidate.Key, candidate);
                takenFromSeed++;

                if (!gathered.TryGetValue(candidate.Key, out var existing) || existing.SeedWeight < seedWeight)
                {
                    gathered[candidate.Key] = (candidate, seedWeight, rank, RecommendationSource.TmdbSimilarity);
                }
            }
        }

        // Three more signals genre affinity alone can't catch: shared themes/franchise tags
        // (keywords), shared cast/director, and direct sequels/prequels in the same collection.
        var (providerIds, providerRegion) = GetWatchProviderFilter();
        await AddKeywordMatchesAsync(seeds, gathered, watchedKeys, activeTypes, yearFrom, yearTo, providerIds, providerRegion);
        await AddPeopleMatchesAsync(seeds, gathered, watchedKeys, activeTypes, yearFrom, yearTo, providerIds, providerRegion);
        await AddCollectionMatchesAsync(seeds, gathered, watchedKeys, yearFrom, yearTo);

        recommendations = gathered.Values
            .Select(entry => new MovieRecommendation(
                entry.Movie,
                GetRecommendationScore(entry.Movie, affinity, entry.SeedWeight, entry.SeedRank),
                DescribeRecommendationFor(entry.Movie, affinity, entry.Source),
                entry.Movie.Genres.Where(affinity.ContainsKey).OrderByDescending(g => affinity[g]).Take(3).ToArray()))
            .OrderByDescending(recommendation => recommendation.Score)
            .Take(6)
            .ToList();

        IsLoadingRecommendations = false;
    }

    private enum RecommendationSource
    {
        TmdbSimilarity,
        Keyword,
        People,
        Collection
    }

    /// <summary>
    /// Genre affinity alone can't catch a cross-genre overlap like "a superhero title that's
    /// also a detective story" — rating Spider-Man movies and detective movies highly only
    /// builds affinity for "Action"/"Crime" separately, never surfacing something that sits at
    /// their intersection. TMDb's keyword tags (character names, franchise, themes like "noir")
    /// are the sharpest signal it offers for that, so this fetches the keywords of the rated
    /// seeds, weights them by how highly each seed was rated (same shape as genre affinity),
    /// and runs one extra discover query per active media type against the top keywords
    /// (OR'd together) to surface titles genre affinity would never have reached. Keyword
    /// matches get a quality floor than the TMDb-recommendations-sourced candidates: the keyword
    /// match is itself a strong relevance signal, so a lower vote count shouldn't disqualify it.
    /// </summary>
    private async Task AddKeywordMatchesAsync(
        List<(Movie Movie, int Rating)> seeds,
        Dictionary<MediaKey, (Movie Movie, double SeedWeight, int SeedRank, RecommendationSource Source)> gathered,
        HashSet<MediaKey> watchedKeys,
        HashSet<MediaType> activeTypes,
        int? yearFrom,
        int? yearTo,
        IReadOnlyCollection<int>? providerIds,
        string? providerRegion)
    {
        var seedKeywordLists = await Task.WhenAll(seeds.Select(s => tmdb.GetKeywordsAsync(s.Movie.Type, s.Movie.Id)));

        var keywordAffinity = new Dictionary<int, double>();
        for (var i = 0; i < seeds.Count; i++)
        {
            var weight = seeds[i].Rating switch
            {
                5 => 1.6,
                4 => 1.35,
                3 => 1.0,
                _ => 0.8
            };

            foreach (var (id, _) in seedKeywordLists[i])
            {
                keywordAffinity[id] = keywordAffinity.TryGetValue(id, out var existing) ? existing + weight : weight;
            }
        }

        var topKeywordIds = keywordAffinity
            .OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => kv.Key)
            .ToList();

        if (topKeywordIds.Count == 0)
        {
            return;
        }

        const double keywordMatchWeight = 1.25;

        foreach (var type in activeTypes)
        {
            var result = await tmdb.DiscoverAsync(type, [], "vote_average.desc", 1, TmdbLanguage, yearFrom, yearTo, topKeywordIds, watchProviderIds: providerIds, watchRegion: providerRegion);
            if (!result.Success)
            {
                continue;
            }

            var rank = 0;
            var taken = 0;

            foreach (var candidate in result.Movies)
            {
                rank++;

                if (watchedKeys.Contains(candidate.Key) || candidate.VoteCount < 15)
                {
                    continue;
                }

                if (!IsWithinYearRange(candidate, yearFrom, yearTo))
                {
                    continue;
                }

                if (taken >= 4)
                {
                    break;
                }

                movieCache.TryAdd(candidate.Key, candidate);
                taken++;

                if (!gathered.TryGetValue(candidate.Key, out var existing) || existing.SeedWeight < keywordMatchWeight)
                {
                    gathered[candidate.Key] = (candidate, keywordMatchWeight, rank, RecommendationSource.Keyword);
                }
            }
        }
    }

    /// <summary>
    /// Classic "you liked their other work": fetches the director (movies only — TV credits
    /// don't reliably expose a single per-show director) and top-billed cast for each seed,
    /// weights by rating, then discovers titles sharing those people. A stronger signal than
    /// keywords (an actor/director match is rarely a coincidence), so it gets a higher weight.
    /// </summary>
    private async Task AddPeopleMatchesAsync(
        List<(Movie Movie, int Rating)> seeds,
        Dictionary<MediaKey, (Movie Movie, double SeedWeight, int SeedRank, RecommendationSource Source)> gathered,
        HashSet<MediaKey> watchedKeys,
        HashSet<MediaType> activeTypes,
        int? yearFrom,
        int? yearTo,
        IReadOnlyCollection<int>? providerIds,
        string? providerRegion)
    {
        var seedPeopleLists = await Task.WhenAll(seeds.Select(s => tmdb.GetKeyPeopleIdsAsync(s.Movie.Type, s.Movie.Id)));

        var peopleAffinity = new Dictionary<int, double>();
        for (var i = 0; i < seeds.Count; i++)
        {
            var weight = seeds[i].Rating switch
            {
                5 => 1.6,
                4 => 1.35,
                3 => 1.0,
                _ => 0.8
            };

            foreach (var personId in seedPeopleLists[i])
            {
                peopleAffinity[personId] = peopleAffinity.TryGetValue(personId, out var existing) ? existing + weight : weight;
            }
        }

        var topPeopleIds = peopleAffinity
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => kv.Key)
            .ToList();

        if (topPeopleIds.Count == 0)
        {
            return;
        }

        const double peopleMatchWeight = 1.3;

        foreach (var type in activeTypes)
        {
            var result = await tmdb.DiscoverAsync(type, [], "vote_average.desc", 1, TmdbLanguage, yearFrom, yearTo, peopleIds: topPeopleIds, watchProviderIds: providerIds, watchRegion: providerRegion);
            if (!result.Success)
            {
                continue;
            }

            var rank = 0;
            var taken = 0;

            foreach (var candidate in result.Movies)
            {
                rank++;

                if (watchedKeys.Contains(candidate.Key) || candidate.VoteCount < 15)
                {
                    continue;
                }

                if (!IsWithinYearRange(candidate, yearFrom, yearTo))
                {
                    continue;
                }

                if (taken >= 4)
                {
                    break;
                }

                movieCache.TryAdd(candidate.Key, candidate);
                taken++;

                if (!gathered.TryGetValue(candidate.Key, out var existing) || existing.SeedWeight < peopleMatchWeight)
                {
                    gathered[candidate.Key] = (candidate, peopleMatchWeight, rank, RecommendationSource.People);
                }
            }
        }
    }

    /// <summary>
    /// Direct franchise completion: if a rated movie belongs to a TMDb collection (e.g. a
    /// sequel series), its other parts are near-certain relevance, so they get the highest
    /// weight of any source. TV has no collection concept in TMDb. Capped at 2 parts per seed
    /// so one big franchise can't fill the whole rail on its own.
    /// </summary>
    private async Task AddCollectionMatchesAsync(
        List<(Movie Movie, int Rating)> seeds,
        Dictionary<MediaKey, (Movie Movie, double SeedWeight, int SeedRank, RecommendationSource Source)> gathered,
        HashSet<MediaKey> watchedKeys,
        int? yearFrom,
        int? yearTo)
    {
        const double collectionMatchWeight = 1.5;

        var movieSeeds = seeds.Where(s => s.Movie.Type == MediaType.Movie).ToList();

        foreach (var (seedMovie, _) in movieSeeds)
        {
            var detail = await tmdb.GetDetailAsync(MediaType.Movie, seedMovie.Id, TmdbLanguage);
            if (detail?.CollectionId is not int collectionId)
            {
                continue;
            }

            var parts = await tmdb.GetCollectionPartsAsync(collectionId, TmdbLanguage);
            var taken = 0;

            foreach (var candidate in parts.OrderByDescending(p => p.Year ?? 0))
            {
                if (watchedKeys.Contains(candidate.Key))
                {
                    continue;
                }

                if (!IsWithinYearRange(candidate, yearFrom, yearTo))
                {
                    continue;
                }

                if (taken >= 2)
                {
                    break;
                }

                movieCache.TryAdd(candidate.Key, candidate);
                taken++;

                if (!gathered.TryGetValue(candidate.Key, out var existing) || existing.SeedWeight < collectionMatchWeight)
                {
                    gathered[candidate.Key] = (candidate, collectionMatchWeight, 1, RecommendationSource.Collection);
                }
            }
        }
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

    private static string MapSortMode(string sortMode) => sortMode switch
    {
        SortPopular => "popularity.desc",
        SortNewest => "primary_release_date.desc",
        SortTitle => "original_title.asc",
        _ => "popularity.desc"
    };

    private string DescribeRecommendationFor(Movie movie, IReadOnlyDictionary<string, double> affinity, RecommendationSource source = RecommendationSource.TmdbSimilarity)
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

        // These signals are specific enough (and often cross-genre, which is the whole point
        // of having them) that they're more informative than falling back to a generic genre
        // match once the strongest genre+rating combo above doesn't apply.
        switch (source)
        {
            case RecommendationSource.Collection:
                return localization.T("recs.reason.collection");
            case RecommendationSource.People:
                return localization.T("recs.reason.people");
            case RecommendationSource.Keyword:
                return localization.T("recs.reason.themes");
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

        foreach (var key in recentViews)
        {
            if (!movieCache.TryGetValue(key, out var movie))
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

        foreach (var (key, rating) in ratings)
        {
            if (!movieCache.TryGetValue(key, out var movie))
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

        // "Не интересно" is a weaker negative than an explicit 1-star rating (the user never
        // actually watched it, just dismissed it on sight), but should still push its genres down.
        const double notInterestedWeight = -0.6;

        foreach (var key in notInterestedKeys)
        {
            if (!movieCache.TryGetValue(key, out var movie))
            {
                continue;
            }

            foreach (var genre in movie.Genres)
            {
                affinity[genre] = affinity.TryGetValue(genre, out var existing)
                    ? existing + notInterestedWeight
                    : notInterestedWeight;
            }
        }

        return affinity;
    }

    private void TouchRecentView(MediaKey key)
    {
        recentViews.Remove(key);
        recentViews.AddFirst(key);

        while (recentViews.Count > RecentViewLimit)
        {
            recentViews.RemoveLast();
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
