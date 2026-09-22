using POP.Models;

namespace POP.Services;

public sealed class MovieLibraryState
{
    private const int RecentViewLimit = 6;
    private const string SortRecommended = "recommended";
    private const string SortPopular = "popular";
    private const string SortNewest = "newest";
    private const string SortTitle = "title";

    private readonly List<Movie> movies = MovieCatalog.GetAll().ToList();
    private readonly HashSet<string> selectedGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> ratings = new();
    private readonly LinkedList<int> recentViews = new();
    private readonly IReadOnlyList<(string Value, string Label)> sortOptions =
    [
        (SortRecommended, "По рекомендациям"),
        (SortPopular, "По популярности"),
        (SortNewest, "Сначала новые"),
        (SortTitle, "По названию")
    ];

    public event Action? Changed;

    public IReadOnlyList<Movie> Movies => movies;

    public IReadOnlyList<(string Value, string Label)> SortOptions => sortOptions;

    public IReadOnlyList<string> AvailableGenres { get; } = MovieCatalog
        .GetAll()
        .SelectMany(movie => movie.Genres)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(genre => genre)
        .ToArray();

    public string SearchQuery { get; private set; } = string.Empty;

    public string SortMode { get; private set; } = SortRecommended;

    public IReadOnlyCollection<string> SelectedGenres => selectedGenres;

    public Movie? SelectedMovie => GetMovieById(SelectedMovieId);

    public int? SelectedMovieId { get; private set; }

    public IReadOnlyList<Movie> FilteredMovies => GetFilteredMovies().ToList();

    public IReadOnlyList<MovieRecommendation> Recommendations => GetRecommendations().ToList();

    public IReadOnlyList<Movie> RecentMovies => recentViews
        .Select(GetMovieById)
        .Where(movie => movie is not null)
        .Select(movie => movie!)
        .ToList();

    public int TotalMovies => movies.Count;

    public int TotalRatings => ratings.Count;

    public string StatusSummary => $"{FilteredMovies.Count} из {movies.Count} фильмов";

    public void SetSearchQuery(string query)
    {
        SearchQuery = query.Trim();
        NotifyChanged();
    }

    public void SetSortMode(string sortMode)
    {
        SortMode = sortMode switch
        {
            SortPopular or SortNewest or SortTitle => sortMode,
            _ => SortRecommended
        };

        NotifyChanged();
    }

    public void ToggleGenre(string genre)
    {
        if (!selectedGenres.Add(genre))
        {
            selectedGenres.Remove(genre);
        }

        NotifyChanged();
    }

    public void ClearFilters()
    {
        SearchQuery = string.Empty;
        SortMode = SortRecommended;
        selectedGenres.Clear();
        NotifyChanged();
    }

    public void SelectMovie(Movie movie)
    {
        SelectedMovieId = movie.Id;
        TouchRecentView(movie.Id);
        NotifyChanged();
    }

    public void RateMovie(Movie movie, int rating)
    {
        if (rating is < 1 or > 5)
        {
            return;
        }

        ratings[movie.Id] = rating;
        SelectedMovieId = movie.Id;
        TouchRecentView(movie.Id);
        NotifyChanged();
    }

    public int GetRating(Movie movie) => ratings.TryGetValue(movie.Id, out var rating) ? rating : 0;

    public string DescribeRecommendation(Movie movie)
    {
        var affinity = BuildGenreAffinity();
        var matchingGenres = movie.Genres
            .Where(affinity.ContainsKey)
            .OrderByDescending(genre => affinity[genre])
            .Take(2)
            .ToArray();

        var rating = GetRating(movie);

        if (matchingGenres.Length > 0 && rating >= 4)
        {
            return $"попадает в ваши любимые жанры: {string.Join(", ", matchingGenres)}";
        }

        if (matchingGenres.Length > 0)
        {
            return $"собирает ваши последние интересы: {string.Join(", ", matchingGenres)}";
        }

        if (RecentMovies.Count == 0)
        {
            return "подбирается по общей популярности и свежести каталога";
        }

        return rating >= 4
            ? "усиливает уже высоко оценённые вами истории"
            : "хорошо дополняет ваш текущий профиль вкуса";
    }

    private IEnumerable<Movie> GetFilteredMovies()
    {
        IEnumerable<Movie> query = movies;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var terms = SearchQuery
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            query = query.Where(movie => MatchesSearch(movie, terms));
        }

        if (selectedGenres.Count > 0)
        {
            query = query.Where(movie => movie.Genres.Any(selectedGenres.Contains));
        }

