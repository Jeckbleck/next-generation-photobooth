using Microsoft.EntityFrameworkCore;
using Photobooth.Data.Models;

namespace Photobooth.Data
{
    public class PhotoboothDbContext : DbContext
    {
        private static readonly string DbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Photobooth", "photobooth.db");

        public DbSet<Event>   Events   { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<Photo>   Photos   { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite($"Data Source={DbPath}");

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Event>(e =>
            {
                e.HasIndex(x => x.Slug).IsUnique();
                // Soft-delete filter — archived events are excluded from normal queries
                e.HasQueryFilter(x => x.ArchivedAt == null);
            });

            model.Entity<Session>(s =>
            {
                // Prevent accidental cascade-delete of sessions when an event is deleted
                s.HasOne(x => x.Event)
                 .WithMany(x => x.Sessions)
                 .HasForeignKey(x => x.EventId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            model.Entity<Photo>(p =>
            {
                // Deleting a session removes its photos
                p.HasOne(x => x.Session)
                 .WithMany(x => x.Photos)
                 .HasForeignKey(x => x.SessionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
