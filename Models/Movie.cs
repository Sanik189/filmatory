namespace POP.Models;

public sealed record Movie(
    int Id,
    string Title,
    string Overview,
    int? Year,
    int? Runtime,
    decimal VoteAverage,
    int VoteCount,
    double Popularity,
    string? PosterUrl,
    string? BackdropUrl,
    IReadOnlyList<string> Genres)
{
    public string DurationLabel => Runtime is int minutes and > 0
        ? $"{minutes / 60} ч {minutes % 60:D2} мин"
        : "—";

    public string ScoreLabel => VoteAverage > 0 ? VoteAverage.ToString("0.0") : "—";
}

public sealed record MovieRecommendation(
    Movie Movie,
    double Score,
    string Reason,
    IReadOnlyList<string> MatchingGenres);
