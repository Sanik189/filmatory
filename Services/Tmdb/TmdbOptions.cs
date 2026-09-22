namespace POP.Services.Tmdb;

public sealed class TmdbOptions
{
    public const string SectionName = "TMDb";

    public string ApiKey { get; set; } = string.Empty;
    public string Language { get; set; } = "ru-RU";
    public string ImageBaseUrl { get; set; } = "https://image.tmdb.org/t/p/";
    public string PosterSize { get; set; } = "w342";
    public string BackdropSize { get; set; } = "w780";
}
