using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using POP.Models;

namespace POP.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<WatchedMovie> WatchedMovies => Set<WatchedMovie>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<WatchedMovie>(entity =>
        {
            entity.HasIndex(w => new { w.UserId, w.MovieId }).IsUnique();
            entity.Property(w => w.Title).HasMaxLength(512).IsRequired();
            entity.Property(w => w.PosterUrl).HasMaxLength(512);
            entity.Property(w => w.GenresJson).HasMaxLength(1024);
        });
    }
}
