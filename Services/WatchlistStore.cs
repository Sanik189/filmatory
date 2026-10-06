using Microsoft.EntityFrameworkCore;
using POP.Data;
using POP.Models;

namespace POP.Services;

public sealed class WatchlistStore(ApplicationDbContext db)
{
    // Same rationale as WatchHistoryStore: fire-and-forget callers can overlap on one DbContext.
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<List<WatchlistItem>> GetAllAsync(string userId)
    {
        await gate.WaitAsync();
        try
        {
            return await db.WatchlistItems
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.AddedAt)
                .ToListAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task AddAsync(string userId, Movie movie)
    {
        await gate.WaitAsync();
        try
        {
            var exists = await db.WatchlistItems.AnyAsync(w => w.UserId == userId && w.MovieId == movie.Id && w.Type == movie.Type);
            if (exists)
            {
                return;
            }

            db.WatchlistItems.Add(new WatchlistItem
            {
                UserId = userId,
                MovieId = movie.Id,
                Type = movie.Type,
                Title = movie.Title,
                PosterUrl = movie.PosterUrl,
                Year = movie.Year,
                AddedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAsync(string userId, MediaKey key)
    {
        await gate.WaitAsync();
        try
        {
            var entry = await db.WatchlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.MovieId == key.Id && w.Type == key.Type);
            if (entry is not null)
            {
                db.WatchlistItems.Remove(entry);
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
