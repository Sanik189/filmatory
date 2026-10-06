using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace POP.Services.Localization;

/// <summary>
/// Reading the language straight from the request cookie works for the initial static
/// prerender, but once the Blazor Server circuit takes over for live interactivity,
/// HttpContext is no longer reliably available (a well-known Blazor Server limitation),
/// so a fresh instance in that scope would silently fall back to the wrong default.
/// PersistentComponentState carries the value computed during prerender across that
/// boundary instead — the same mechanism ASP.NET Core Identity uses for auth state.
/// </summary>
public sealed class LocalizationState : IDisposable
{
    private readonly PersistentComponentState persistentState;
    private readonly PersistingComponentStateSubscription subscription;
    private readonly AppLanguage current;

    public LocalizationState(IHttpContextAccessor httpContextAccessor, PersistentComponentState persistentState)
    {
        this.persistentState = persistentState;

        if (persistentState.TryTakeFromJson<string>("lang", out var persisted))
        {
            current = persisted == "en" ? AppLanguage.En : AppLanguage.Ru;
        }
        else
        {
            var cookie = httpContextAccessor.HttpContext?.Request.Cookies["lang"];
            current = cookie == "en" ? AppLanguage.En : AppLanguage.Ru;
        }

        subscription = persistentState.RegisterOnPersisting(PersistLanguage, RenderMode.InteractiveServer);
    }

    public AppLanguage Current => current;

    public string TmdbLanguage => current == AppLanguage.En ? "en-US" : "ru-RU";

    public string T(string key) => Translations.Get(key, current);

    public string T(string key, params object[] args) => string.Format(Translations.Get(key, current), args);

    private Task PersistLanguage()
    {
        persistentState.PersistAsJson("lang", current == AppLanguage.En ? "en" : "ru");
        return Task.CompletedTask;
    }

    public void Dispose() => subscription.Dispose();
}
