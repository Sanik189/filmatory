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
    private readonly Dictionary<string, Dictionary<int, string>> genreCacheByLanguage = [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<IReadOnlyList<(int Id, string Name)>> GetGenresAsync(string language, CancellationToken ct = default)
    {
        var map = await EnsureGenresAsync(language, ct);
        return map
            .Select(pair => (Id: pair.Key, Name: pair.Value))
            .OrderBy(genre => genre.Name)
            .ToArray();
    }

    public Task<TmdbResult> GetPopularAsync(int page, string language, CancellationToken ct = default) =>
        FetchAsync($"movie/popular?page={page}", language, ct);

    public Task<TmdbResult> SearchAsync(string query, int page, string language, CancellationToken ct = default) =>
        FetchAsync($"search/movie?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false", language, ct);

    public Task<TmdbResult> DiscoverAsync(
        IReadOnlyCollection<int> genreIds,
        string sortBy,
        int page,
        string language,
        int? yearFrom = null,
        int? yearTo = null,
        CancellationToken ct = default)
    {
        var url = $"discover/movie?page={page}&sort_by={sortBy}&vote_count.gte=20";

        if (genreIds.Count > 0)
        {
            url += $"&with_genres={string.Join(',', genreIds)}";
        }

        if (yearFrom is int from)
        {
            url += $"&primary_release_date.gte={from:0000}-01-01";
        }

        if (yearTo is int to)
        {
            url += $"&primary_release_date.lte={to:0000}-12-31";
        }

        return FetchAsync(url, language, ct);
    }

    public Task<TmdbResult> GetRecommendationsAsync(int movieId, string language, CancellationToken ct = default) =>
        FetchAsync($"movie/{movieId}/recommendations", language, ct);

    public Task<TmdbResult> GetSimilarAsync(int movieId, string language, CancellationToken ct = default) =>
        FetchAsync($"movie/{movieId}/similar", language, ct);

    public async Task<Movie?> GetMovieDetailAsync(int movieId, string language, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        try
        {
            var genreMap = await EnsureGenresAsync(language, ct);
            var dto = await httpClient.GetFromJsonAsync<TmdbMovieDto>(BuildUrl($"movie/{movieId}", language), JsonOptions, ct);

            return dto is null ? null : MapMovie(dto, ResolveGenreNames(dto, genreMap), language);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb movie {MovieId}", movieId);
            return null;
        }
    }

    private async Task<TmdbResult> FetchAsync(string relativeUrl, string language, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return TmdbResult.Failed(language == "en-US"
                ? "TMDb API key is not configured. Add it to appsettings.json (TMDb:ApiKey)."
                : "API-ключ TMDb не настроен. Добавьте его в appsettings.json (раздел TMDb:ApiKey).");
        }

        try
        {
            var genreMap = await EnsureGenresAsync(language, ct);
            var payload = await httpClient.GetFromJsonAsync<TmdbPagedResponse<TmdbMovieDto>>(BuildUrl(relativeUrl, language), JsonOptions, ct);

            if (payload is null)
            {
                return TmdbResult.Failed(language == "en-US" ? "Empty response from TMDb." : "Пустой ответ от TMDb.");
            }

            var movies = payload.Results
                .Select(dto => MapMovie(dto, ResolveGenreNames(dto, genreMap), language))
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

    private static IReadOnlyList<string> ResolveGenreNames(TmdbMovieDto dto, IReadOnlyDictionary<int, string> genreMap)
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

    private Movie MapMovie(TmdbMovieDto dto, IReadOnlyList<string> genres, string language)
    {
        int? year = DateTime.TryParse(dto.ReleaseDate, out var date) ? date.Year : null;
        var title = string.IsNullOrWhiteSpace(dto.Title)
            ? (language == "en-US" ? "Untitled" : "Без названия")
            : dto.Title;
        var overview = string.IsNullOrWhiteSpace(dto.Overview)
            ? (language == "en-US" ? "No description available yet." : "Описание пока недоступно.")
            : dto.Overview;

        return new Movie(
            dto.Id,
            title,
            overview,
            year,
            dto.Runtime,
            dto.VoteAverage,
            dto.VoteCount,
            dto.Popularity,
            BuildImageUrl(dto.PosterPath, options.PosterSize),
            BuildImageUrl(dto.BackdropPath, options.BackdropSize),
            genres);
    }

    private string? BuildImageUrl(string? path, string size) =>
        string.IsNullOrWhiteSpace(path) ? null : $"{options.ImageBaseUrl}{size}{path}";

    private async Task<Dictionary<int, string>> EnsureGenresAsync(string language, CancellationToken ct)
    {
        if (genreCacheByLanguage.TryGetValue(language, out var cached))
        {
            return cached;
        }

        await genreLock.WaitAsync(ct);
        try
        {
            if (genreCacheByLanguage.TryGetValue(language, out cached))
            {
                return cached;
            }

            if (!IsConfigured)
            {
                return genreCacheByLanguage[language] = [];
            }

            var response = await httpClient.GetFromJsonAsync<TmdbGenreListResponse>(BuildUrl("genre/movie/list", language), JsonOptions, ct);
            return genreCacheByLanguage[language] = response?.Genres.ToDictionary(g => g.Id, g => g.Name) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Failed to load TMDb genre list");
            return genreCacheByLanguage[language] = [];
        }
        finally
        {
            genreLock.Release();
        }
    }

    private string BuildUrl(string relativeUrl, string language)
    {
        var separator = relativeUrl.Contains('?') ? '&' : '?';
        return $"{relativeUrl}{separator}api_key={options.ApiKey}&language={language}";
    }
}
