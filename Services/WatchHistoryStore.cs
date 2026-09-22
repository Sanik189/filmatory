using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POP.Data;
using POP.Models;

namespace POP.Services;

public sealed class WatchHistoryStore(ApplicationDbContext db)
{
    // MovieLibraryState fires persistence calls without awaiting them, so rapid clicks can overlap;
    // a single DbContext instance isn't safe for concurrent operations, hence the serializing gate.
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<WatchedMovie>> GetAllAsync(string userId)
    {
        await gate.WaitAsync();
        try
        {
            return await db.WatchedMovies
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.ViewedAt)
                .ToListAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordViewAsync(string userId, Movie movie)
    {
        await gate.WaitAsync();
        try
        {
            var entry = await db.WatchedMovies.FirstOrDefaultAsync(w => w.UserId == userId && w.MovieId == movie.Id);

            if (entry is null)
            {
                db.WatchedMovies.Add(BuildEntry(userId, movie));
            }
            else
            {
                entry.ViewedAt = DateTime.UtcNow;
                entry.Title = movie.Title;
                entry.PosterUrl = movie.PosterUrl;
                entry.Year = movie.Year;
                entry.GenresJson = JsonSerializer.Serialize(movie.Genres);
            }

            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecordRatingAsync(string userId, Movie movie, int rating)
    {
        await gate.WaitAsync();
        try
        {
            var entry = await db.WatchedMovies.FirstOrDefaultAsync(w => w.UserId == userId && w.MovieId == movie.Id);

            if (entry is null)
            {
                entry = BuildEntry(userId, movie);
                db.WatchedMovies.Add(entry);
            }

            entry.Rating = rating;
            entry.RatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    private static WatchedMovie BuildEntry(string userId, Movie movie) => new()
    {
        UserId = userId,
        MovieId = movie.Id,
        Title = movie.Title,
        PosterUrl = movie.PosterUrl,
        Year = movie.Year,
        GenresJson = JsonSerializer.Serialize(movie.Genres),
        ViewedAt = DateTime.UtcNow
    };
}
