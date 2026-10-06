namespace POP.Models;

public sealed class WatchedMovie
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int MovieId { get; set; }

    public MediaType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? PosterUrl { get; set; }

    public int? Year { get; set; }

    public string GenresJson { get; set; } = "[]";

    public int? Rating { get; set; }

    public bool NotInterested { get; set; }

    public DateTime ViewedAt { get; set; }

    public DateTime? RatedAt { get; set; }
}
