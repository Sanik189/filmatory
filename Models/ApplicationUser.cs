using Microsoft.AspNetCore.Identity;

namespace POP.Models;

public sealed class ApplicationUser : IdentityUser
{
    public bool YearFilterEnabled { get; set; }

    public int? PreferredYearFrom { get; set; }

    public int? PreferredYearTo { get; set; }

    public bool WatchProviderFilterEnabled { get; set; }

    /// <summary>Comma-separated TMDb provider IDs, e.g. "8,119". Null/empty = none selected.</summary>
    public string? PreferredWatchProviderIds { get; set; }
}
