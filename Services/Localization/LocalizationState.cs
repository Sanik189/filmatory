namespace POP.Services.Localization;

public sealed class LocalizationState(IHttpContextAccessor httpContextAccessor)
{
    private AppLanguage? current;

    public AppLanguage Current
    {
        get
        {
            if (current is null)
            {
                var cookie = httpContextAccessor.HttpContext?.Request.Cookies["lang"];
                current = cookie == "en" ? AppLanguage.En : AppLanguage.Ru;
            }

            return current.Value;
        }
    }

    public string TmdbLanguage => Current == AppLanguage.En ? "en-US" : "ru-RU";

    public string T(string key) => Translations.Get(key, Current);

    public string T(string key, params object[] args) => string.Format(Translations.Get(key, Current), args);
}
