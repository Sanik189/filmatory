namespace POP.Models;

/// <summary>
/// "Watch later" bookmark — deliberately separate from <see cref="WatchedMovie"/> since it
/// carries no rating/judgment signal, just intent. Excluded from future recommendation
/// candidates (no point re-suggesting something already queued) but never factored into
/// taste affinity.
/// </summary>
public sealed class WatchlistItem
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int MovieId { get; set; }

    public MediaType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? PosterUrl { get; set; }

    public int? Year { get; set; }

    public DateTime AddedAt { get; set; }
}
