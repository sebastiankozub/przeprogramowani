using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MovieDate.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options), IDataProtectionKeyContext
{
    // Data Protection keys live in the database so auth cookies stay valid across Fly machines and restarts.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<WatchedMovie> WatchedMovies => Set<WatchedMovie>();

    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();

    public DbSet<City> Cities => Set<City>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<City>(city =>
        {
            city.Property(c => c.WikidataId).HasMaxLength(City.WikidataIdMaxLength);
            city.Property(c => c.Name).HasMaxLength(City.NameMaxLength);
            city.Property(c => c.Voivodeship).HasMaxLength(City.VoivodeshipMaxLength);
            city.HasIndex(c => c.WikidataId).IsUnique();
        });

        builder.Entity<AppUser>(user =>
        {
            // Catalog rows are never deleted while a user lives there.
            user.HasOne(u => u.City)
                .WithMany()
                .HasForeignKey(u => u.CityId)
                .OnDelete(DeleteBehavior.Restrict);
            user.HasIndex(u => u.CityId);
        });

        builder.Entity<Movie>(movie =>
        {
            movie.Property(m => m.WikidataId).HasMaxLength(Movie.WikidataIdMaxLength);
            movie.Property(m => m.Title).HasMaxLength(Movie.TitleMaxLength);
            movie.Property(m => m.OriginalTitle).HasMaxLength(Movie.TitleMaxLength);
            movie.HasIndex(m => m.WikidataId).IsUnique();
        });

        builder.Entity<WatchedMovie>(watched =>
        {
            watched.HasKey(w => new { w.UserId, w.MovieId });

            // Keyed on AspNetUsers.Id (never the email) — deleting the account removes its ratings.
            watched.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Catalog rows are never deleted while rated.
            watched.HasOne(w => w.Movie)
                .WithMany()
                .HasForeignKey(w => w.MovieId)
                .OnDelete(DeleteBehavior.Restrict);

            watched.ToTable(t => t.HasCheckConstraint(
                "CK_WatchedMovies_Rating",
                $"\"Rating\" BETWEEN {WatchedMovie.MinRating} AND {WatchedMovie.MaxRating}"));
        });

        builder.Entity<WatchlistItem>(item =>
        {
            item.HasKey(w => new { w.UserId, w.MovieId });

            // Keyed on AspNetUsers.Id (never the email) — deleting the account removes its watchlist.
            item.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(w => w.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Catalog rows are never deleted while on a watchlist.
            item.HasOne(w => w.Movie)
                .WithMany()
                .HasForeignKey(w => w.MovieId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
