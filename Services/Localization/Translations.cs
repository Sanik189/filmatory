namespace POP.Services.Localization;

public static class Translations
{
    private static readonly Dictionary<string, (string Ru, string En)> Map = new()
    {
        ["nav.hello"] = ("Привет, {0}", "Hi, {0}"),
        ["nav.profile"] = ("Профиль", "Profile"),
        ["nav.logout"] = ("Выйти", "Log out"),
        ["nav.login"] = ("Войти", "Log in"),
        ["nav.register"] = ("Регистрация", "Sign up"),

        ["brand.tagline"] = ("Фильмы для вас", "Movies for you"),
        ["brand.title"] = ("Умный поиск, который подстраивается под то, что вы уже смотрели", "Smart search that adapts to what you've already watched"),

        ["footer.tmdbPrefix"] = ("Данные о фильмах предоставлены", "Movie data provided by"),
        ["footer.tmdbSuffix"] = ("Этот продукт использует API TMDb, но не одобрен и не сертифицирован TMDb.", "This product uses the TMDb API but is not endorsed or certified by TMDb."),

        ["hero.eyebrow"] = ("Сайт для открытия фильмов, а не для просмотра", "A site for discovering movies, not streaming them"),
        ["hero.title"] = ("Поиск по каталогу TheMovieDB и рекомендации, которые меняются от ваших последних просмотров.", "Search the TheMovieDB catalog and get recommendations that evolve with what you've recently watched."),
        ["hero.subtitle"] = (
            "Выбирайте несколько жанров одновременно, открывайте карточки фильмов и ставьте оценки — сервис подтягивает актуальные данные из TMDb и перестраивает выдачу под ваш вкус.",
            "Pick several genres at once, open movie cards and rate them — the service pulls live data from TMDb and rebuilds the feed around your taste."),
        ["hero.stat.movies"] = ("фильмов доступно в TMDb", "movies available on TMDb"),
        ["hero.stat.genres"] = ("жанров в умном поиске", "genres in smart search"),
        ["hero.stat.ratings"] = ("оценок уже учитывается", "ratings counted so far"),
        ["hero.personalization"] = ("Персонализация", "Personalization"),
        ["hero.nextMovie"] = ("Ваш следующий фильм", "Your next movie"),
        ["hero.openHint"] = (
            "Откройте любую карточку, чтобы закрепить последний просмотр и увидеть точнее подобранные рекомендации.",
            "Open any card to log a view and get more precisely tailored recommendations."),

        ["error.title"] = ("Не удалось загрузить данные", "Failed to load data"),
        ["error.apiKeyMissing"] = (
            "API-ключ TMDb не настроен. Добавьте его в appsettings.json (раздел TMDb:ApiKey).",
            "TMDb API key is not configured. Add it to appsettings.json (TMDb:ApiKey)."),

        ["status.withTotal"] = ("{0} из {1:N0} найденных в TMDb", "{0} of {1:N0} found on TMDb"),
        ["status.simple"] = ("{0} фильмов", "{0} movies"),

        ["search.label"] = ("Smart search", "Smart search"),
        ["search.heading"] = ("Выбор сразу нескольких жанров", "Pick several genres at once"),
        ["search.clear"] = ("Сбросить фильтры", "Clear filters"),
        ["search.inputLabel"] = ("Поиск по названию фильма", "Search by movie title"),
        ["search.placeholder"] = ("Например: Дюна, Начало, Интерстеллар", "e.g. Dune, Inception, Interstellar"),
        ["search.sortLabel"] = ("Сортировка", "Sort by"),
        ["search.genresUnrestricted"] = ("Жанры не ограничены", "All genres"),

        ["sort.recommended"] = ("По рекомендациям", "Recommended"),
        ["sort.popular"] = ("По популярности", "Most popular"),
        ["sort.newest"] = ("Сначала новые", "Newest first"),
        ["sort.title"] = ("По названию", "Title"),

        ["catalog.label"] = ("Каталог", "Catalog"),
        ["catalog.heading"] = ("Фильмы, которые подходят под текущий вкус", "Movies matching your taste"),
        ["catalog.empty.title"] = ("Ничего не найдено", "Nothing found"),
        ["catalog.empty.body"] = ("Снимите часть жанров или попробуйте другой запрос.", "Remove some genres or try a different search."),
        ["catalog.loadMore"] = ("Показать ещё", "Show more"),
        ["catalog.loading"] = ("Загрузка…", "Loading…"),
        ["catalog.personalizationHint"] = (
            "Персонализация появится после нескольких оценок — пока показываем общую популярность.",
            "Personalization kicks in after a few ratings — showing general popularity for now."),

        ["recs.label"] = ("Рекомендации", "Recommendations"),
        ["recs.heading"] = ("Подстраиваются под последние просмотры и оценки", "Adapts to your recent views and ratings"),
        ["recs.empty"] = ("Оцените или откройте пару фильмов, чтобы увидеть персональные подборки.", "Rate or open a couple of movies to see personal picks."),
        ["recs.reason.popular"] = ("подбирается по общей популярности TMDb", "picked from general TMDb popularity"),
        ["recs.reason.topGenres"] = ("попадает в ваши любимые жанры: {0}", "matches your favorite genres: {0}"),
        ["recs.reason.recentInterests"] = ("перекликается с вашими последними интересами: {0}", "echoes your recent interests: {0}"),
        ["recs.reason.reinforces"] = ("усиливает уже высоко оценённые вами истории", "builds on stories you've already rated highly"),
        ["recs.reason.complements"] = ("хорошо дополняет ваш текущий профиль вкуса", "complements your current taste profile"),

        ["history.label"] = ("Недавние просмотры", "Recently viewed"),
        ["history.heading"] = ("История, на которой строятся рекомендации", "History that powers your recommendations"),
        ["history.empty"] = ("Пока пусто. Откройте несколько карточек, чтобы персонализация стала заметнее.", "Empty for now. Open a few cards to make personalization noticeable."),
        ["history.authHint.login"] = ("Войдите", "Log in"),
        ["history.authHint.or"] = ("или", "or"),
        ["history.authHint.register"] = ("зарегистрируйтесь", "sign up"),
        ["history.authHint.suffix"] = ("чтобы история и оценки сохранялись между сеансами.", "so your history and ratings are saved between sessions."),

        ["modal.year"] = ("Год", "Year"),
        ["modal.duration"] = ("Длительность", "Duration"),
        ["modal.rating"] = ("Рейтинг TMDb", "TMDb rating"),
        ["modal.yourRating"] = ("Ваша оценка", "Your rating"),
        ["modal.loginPrefix"] = ("Войдите", "Log in"),
        ["modal.loginSuffix"] = (", чтобы оценка сохранилась.", " to save your rating."),
        ["modal.noYear"] = ("—", "—"),

        ["auth.login.title"] = ("Вход", "Log in"),
        ["auth.login.subtitle"] = ("Войдите, чтобы ваши просмотры и оценки сохранялись между сеансами.", "Log in so your views and ratings are saved between sessions."),
        ["auth.email"] = ("Email", "Email"),
        ["auth.password"] = ("Пароль", "Password"),
        ["auth.submit.login"] = ("Войти", "Log in"),
        ["auth.switch.noAccount"] = ("Ещё нет аккаунта?", "Don't have an account?"),
        ["auth.switch.toRegister"] = ("Зарегистрироваться", "Sign up"),
        ["auth.register.title"] = ("Регистрация", "Sign up"),
        ["auth.register.subtitle"] = (
            "Создайте аккаунт, чтобы система запоминала, что вы смотрели, и подбирала фильмы под ваш вкус.",
            "Create an account so the system remembers what you've watched and picks movies for your taste."),
        ["auth.username"] = ("Имя пользователя", "Username"),
        ["auth.confirmPassword"] = ("Повторите пароль", "Confirm password"),
        ["auth.submit.register"] = ("Зарегистрироваться", "Sign up"),
        ["auth.switch.haveAccount"] = ("Уже есть аккаунт?", "Already have an account?"),
        ["auth.switch.toLogin"] = ("Войти", "Log in"),
        ["auth.error.invalidCredentials"] = ("Неверный email или пароль.", "Incorrect email or password."),
        ["auth.error.lockedOut"] = ("Слишком много неудачных попыток. Попробуйте позже.", "Too many failed attempts. Try again later."),
        ["auth.error.duplicateUserName"] = ("Это имя пользователя уже занято.", "This username is already taken."),
        ["auth.error.duplicateEmail"] = ("Этот email уже зарегистрирован.", "This email is already registered."),
        ["auth.error.passwordTooShort"] = ("Пароль слишком короткий (минимум 6 символов).", "Password is too short (minimum 6 characters)."),

        ["profile.title"] = ("Профиль", "Profile"),
        ["profile.subtitle"] = ("Ваша история просмотров и оценок", "Your watch and rating history"),
        ["profile.stat.watched"] = ("просмотрено", "watched"),
        ["profile.stat.rated"] = ("оценено", "rated"),
        ["profile.preferences.title"] = ("Настройки рекомендаций", "Recommendation settings"),
        ["profile.preferences.yearRange"] = ("Ограничить рекомендации годами выпуска", "Limit recommendations by release years"),
        ["profile.preferences.yearFrom"] = ("С", "From"),
        ["profile.preferences.yearTo"] = ("По", "To"),
        ["profile.preferences.hint"] = (
            "Рекомендации будут учитывать диапазон ±2 года от указанных значений. По умолчанию фильтр выключен.",
            "Recommendations will consider a range of ±2 years around these values. Disabled by default."),
        ["profile.preferences.save"] = ("Сохранить", "Save"),
        ["profile.preferences.saved"] = ("Настройки сохранены.", "Preferences saved."),
        ["profile.preferences.invalidRange"] = ("Год «с» должен быть меньше или равен году «по».", "The \"from\" year must be less than or equal to the \"to\" year."),
        ["profile.history.title"] = ("История", "History"),
        ["profile.history.empty"] = ("Вы пока не смотрели фильмы.", "You haven't watched any movies yet."),
        ["profile.history.rated"] = ("Оценка", "Rating"),
        ["profile.history.notRated"] = ("Без оценки", "Not rated"),
        ["profile.history.viewedOn"] = ("Просмотрено", "Viewed"),
        ["profile.backHome"] = ("На главную", "Back to home"),
    };

    public static string Get(string key, AppLanguage language)
    {
        if (!Map.TryGetValue(key, out var value))
        {
            return key;
        }

        return language == AppLanguage.En ? value.En : value.Ru;
    }
}
