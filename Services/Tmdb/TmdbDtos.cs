using System.Text.Json.Serialization;

namespace POP.Services.Tmdb;

internal sealed class TmdbPagedResponse<T>
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = [];

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
}

/// <summary>
/// Shared shape for both /movie and /tv responses: movie JSON leaves the tv-only fields
/// (name, first_air_date, number_of_seasons) absent and vice versa, so one DTO maps both.
/// </summary>
internal sealed class TmdbTitleDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("vote_average")]
    public decimal VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int VoteCount { get; set; }

    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }

    [JsonPropertyName("genre_ids")]
    public List<int>? GenreIds { get; set; }

    [JsonPropertyName("genres")]
    public List<TmdbGenreDto>? Genres { get; set; }

    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    [JsonPropertyName("number_of_seasons")]
    public int? NumberOfSeasons { get; set; }

    [JsonPropertyName("belongs_to_collection")]
    public TmdbCollectionRefDto? BelongsToCollection { get; set; }
}

internal sealed class TmdbCollectionRefDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

internal sealed class TmdbCollectionDto
{
    [JsonPropertyName("parts")]
    public List<TmdbTitleDto> Parts { get; set; } = [];
}

internal sealed class TmdbGenreDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class TmdbGenreListResponse
{
    [JsonPropertyName("genres")]
    public List<TmdbGenreDto> Genres { get; set; } = [];
}

internal sealed class TmdbKeywordDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

// TMDb is inconsistent here: /movie/{id}/keywords nests the list under "keywords",
// /tv/{id}/keywords nests the same shape under "results".
internal sealed class TmdbMovieKeywordsResponse
{
    [JsonPropertyName("keywords")]
    public List<TmdbKeywordDto> Keywords { get; set; } = [];
}

internal sealed class TmdbTvKeywordsResponse
{
    [JsonPropertyName("results")]
    public List<TmdbKeywordDto> Results { get; set; } = [];
}

internal sealed class TmdbCreditsDto
{
    [JsonPropertyName("cast")]
    public List<TmdbCastMemberDto> Cast { get; set; } = [];

    [JsonPropertyName("crew")]
    public List<TmdbCrewMemberDto> Crew { get; set; } = [];
}

internal sealed class TmdbCastMemberDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

internal sealed class TmdbCrewMemberDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("job")]
    public string Job { get; set; } = string.Empty;
}

internal sealed class TmdbWatchProvidersResponse
{
    [JsonPropertyName("results")]
    public Dictionary<string, TmdbWatchProviderRegionDto> Results { get; set; } = [];
}

internal sealed class TmdbWatchProviderRegionDto
{
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("flatrate")]
    public List<TmdbWatchProviderDto>? Flatrate { get; set; }

    [JsonPropertyName("rent")]
    public List<TmdbWatchProviderDto>? Rent { get; set; }

    [JsonPropertyName("buy")]
    public List<TmdbWatchProviderDto>? Buy { get; set; }
}

internal sealed class TmdbWatchProviderDto
{
    [JsonPropertyName("provider_id")]
    public int ProviderId { get; set; }

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = string.Empty;

    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; }

    [JsonPropertyName("display_priority")]
    public int DisplayPriority { get; set; }
}

// The /watch/providers/{movie|tv} catalog-listing endpoint reuses the same per-provider
// shape as the per-title regional data above, just flattened directly under "results".
internal sealed class TmdbWatchProviderListResponse
{
    [JsonPropertyName("results")]
    public List<TmdbWatchProviderDto> Results { get; set; } = [];
}

internal sealed class TmdbVideosResponse
{
    [JsonPropertyName("results")]
    public List<TmdbVideoDto> Results { get; set; } = [];
}

internal sealed class TmdbVideoDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("site")]
    public string Site { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("official")]
    public bool Official { get; set; }
}
