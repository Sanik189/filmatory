using Microsoft.AspNetCore.Identity;

namespace POP.Models;

public sealed class ApplicationUser : IdentityUser
{
    public bool YearFilterEnabled { get; set; }

    public int? PreferredYearFrom { get; set; }

    public int? PreferredYearTo { get; set; }
}
