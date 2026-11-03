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

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Message> Messages => Set<Message>();

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
            user.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength);

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

        builder.Entity<Match>(match =>
        {
            match.HasKey(m => new { m.UserId, m.CandidateId });

            // Both sides keyed on AspNetUsers.Id (never the email) — deleting either account removes the pair.
            match.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            match.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.CandidateId)
                .OnDelete(DeleteBehavior.Cascade);

            // The PK serves lookups by UserId; this index serves lookups by CandidateId.
            match.HasIndex(m => m.CandidateId);

            match.ToTable(t => t.HasCheckConstraint("CK_Matches_NotSelf", "\"UserId\" <> \"CandidateId\""));
        });

        builder.Entity<Friendship>(friendship =>
        {
            friendship.HasKey(f => new { f.UserId, f.FriendId });

            // Both sides keyed on AspNetUsers.Id (never the email) — deleting either account removes the friendship.
            friendship.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            friendship.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(f => f.FriendId)
                .OnDelete(DeleteBehavior.Cascade);

            // The PK serves "whom I added"; this index serves "who added me".
            friendship.HasIndex(f => f.FriendId);

            friendship.ToTable(t => t.HasCheckConstraint("CK_Friendships_NotSelf", "\"UserId\" <> \"FriendId\""));
        });

        builder.Entity<Message>(message =>
        {
            message.Property(m => m.Body).HasMaxLength(Message.BodyMaxLength);

            // Both sides keyed on AspNetUsers.Id (never the email) — deleting either account removes the messages.
            message.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Cascade);

            message.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.RecipientId)
                .OnDelete(DeleteBehavior.Cascade);

            // Serves reading one direction of a pair's conversation by time.
            message.HasIndex(m => new { m.SenderId, m.RecipientId, m.SentAt, m.Id });

            message.ToTable(t => t.HasCheckConstraint("CK_Messages_NotSelf", "\"SenderId\" <> \"RecipientId\""));
        });
    }
}