        return Sort(query);
    }

    private IEnumerable<Movie> Sort(IEnumerable<Movie> moviesToSort)
    {
        return SortMode switch
        {
            SortPopular => moviesToSort.OrderByDescending(movie => movie.CommunityScore).ThenBy(movie => movie.Title),
            SortNewest => moviesToSort.OrderByDescending(movie => movie.Year).ThenByDescending(movie => movie.CommunityScore),
            SortTitle => moviesToSort.OrderBy(movie => movie.Title),
            _ => moviesToSort
                .OrderByDescending(movie => GetSearchAndTasteScore(movie))
                .ThenByDescending(movie => movie.CommunityScore)
                .ThenBy(movie => movie.Title)
        };
    }

    private IEnumerable<MovieRecommendation> GetRecommendations()
    {
        var affinity = BuildGenreAffinity();

        return movies
            .Where(movie => movie.Id != SelectedMovieId)
            .Select(movie => new MovieRecommendation(
                movie,
                GetRecommendationScore(movie, affinity),
                DescribeRecommendation(movie),
                movie.Genres.Where(affinity.ContainsKey).OrderByDescending(genre => affinity[genre]).Take(3).ToArray()))
            .OrderByDescending(recommendation => recommendation.Score)
            .ThenByDescending(recommendation => recommendation.Movie.CommunityScore)
            .Take(5);
    }

    private double GetSearchAndTasteScore(Movie movie)
    {
        var score = (double)movie.CommunityScore * 3;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery;

            if (movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 42;
            }

            if (movie.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                score += 24;
            }

            score += movie.Genres.Count(genre => genre.Contains(query, StringComparison.OrdinalIgnoreCase)) * 18;
            score += movie.Tags.Count(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)) * 16;
        }

        if (selectedGenres.Count > 0)
        {
            score += movie.Genres.Count(selectedGenres.Contains) * 14;
        }

        var rating = GetRating(movie);
        if (rating > 0)
        {
            score += rating * 5;
        }

        return score;
    }

    private int GetRecommendationScore(Movie movie, IReadOnlyDictionary<string, double>? affinity = null)
    {
        affinity ??= BuildGenreAffinity();

        var score = (int)Math.Round(movie.CommunityScore * 10);

        foreach (var genre in movie.Genres)
        {
            if (affinity.TryGetValue(genre, out var value))
            {
                score += (int)Math.Round(value * 38);
            }
        }

        var rating = GetRating(movie);
        if (rating >= 4)
        {
            score += 20;
        }
        else if (rating is 1 or 2)
        {
            score -= 8;
        }

        if (movie.Year >= DateTime.Now.Year - 2)
        {
            score += 8;
        }

        return score;
    }

    private Dictionary<string, double> BuildGenreAffinity()
    {
        var affinity = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;

        foreach (var movieId in recentViews)
        {
            var movie = GetMovieById(movieId);
            if (movie is null)
            {
                continue;
            }

            var rating = GetRating(movie);
            var recencyWeight = 1.0 - Math.Min(depth * 0.14, 0.56);
            var ratingWeight = rating switch
            {
                5 => 1.6,
                4 => 1.35,
                3 => 1.0,
                2 => 0.72,
                1 => 0.45,
                _ => 0.9
            };

            foreach (var genre in movie.Genres)
            {
                affinity[genre] = affinity.TryGetValue(genre, out var existing)
                    ? existing + recencyWeight * ratingWeight
                    : recencyWeight * ratingWeight;
            }

            depth++;
        }

        foreach (var (movieId, rating) in ratings)
        {
            var movie = GetMovieById(movieId);
            if (movie is null)
            {
                continue;
            }

            var ratingWeight = rating switch
            {
                5 => 0.9,
                4 => 0.7,
                3 => 0.25,
                2 => -0.45,
                1 => -0.7,
                _ => 0
            };

            foreach (var genre in movie.Genres)
            {
                affinity[genre] = affinity.TryGetValue(genre, out var existing)
                    ? existing + ratingWeight
                    : ratingWeight;
            }
        }

        return affinity;
    }

    private static bool MatchesSearch(Movie movie, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return true;
        }

        return terms.All(term =>
            movie.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            movie.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            movie.Genres.Any(genre => genre.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            movie.Tags.Any(tag => tag.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private Movie? GetMovieById(int? movieId)
    {
        if (movieId is null)
        {
            return null;
        }

        return movies.FirstOrDefault(movie => movie.Id == movieId.Value);
    }

    private Movie? GetMovieById(int movieId) => movies.FirstOrDefault(movie => movie.Id == movieId);

    private void TouchRecentView(int movieId)
    {
        recentViews.Remove(movieId);
        recentViews.AddFirst(movieId);

        while (recentViews.Count > RecentViewLimit)
        {
            recentViews.RemoveLast();
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}

public sealed record MovieRecommendation(
    Movie Movie,
    int Score,
    string Reason,
    IReadOnlyList<string> MatchingGenres);