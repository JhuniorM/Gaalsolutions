using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Gaalsolutions.Monitoring.Models;

namespace Gaalsolutions.Monitoring.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(
            DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<NotificationHistoryModel> NotificationHistories { get; set; }
    }

    public sealed class PostgreSqlApplicationDbContext : ApplicationDbContext
    {
        public PostgreSqlApplicationDbContext(
            DbContextOptions<PostgreSqlApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<NotificationHistoryModel>()
                .Property(notification => notification.SentAt)
                .HasColumnType("timestamp without time zone");
        }
    }
}