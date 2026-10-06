using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using POP.Models;

namespace POP.Services.Tmdb;

public sealed record TmdbResult(bool Success, IReadOnlyList<Movie> Movies, int Page, int TotalPages, int TotalResults, string? Error)
{
    public static TmdbResult Ok(IReadOnlyList<Movie> movies, int page, int totalPages, int totalResults) =>
        new(true, movies, page, totalPages, totalResults, null);

    public static TmdbResult Failed(string error) =>
        new(false, [], 1, 1, 0, error);
}

public sealed class TmdbService(HttpClient httpClient, IOptions<TmdbOptions> options, ILogger<TmdbService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TmdbOptions options = options.Value;
    private readonly SemaphoreSlim genreLock = new(1, 1);
    private readonly Dictionary<(string Language, MediaType Type), Dictionary<int, string>> genreCache = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<IReadOnlyList<(int Id, string Name)>> GetGenresAsync(MediaType type, string language, CancellationToken ct = default)
    {
        var map = await EnsureGenresAsync(type, language, ct);
        return map
            .Select(pair => (Id: pair.Key, Name: pair.Value))
            .OrderBy(genre => genre.Name)
            .ToArray();
    }

    public Task<TmdbResult> GetPopularAsync(MediaType type, int page, string language, CancellationToken ct = default) =>
        FetchAsync($"{Segment(type)}/popular?page={page}", type, language, ct);

    public Task<TmdbResult> SearchAsync(MediaType type, string query, int page, string language, CancellationToken ct = default) =>
        FetchAsync($"search/{Segment(type)}?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false", type, language, ct);

    public Task<TmdbResult> DiscoverAsync(
        MediaType type,
        IReadOnlyCollection<int> genreIds,
        string sortBy,
        int page,
        string language,
        int? yearFrom = null,
        int? yearTo = null,
        IReadOnlyCollection<int>? keywordIds = null,
        IReadOnlyCollection<int>? peopleIds = null,
        IReadOnlyCollection<int>? watchProviderIds = null,
        string? watchRegion = null,
        CancellationToken ct = default)
    {
        var url = $"discover/{Segment(type)}?page={page}&sort_by={sortBy}&vote_count.gte=20";

        if (genreIds.Count > 0)
        {
            url += $"&with_genres={string.Join(',', genreIds)}";
        }

        if (keywordIds is { Count: > 0 })
        {
            // Pipe = OR: match any of these keywords, not all of them.
            url += $"&with_keywords={string.Join('|', keywordIds)}";
        }

        if (peopleIds is { Count: > 0 })
        {
            url += $"&with_people={string.Join('|', peopleIds)}";
        }

        if (watchProviderIds is { Count: > 0 } && watchRegion is not null)
        {
            url += $"&with_watch_providers={string.Join('|', watchProviderIds)}&watch_region={watchRegion}";
        }

        var dateField = type == MediaType.Movie ? "primary_release_date" : "first_air_date";

        if (yearFrom is int from)
        {
            url += $"&{dateField}.gte={from:0000}-01-01";
        }

        if (yearTo is int to)
        {
            url += $"&{dateField}.lte={to:0000}-12-31";
        }

        return FetchAsync(url, type, language, ct);
    }

    public Task<TmdbResult> GetRecommendationsAsync(MediaType type, int id, string language, CancellationToken ct = default) =>
        FetchAsync($"{Segment(type)}/{id}/recommendations", type, language, ct);

    /// <summary>
    /// TMDb's free-text tags for a title (character names, franchise, themes like "noir" or
    /// "detective") — a much sharper signal than genre alone for catching cross-genre overlaps
    /// (e.g. a superhero title that is ALSO a detective story). Keyword names are not localized
    /// by TMDb regardless of language, so no language param here.
    /// </summary>
    public async Task<IReadOnlyList<(int Id, string Name)>> GetKeywordsAsync(MediaType type, int id, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        try
        {
            var url = $"{Segment(type)}/{id}/keywords?api_key={options.ApiKey}";

            if (type == MediaType.Movie)
            {
                var response = await httpClient.GetFromJsonAsync<TmdbMovieKeywordsResponse>(url, JsonOptions, ct);
                return response?.Keywords.Select(k => (k.Id, k.Name)).ToArray() ?? [];
            }

            var tvResponse = await httpClient.GetFromJsonAsync<TmdbTvKeywordsResponse>(url, JsonOptions, ct);
            return tvResponse?.Results.Select(k => (k.Id, k.Name)).ToArray() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb {Type} {Id} keywords", type, id);
            return [];
        }
    }

    public Task<TmdbResult> GetSimilarAsync(MediaType type, int id, string language, CancellationToken ct = default) =>
        FetchAsync($"{Segment(type)}/{id}/similar", type, language, ct);

    /// <summary>
    /// The director (for movies) plus the top-billed cast — a classic "you liked their other
    /// work" signal. TV credits don't reliably expose a single "director" per episode, so this
    /// only adds a director for movies; cast works the same for both. IDs only (no language):
    /// they feed straight into with_people, names aren't needed.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetKeyPeopleIdsAsync(MediaType type, int id, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        try
        {
            var url = $"{Segment(type)}/{id}/credits?api_key={options.ApiKey}";
            var response = await httpClient.GetFromJsonAsync<TmdbCreditsDto>(url, JsonOptions, ct);

            if (response is null)
            {
                return [];
            }

            var people = new List<int>();

            if (type == MediaType.Movie)
            {
                var director = response.Crew.FirstOrDefault(c => c.Job == "Director");
                if (director is not null)
                {
                    people.Add(director.Id);
                }
            }

            people.AddRange(response.Cast.OrderBy(c => c.Order).Take(3).Select(c => c.Id));

            return people;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb {Type} {Id} credits", type, id);
            return [];
        }
    }

    /// <summary>
    /// Where this title can be streamed/rented/bought, for one region (ISO 3166-1, e.g. "US",
    /// "RU"). Sourced from JustWatch via TMDb — attribute accordingly wherever this is shown.
    /// </summary>
    public async Task<WatchProviders?> GetWatchProvidersAsync(MediaType type, int id, string region, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            var url = $"{Segment(type)}/{id}/watch/providers?api_key={options.ApiKey}";
            var response = await httpClient.GetFromJsonAsync<TmdbWatchProvidersResponse>(url, JsonOptions, ct);

            if (response is null || !response.Results.TryGetValue(region, out var regionData))
            {
                return null;
            }

            return new WatchProviders(
                regionData.Link,
                MapProviders(regionData.Flatrate),
                MapProviders(regionData.Rent),
                MapProviders(regionData.Buy));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb {Type} {Id} watch providers", type, id);
            return null;
        }
    }

    private List<WatchProviderEntry> MapProviders(List<TmdbWatchProviderDto>? providers) =>
        providers?
            .OrderBy(p => p.DisplayPriority)
            .Select(p => new WatchProviderEntry(p.ProviderName, BuildImageUrl(p.LogoPath, "w92")))
            .ToList() ?? [];

    /// <summary>
    /// The full catalog of streaming/rental services TMDb tracks for one region — used to
    /// populate the "which services do you have" picker in the profile. Uses the movie catalog
    /// for both media types since the real-world service list is effectively the same.
    /// </summary>
    public async Task<IReadOnlyList<WatchProviderOption>> GetAvailableProvidersAsync(string region, string language, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        try
        {
            var url = $"watch/providers/movie?api_key={options.ApiKey}&watch_region={region}&language={language}";
            var response = await httpClient.GetFromJsonAsync<TmdbWatchProviderListResponse>(url, JsonOptions, ct);

            return response?.Results
                .OrderBy(p => p.DisplayPriority)
                .Select(p => new WatchProviderOption(p.ProviderId, p.ProviderName, BuildImageUrl(p.LogoPath, "w92")))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb watch provider list for {Region}", region);
            return [];
        }
    }

    /// <summary>
    /// Best available YouTube trailer key. Falls back to English if nothing is tagged for the
    /// requested language (TMDb doesn't auto-fallback on /videos the way it does for titles).
    /// </summary>
    public async Task<string?> GetTrailerKeyAsync(MediaType type, int id, string language, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var key = await FetchTrailerKeyAsync(type, id, language, ct);
        if (key is not null || language == "en-US")
        {
            return key;
        }

        return await FetchTrailerKeyAsync(type, id, "en-US", ct);
    }

    private async Task<string?> FetchTrailerKeyAsync(MediaType type, int id, string language, CancellationToken ct)
    {
        try
        {
            var url = $"{Segment(type)}/{id}/videos?api_key={options.ApiKey}&language={language}";
            var response = await httpClient.GetFromJsonAsync<TmdbVideosResponse>(url, JsonOptions, ct);

            return response?.Results
                .Where(v => v.Site == "YouTube")
                .OrderByDescending(v => v.Official)
                .ThenBy(v => v.Type == "Trailer" ? 0 : v.Type == "Teaser" ? 1 : 2)
                .FirstOrDefault()
                ?.Key;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb {Type} {Id} videos", type, id);
            return null;
        }
    }

    /// <summary>
    /// All entries in the same movie collection (e.g. a franchise's other installments) —
    /// near-certain relevance if the user liked one part. Collections only exist for movies.
    /// </summary>
    public async Task<IReadOnlyList<Movie>> GetCollectionPartsAsync(int collectionId, string language, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        try
        {
            var genreMap = await EnsureGenresAsync(MediaType.Movie, language, ct);
            var response = await httpClient.GetFromJsonAsync<TmdbCollectionDto>(BuildUrl($"collection/{collectionId}", language), JsonOptions, ct);

            return response?.Parts
                .Select(dto => MapTitle(dto, MediaType.Movie, ResolveGenreNames(dto, genreMap), language))
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb collection {CollectionId}", collectionId);
            return [];
        }
    }

    public async Task<Movie?> GetDetailAsync(MediaType type, int id, string language, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            var genreMap = await EnsureGenresAsync(type, language, ct);
            var dto = await httpClient.GetFromJsonAsync<TmdbTitleDto>(BuildUrl($"{Segment(type)}/{id}", language), JsonOptions, ct);

            return dto is null ? null : MapTitle(dto, type, ResolveGenreNames(dto, genreMap), language);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb {Type} {Id}", type, id);
            return null;
        }
    }

    private async Task<TmdbResult> FetchAsync(string relativeUrl, MediaType type, string language, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return TmdbResult.Failed(language == "en-US"
                ? "TMDb API key is not configured. Add it to appsettings.json (TMDb:ApiKey)."
                : "API-ключ TMDb не настроен. Добавьте его в appsettings.json (раздел TMDb:ApiKey).");
        }

        try
        {
            var genreMap = await EnsureGenresAsync(type, language, ct);
            var payload = await httpClient.GetFromJsonAsync<TmdbPagedResponse<TmdbTitleDto>>(BuildUrl(relativeUrl, language), JsonOptions, ct);

            if (payload is null)
            {
                return TmdbResult.Failed(language == "en-US" ? "Empty response from TMDb." : "Пустой ответ от TMDb.");
            }

            var movies = payload.Results
                .Select(dto => MapTitle(dto, type, ResolveGenreNames(dto, genreMap), language))
                .ToList();

            return TmdbResult.Ok(movies, payload.Page, Math.Max(payload.TotalPages, 1), payload.TotalResults);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "TMDb request failed: {Url}", relativeUrl);
            return TmdbResult.Failed(language == "en-US"
                ? "Couldn't reach TMDb. Check your connection or API key."
                : "Не удалось получить данные от TMDb. Проверьте соединение или ключ API.");
        }
    }

    private static IReadOnlyList<string> ResolveGenreNames(TmdbTitleDto dto, IReadOnlyDictionary<int, string> genreMap)
    {
        if (dto.Genres is { Count: > 0 })
        {
            return dto.Genres.Select(g => g.Name).ToArray();
        }

        return dto.GenreIds?
            .Where(genreMap.ContainsKey)
            .Select(id => genreMap[id])
            .ToArray() ?? [];
    }

    private Movie MapTitle(TmdbTitleDto dto, MediaType type, IReadOnlyList<string> genres, string language)
    {
        var isEn = language == "en-US";
        var dateStr = type == MediaType.Movie ? dto.ReleaseDate : dto.FirstAirDate;
        int? year = DateTime.TryParse(dateStr, out var date) ? date.Year : null;

        var rawTitle = type == MediaType.Movie ? dto.Title : dto.Name;
        var title = string.IsNullOrWhiteSpace(rawTitle)
            ? (isEn ? "Untitled" : "Без названия")
            : rawTitle;
        var overview = string.IsNullOrWhiteSpace(dto.Overview)
            ? (isEn ? "No description available yet." : "Описание пока недоступно.")
            : dto.Overview;

        var durationLabel = type == MediaType.Movie
            ? FormatRuntime(dto.Runtime, isEn)
            : FormatSeasons(dto.NumberOfSeasons, isEn);

        return new Movie(
            new MediaKey(type, dto.Id),
            title,
            overview,
            year,
            durationLabel,
            dto.VoteAverage,
            dto.VoteCount,
            dto.Popularity,
            BuildImageUrl(dto.PosterPath, options.PosterSize),
            BuildImageUrl(dto.BackdropPath, options.BackdropSize),
            genres,
            type == MediaType.Movie ? dto.BelongsToCollection?.Id : null);
    }

    private static string FormatRuntime(int? minutes, bool isEn)
    {
        if (minutes is not int m || m <= 0)
        {
            return "—";
        }

        return isEn ? $"{m / 60}h {m % 60:D2}m" : $"{m / 60} ч {m % 60:D2} мин";
    }

    private static string FormatSeasons(int? seasons, bool isEn)
    {
        if (seasons is not int s || s <= 0)
        {
            return "—";
        }

        if (isEn)
        {
            return s == 1 ? "1 season" : $"{s} seasons";
        }

        var mod100 = s % 100;
        var mod10 = s % 10;
        var word = mod100 is >= 11 and <= 14
            ? "сезонов"
            : mod10 switch
            {
                1 => "сезон",
                >= 2 and <= 4 => "сезона",
                _ => "сезонов"
            };

        return $"{s} {word}";
    }

    private string? BuildImageUrl(string? path, string size) =>
        string.IsNullOrWhiteSpace(path) ? null : $"{options.ImageBaseUrl}{size}{path}";

    private async Task<Dictionary<int, string>> EnsureGenresAsync(MediaType type, string language, CancellationToken ct)
    {
        var key = (language, type);

        if (genreCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await genreLock.WaitAsync(ct);
        try
        {
            if (genreCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            if (!IsConfigured)
            {
                return genreCache[key] = [];
            }

            var response = await httpClient.GetFromJsonAsync<TmdbGenreListResponse>(BuildUrl($"genre/{Segment(type)}/list", language), JsonOptions, ct);
            return genreCache[key] = response?.Genres.ToDictionary(g => g.Id, g => g.Name) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb {Type} genre list", type);
            return genreCache[key] = [];
        }
        finally
        {
            genreLock.Release();
        }
    }

    private static string Segment(MediaType type) => type == MediaType.Movie ? "movie" : "tv";

    private string BuildUrl(string relativeUrl, string language)
    {
        var separator = relativeUrl.Contains('?') ? '&' : '?';
        return $"{relativeUrl}{separator}api_key={options.ApiKey}&language={language}";
    }
}
