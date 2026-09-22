using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POP.Components;
using POP.Data;
using POP.Models;
using POP.Services;
using POP.Services.Localization;
using POP.Services.Tmdb;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection(TmdbOptions.SectionName));
builder.Services.AddHttpClient<TmdbService>(client =>
{
    client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpContextAccessor();

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=pop.db";
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();

builder.Services.AddScoped<WatchHistoryStore>();
builder.Services.AddScoped<LocalizationState>();
builder.Services.AddScoped<MovieLibraryState>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/set-language/{lang}", (string lang, string? returnUrl, HttpContext http) =>
{
    var culture = lang == "en" ? "en" : "ru";
    http.Response.Cookies.Append("lang", culture, new CookieOptions
    {
        Expires = DateTimeOffset.UtcNow.AddYears(1),
        IsEssential = true,
        SameSite = SameSiteMode.Lax
    });

    var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//")
        ? returnUrl
        : "/";

    return Results.LocalRedirect(target);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
